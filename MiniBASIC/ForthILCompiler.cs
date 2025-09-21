using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace MiniBasicIL
{
    /// <summary>
    /// ForthILCompiler (pointer-style variables)
    /// - VARIABLE name : defines a word that pushes the "address" (slot id) of a scalar
    /// - @  ( addr -- x )    : indirect read
    /// - !  ( x addr -- )    : indirect write
    /// - ARRAY  A N          : defines 1D numeric array A(0..N); word A pushes its "address" (array slot id)
    /// - ARRAY2 M NX NY      : defines 2D numeric array M(0..NX,0..NY); word M pushes array slot id
    /// - []@  ( addr i -- x ), []!  ( x addr i -- )
    /// - []@2 ( addr i j -- x ), []!2 ( x addr i j -- )
    ///
    /// Supports subset:
    ///   Literals: numbers, "string"
    ///   Stack   : DUP DROP SWAP OVER ROT
    ///   Arithmetic: + - * / ^ MOD
    ///   Compare : = <> < <= > >=
    ///   Output  : .  CR  EMIT
    ///   Bitwise : BAND BOR BXOR
    ///   Control : IF ELSE THEN, DO ... LOOP [UNTIL expr], FOR var ... NEXT [var]
    ///   Dictionary: : NAME ... ; and WORDS
    ///   BASIC bridge: SIN COS TAN ATN SQR ABS LOG EXP INT VAL STR$ LEN CHR$ ASC RND PI TIMER etc.
    ///
    /// Notes:
    /// - Emits Op[] executable by VM. BASIC mode unaffected.
    /// - Requires OpCode: LOAD_IND, STORE_IND, LOAD_ARR_IND, STORE_ARR_IND.
    /// - S@/S!（C@/C!） are rerouted to []@/[]! on the special S array (lazy DIM 0..255).
    /// </summary>
    /// 

    


    sealed class ForthILCompiler
    {
        // ----- User dictionary (macros) -----
        private readonly Dictionary<string, List<string>> dict = new(StringComparer.OrdinalIgnoreCase);

        // ----- Variable/Array name registries -----
        private readonly HashSet<string> varWords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> arr1Words = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> arr2Words = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> variables = new(StringComparer.OrdinalIgnoreCase); // for FOR/NEXT var recognition

        // ForthILCompiler のフィールド
        private List<int> lastCols = new();

        // ----- Definition state -----
        private bool compiling = false;
        private string currentWord = "";
        private List<string>? currentBody = null;

        // ----- Symbol table -----
        private readonly Symtab sym = new();

        // ----- Special S array (lazy DIM to 0..255) -----
        private bool arraySDimmed = false;

        // ----- Control flow stacks (shared while compiling a single line or macro expansion) -----
        private readonly Stack<int> ifStack = new();
        private readonly Stack<int> elseStack = new();
        private readonly Stack<(int slot, int checkPc)> forStack = new();
        private readonly Stack<int> doStackF = new();

        // ForthILCompiler クラスに追加
        public void ClearAll()
        {
            dict.Clear();                   // ユーザー定義語
            varWords.Clear();
            arr1Words.Clear();
            arr2Words.Clear();
            variables.Clear();

            compiling = false;
            currentWord = "";
            currentBody = null;

            arraySDimmed = false;

            ifStack.Clear();
            elseStack.Clear();
            forStack.Clear();
            doStackF.Clear();

            sym.Clear();                    // ← 1) で追加した Symtab.Clear()
        }

        public CompiledProgram CompileLine(string line)
        {
            var toks = Tokenize(line);
            int i = 0;

            var ops = new List<Op>();
            var pcLines = new List<int>();
            void Emit(Op op) { ops.Add(op); pcLines.Add(0); }

            while (i < toks.Count)
            {
                string tok = toks[i++];

                // ----- Definition mode -----
                if (compiling)
                {
                    if (tok == ";")
                    {
                        if (string.IsNullOrEmpty(currentWord) || currentBody == null)
                            throw new Exception("FORTH: compile error in ':' definition");
                        dict[currentWord] = new List<string>(currentBody);
                        compiling = false; currentWord = ""; currentBody = null;
                        continue;
                    }
                    // ★ 追加：定義中の行内コメント
                    if (tok == "\\")
                    {
                        // この行の残りトークンは body に入れない（行末までコメント）
                        while (i < toks.Count) i++;
                        continue;
                    }
                    if (string.IsNullOrEmpty(currentWord))
                    {
                        currentWord = tok;
                        continue;
                    }
                    currentBody!.Add(tok);
                    continue;
                }

                // Start definition or line comment
                if (tok == ":") { compiling = true; currentWord = ""; currentBody = new List<string>(); continue; }
                if (tok == "\\") break; // rest of line comment

                // ----- Macro expansion -----
                if (dict.TryGetValue(tok, out var body))
                {
                    EmitTokens(body, Emit, ops, pcLines);
                    continue;
                }

                // ----- String literal -----
                if (IsQuoted(tok)) { Emit(Op.Str(Unquote(tok))); continue; }

                // ----- Number literal -----
                if (TryNumber(tok, out var num)) { Emit(Op.Num(num)); continue; }

                // ----- Core words -----
                if (HandleCoreWord(tok, Emit, toks, ref i, ops, pcLines)) continue;

                // BASIC bridge & addresses
                if (FnId.TryFromName(tok.ToUpperInvariant(), out int fid))
                {
                    if (IsZeroArgFn(fid)) Emit(new Op(OpCode.CALLFN, a: fid, b: 0));
                    else Emit(new Op(OpCode.CALLFN, a: fid, b: 1));
                    continue;
                }
                if (varWords.Contains(tok)) { Emit(Op.Num(sym.GetScalarSlot(tok))); continue; }
                if (arr1Words.Contains(tok) || arr2Words.Contains(tok)) { Emit(Op.Num(sym.GetArraySlot(tok))); continue; }

                throw new Exception($"FORTH: UNKNOWN WORD '{tok}' at col {lastCols[i - 1] + 1}");
            }

            // Terminate
            ops.Add(new Op(OpCode.HALT)); pcLines.Add(0);

            return new CompiledProgram
            {
                Code = ops.ToArray(),
                PcToLine = pcLines.ToArray(),
                Symbols = sym,
                JumpTables = new List<int[]>(),
                LineToPc = new Dictionary<int, int> { { 0, 0 } }
            };
        }

        // ---- shared handler for top-level and macro expansion ----
        private bool HandleCoreWord(string tok, Action<Op> Emit, List<string> toks, ref int i, List<Op> ops, List<int> pcLines)
        {
            switch (tok.ToUpperInvariant())
            {
                // Stack
                case "DUP": Emit(new Op(OpCode.DUP)); return true;
                case "DROP": Emit(new Op(OpCode.DROP)); return true;
                case "SWAP": Emit(new Op(OpCode.SWAP)); return true;
                case "OVER": Emit(new Op(OpCode.OVER)); return true;
                case "ROT": Emit(new Op(OpCode.ROT)); return true;

                // Arithmetic
                case "+": Emit(new Op(OpCode.ADD)); return true;
                case "-": Emit(new Op(OpCode.SUB)); return true;
                case "*": Emit(new Op(OpCode.MUL)); return true;
                case "/": Emit(new Op(OpCode.DIV)); return true;
                case "^": Emit(new Op(OpCode.POW)); return true;
                case "MOD": Emit(new Op(OpCode.MOD)); return true;
                case "NOT": Emit(new Op(OpCode.NOT)); return true;
                case "AND": Emit(new Op(OpCode.AND)); return true;
                case "OR": Emit(new Op(OpCode.OR)); return true;
                case "NEG":
                case "NEGATE": Emit(new Op(OpCode.NEG)); return true;


                // Compare
                case "=": Emit(new Op(OpCode.CEQ)); return true;
                case "<>": Emit(new Op(OpCode.CNE)); return true;
                case "<": Emit(new Op(OpCode.CLT)); return true;
                case "<=": Emit(new Op(OpCode.CLE)); return true;
                case ">": Emit(new Op(OpCode.CGT)); return true;
                case ">=": Emit(new Op(OpCode.CGE)); return true;

                // ===== Graphics (BASIC-compatible) ====// ForthILCompiler.cs の HandleCoreWord 内 switch に追記
                // （位置はどこでも可。既存のケースの下あたりに。）

                // ===== Graphics (BASIC互換; 可変長ではなく形別の固定argcを渡す) =====
                // ===== Graphics (BASIC互換; 固定argc) =====
                // ===== Graphics (BASIC互換; FORTH用エイリアスも含む) =====
                case "SCREEN":   // w h
                    Emit(new Op(OpCode.CALLFN, a: FnId.SCREEN, b: 2)); return true;

                case "CLS":
                    Emit(new Op(OpCode.CALLFN, a: FnId.GCLS, b: 0)); return true;

                case "FLUSH":
                    Emit(new Op(OpCode.CALLFN, a: FnId.GFLUSH, b: 0)); return true;

                case "COLOR":    // r g b
                    Emit(new Op(OpCode.CALLFN, a: FnId.GCOLOR, b: 3)); return true;

                case "COLORP":   // pal
                    Emit(new Op(OpCode.CALLFN, a: FnId.GCOLOR, b: 1)); return true;

                case "PSET":     // x y   （省略形でペン更新）
                    Emit(new Op(OpCode.CALLFN, a: FnId.GPSET, b: 2)); return true;

                case "PSETP":    // x y pal
                    Emit(new Op(OpCode.CALLFN, a: FnId.GPSET, b: 3)); return true;

                case "LINE":     // x1 y1 x2 y2
                    Emit(new Op(OpCode.CALLFN, a: FnId.GLINE, b: 4)); return true;

                case "LINEP":    // x1 y1 x2 y2 pal
                    Emit(new Op(OpCode.CALLFN, a: FnId.GLINE, b: 5)); return true;

                case "LINETO":   // 省略形：現在ペン→(x2,y2)
                    {
                        int argcWithFlag = (2 | (1 << 30)); // 上位ビットで省略形
                        Emit(new Op(OpCode.CALLFN, a: FnId.GLINE, b: argcWithFlag));
                        return true;
                    }
                case "LINETOP":  // 省略形 + パレット：現在ペン→(x2,y2), pal
                    {
                        int argcWithFlag = (3 | (1 << 30)); // (x2,y2,pal)
                        Emit(new Op(OpCode.CALLFN, a: FnId.GLINE, b: argcWithFlag));
                        return true;
                    }

                case "CIRCLE":   // x y r
                    Emit(new Op(OpCode.CALLFN, a: FnId.GCIRCLE, b: 3)); return true;

                case "CIRCLEP":  // x y r pal
                    Emit(new Op(OpCode.CALLFN, a: FnId.GCIRCLE, b: 4)); return true;

                case "BOX":      // x1 y1 x2 y2
                    Emit(new Op(OpCode.CALLFN, a: FnId.GBOX, b: 4)); return true;

                case "BOXF":     // x1 y1 x2 y2 1   （fill=1 を引数で渡す慣習）
                    Emit(new Op(OpCode.CALLFN, a: FnId.GBOX, b: 5)); return true;

                case "BOXP":     // x1 y1 x2 y2 fill pal
                    Emit(new Op(OpCode.CALLFN, a: FnId.GBOX, b: 6)); return true;

                case "LOCATE":   // テキスト座標
                    Emit(new Op(OpCode.CALLFN, a: FnId.GLOCATE, b: 2)); return true;

                case "GPRINT":   // 文字列1個想定（必要なら拡張可）
                    Emit(new Op(OpCode.CALLFN, a: FnId.GPRINT, b: 1)); return true;

                case "GHSV":     // h s v（COLORHSV と同じ）
                case "COLORHSV":
                    Emit(new Op(OpCode.CALLFN, a: FnId.GCOLORHSV, b: 3)); return true;

                case "SAVEIMAGE": // "path.png"
                    Emit(new Op(OpCode.CALLFN, a: FnId.GSAVE, b: 1)); return true;

                // （BASIC関数ブリッジ系）
                case "POINT":    // x y → flag
                    Emit(new Op(OpCode.CALLFN, a: FnId.GPOINT, b: 2)); return true;

                case "GLOCATE":  // x y
                    Emit(new Op(OpCode.CALLFN, a: FnId.GLOCATE, b: 2)); return true;

                case "SLEEP":    // ms
                    Emit(new Op(OpCode.CALLFN, a: FnId.GSLEEP, b: 1)); return true;



                // Output
                case ".":  // print value + a trailing space (like Forth .)
                    Emit(new Op(OpCode.PRINT));
                    Emit(Op.Str(" "));
                    Emit(new Op(OpCode.PRINT));
                    return true;
                case "CR": Emit(new Op(OpCode.PRINT_NL)); return true;
                case "EMIT":
                    Emit(new Op(OpCode.CALLFN, a: FnId.CHRS, b: 1)); // CHR$(n)
                    Emit(new Op(OpCode.PRINT));
                    return true;

                // Bitwise
                case "BAND": Emit(new Op(OpCode.BAND)); return true;
                case "BOR": Emit(new Op(OpCode.BOR)); return true;
                case "BXOR": Emit(new Op(OpCode.BXOR)); return true;

                // Dictionary admin
                case "WORDS":
                    {
                        string s = string.Join(' ', dict.Keys);
                        Emit(Op.Str(s)); Emit(new Op(OpCode.PRINT)); Emit(new Op(OpCode.PRINT_NL));
                        return true;
                    }


                // ===== Pointer-style variable & array =====
                case "@": Emit(new Op(OpCode.LOAD_IND)); return true;
                case "!": Emit(new Op(OpCode.STORE_IND)); return true;

                case "[]@": ops.Add(new Op(OpCode.LOAD_ARR_IND, b: 1)); pcLines.Add(0); return true;
                case "[]!": ops.Add(new Op(OpCode.STORE_ARR_IND, b: 1)); pcLines.Add(0); return true;
                case "[]@2": ops.Add(new Op(OpCode.LOAD_ARR_IND, b: 2)); pcLines.Add(0); return true;
                case "[]!2": ops.Add(new Op(OpCode.STORE_ARR_IND, b: 2)); pcLines.Add(0); return true;

                case "VARIABLE":
                    {
                        string name = NextTokenOrError(toks, ref i, "variable name");
                        variables.Add(name);
                        int slot = sym.GetScalarSlot(name);
                        // initialize to 0
                        Emit(Op.Num(0)); Emit(new Op(OpCode.STORE, a: slot));
                        // word 'name' pushes its address
                        varWords.Add(name);
                        return true;
                    }
                case "ARRAY":
                    {
                        string name = NextTokenOrError(toks, ref i, "array name");
                        string nTok = NextTokenOrError(toks, ref i, "array size");
                        if (!TryNumber(nTok, out var n)) throw new Exception("FORTH: ARRAY size must be number");
                        int arr = sym.GetArraySlot(name);
                        Emit(Op.Num(n));
                        Emit(new Op(OpCode.DIM_ARR, a: arr, b: 1));
                        arr1Words.Add(name);
                        return true;
                    }
                case "ARRAY2":
                    {
                        string name = NextTokenOrError(toks, ref i, "array2 name");
                        string nxTok = NextTokenOrError(toks, ref i, "nx");
                        string nyTok = NextTokenOrError(toks, ref i, "ny");
                        if (!TryNumber(nxTok, out var nx) || !TryNumber(nyTok, out var ny))
                            throw new Exception("FORTH: ARRAY2 sizes must be numbers");
                        int arr = sym.GetArraySlot(name);
                        Emit(Op.Num(nx)); Emit(Op.Num(ny));
                        Emit(new Op(OpCode.DIM_ARR, a: arr, b: 2));
                        arr2Words.Add(name);
                        return true;
                    }

                // S/C legacy helpers
                case "S@":
                case "C@":
                    {
                        EnsureSArrayOnce(Emit);
                        int arr = sym.GetArraySlot("S");
                        // Stack expects: (index). We need (addr index).
                        Emit(Op.Num(arr));
                        ops.Add(new Op(OpCode.SWAP)); pcLines.Add(0);
                        ops.Add(new Op(OpCode.LOAD_ARR_IND, b: 1)); pcLines.Add(0);
                        return true;
                    }
                case "S!":
                case "C!":
                    {
                        EnsureSArrayOnce(Emit);
                        int arr = sym.GetArraySlot("S");
                        // Stack: (val index) -> need (val addr index)
                        Emit(Op.Num(arr));            // (val index addr)
                        ops.Add(new Op(OpCode.ROT)); pcLines.Add(0); // -> index addr val
                        ops.Add(new Op(OpCode.ROT)); pcLines.Add(0); // -> addr val index
                        ops.Add(new Op(OpCode.SWAP)); pcLines.Add(0); // -> val addr index
                        ops.Add(new Op(OpCode.STORE_ARR_IND, b: 1)); pcLines.Add(0);
                        return true;
                    }

                // ===== Control flow =====
                case "IF":
                    ops.Add(new Op(OpCode.JZ, a: -1)); pcLines.Add(0);
                    ifStack.Push(ops.Count - 1);
                    return true;

                case "ELSE":
                    if (ifStack.Count == 0) throw new Exception("FORTH: ELSE WITHOUT IF");
                    ops.Add(new Op(OpCode.JMP, a: -1)); pcLines.Add(0);
                    elseStack.Push(ops.Count - 1);
                    {
                        int jzPos = ifStack.Pop();
                        ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);
                    }
                    return true;

                case "THEN":
                    if (elseStack.Count > 0)
                    {
                        int jmpPos = elseStack.Pop();
                        ops[jmpPos] = new Op(OpCode.JMP, a: ops.Count);
                    }
                    else if (ifStack.Count > 0)
                    {
                        int jzPos = ifStack.Pop();
                        ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);
                    }
                    else throw new Exception("FORTH: THEN WITHOUT IF/ELSE");
                    return true;

                case "DO":
                    doStackF.Push(ops.Count);
                    return true;

                case "LOOP":
                    {
                        if (doStackF.Count == 0) throw new Exception("FORTH: LOOP WITHOUT DO");
                        int startPc = doStackF.Pop();
                        if (i < toks.Count && string.Equals(toks[i], "UNTIL", StringComparison.OrdinalIgnoreCase))
                        {
                            i++; // consume UNTIL
                            if (i >= toks.Count) throw new Exception("FORTH: expected condition after UNTIL");
                            var condToks = toks.GetRange(i, toks.Count - i);
                            i = toks.Count;
                            EmitTokens(condToks, Emit, ops, pcLines);
                            // ( flag ) true=end, false=loop
                            ops.Add(new Op(OpCode.JZ, a: startPc)); pcLines.Add(0);
                        }
                        else
                        {
                            ops.Add(new Op(OpCode.JMP, a: startPc)); pcLines.Add(0);
                        }
                        return true;
                    }

                // FOR/NEXT : stack (start end step) FOR var ... NEXT [var]
                case "FOR":
                    {
                        string varName = NextTokenOrError(toks, ref i, "loop variable after FOR");
                        variables.Add(varName);
                        int slot = sym.GetScalarSlot(varName);

                        // stack: start end step
                        Emit(new Op(OpCode.ROT));                          // (end step start)
                        ops.Add(new Op(OpCode.STORE, a: slot)); pcLines.Add(0); // var := start

                        ops.Add(new Op(OpCode.FOR_INIT, a: slot)); pcLines.Add(0);
                        int bodyPc = ops.Count + 1;                          // FOR_CHECK の次 = 本文の先頭
                        ops.Add(new Op(OpCode.FOR_CHECK, a: slot, b: bodyPc)); pcLines.Add(0);
                        forStack.Push((slot, 0));
                        return true;
                    }
                case "NEXT":
                    {
                        int slot = -1;
                        if (i < toks.Count && variables.Contains(toks[i]))
                        {
                            slot = sym.GetScalarSlot(toks[i]); i++;
                        }
                        ops.Add(new Op(OpCode.FOR_INCR, a: slot)); pcLines.Add(0);
                        if (slot < 0)
                        {
                            if (forStack.Count > 0) forStack.Pop();
                        }
                        return true;
                    }
            }
            return false;
        }

        private void EnsureSArrayOnce(Action<Op> emit)
        {
            if (arraySDimmed) return;
            int arr = sym.GetArraySlot("S");
            emit(Op.Num(255));
            emit(new Op(OpCode.DIM_ARR, a: arr, b: 1));
            arraySDimmed = true;
        }

        private static bool TryNumber(string s, out double d)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

        private static bool IsQuoted(string t) => t.Length >= 2 && t[0] == '"' && t[^1] == '"';
        private static string Unquote(string t) => IsQuoted(t) ? t.Substring(1, t.Length - 2) : t;

        private static string NextTokenOrError(List<string> toks, ref int i, string what)
        {
            if (i >= toks.Count) throw new Exception($"FORTH: expected {what}");
            return toks[i++];
        }

        private static bool IsZeroArgFn(int id)
            => id == FnId.RND || id == FnId.PI || id == FnId.TIMER;

        private void EmitTokens(List<string> body, Action<Op> emit, List<Op> ops, List<int> pcLines)
        {
            // expand a colon-defined word body with the same semantics as top level
            int i = 0;
            while (i < body.Count)
            {
                string tok = body[i++];

                if (dict.TryGetValue(tok, out var nested))
                {
                    EmitTokens(nested, emit, ops, pcLines);
                    continue;
                }
                if (IsQuoted(tok)) { emit(Op.Str(Unquote(tok))); continue; }
                if (TryNumber(tok, out var num)) { emit(Op.Num(num)); continue; }

                if (HandleCoreWord(tok, emit, body, ref i, ops, pcLines)) continue;

                if (FnId.TryFromName(tok.ToUpperInvariant(), out int fid))
                {
                    if (IsZeroArgFn(fid)) emit(new Op(OpCode.CALLFN, a: fid, b: 0));
                    else emit(new Op(OpCode.CALLFN, a: fid, b: 1));
                    continue;
                }
                if (varWords.Contains(tok)) { emit(Op.Num(sym.GetScalarSlot(tok))); continue; }
                if (arr1Words.Contains(tok) || arr2Words.Contains(tok)) { emit(Op.Num(sym.GetArraySlot(tok))); continue; }

                throw new Exception($"FORTH: UNKNOWN WORD '{tok}' in definition");
            }
        }

        // Tokenizer with: ( ... ) comment, \ to EOL, "..." strings
        private static List<string> Tokenize(string line)
        {
            var list = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                char c = line[i];

                if (c == '(')
                {
                    i++;
                    while (i < line.Length && line[i] != ')') i++;
                    if (i < line.Length && line[i] == ')') i++;
                    continue;
                }

                if (c == '\\')
                {
                    list.Add("\\");
                    break;
                }

                if (c == '"')
                {
                    int start = i; i++;
                    while (i < line.Length && line[i] != '"') i++;
                    if (i >= line.Length) throw new Exception("FORTH: UNTERMINATED STRING");
                    i++;
                    list.Add(line.Substring(start, i - start));
                    continue;
                }

                int st = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '(') i++;
                list.Add(line.Substring(st, i - st));
            }
            return list;
        }
    }
}
