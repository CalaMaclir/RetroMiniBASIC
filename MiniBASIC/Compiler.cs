using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace MiniBasicIL
{



    //==============================
    // コンパイラ
    //==============================
    sealed class Compiler
    {
        readonly Symtab sym = new();
        readonly Dictionary<int, int> lineToPc = new();
        readonly List<Op> ops = new();
        readonly List<int> opLines = new();
        readonly List<List<int>> onTablesLineNums = new();
        int currentLine = 0;

        List<Tok> toks = default!;
        int p;

        // ★ DO...LOOP 用のスタック
        readonly Stack<int> doStack = new();

        // ★ WHILE...WEND 用のスタック（(loopStartPc, jzPos)）
        readonly Stack<(int startPc, int jzPos)> whileStack = new();

        // ★ DEF FN: ユーザー定義関数（本実装：式ベース）
        readonly Dictionary<string, (List<string> paramNames, List<string> hiddenNames, string bodyText)> userFns
            = new(StringComparer.OrdinalIgnoreCase);



        Tok LT => toks[p];
        bool Match(TokT t, string? x = null) { if (LT.T != t) return false; if (x != null && !string.Equals(LT.X, x, StringComparison.OrdinalIgnoreCase)) return false; p++; return true; }
        Tok Expect(TokT t, string? x = null)
        {
            if (!Match(t, x))
                throw new Exception(
                    x == null
                        ? $"SYNTAX ERROR at line {currentLine}, col {LT.Col}: expected {t}, got '{LT.X}'"
                        : $"SYNTAX ERROR at line {currentLine}, col {LT.Col}: expected {x}, got '{LT.X}'");
            return toks[p - 1];
        }
        void Emit(Op op) { ops.Add(op); opLines.Add(currentLine); }

        public CompiledProgram Compile(SortedDictionary<int, string> program)
        {
            ops.Clear(); opLines.Clear(); lineToPc.Clear(); onTablesLineNums.Clear();

            foreach (var kv in program)
            {
                currentLine = kv.Key;
                lineToPc[currentLine] = ops.Count;

                toks = new Lexer(kv.Value, currentLine).Lex();
                p = 0;

                while (LT.T != TokT.EOF && LT.T != TokT.EOL)
                {
                    CompileStatement();
                    if (Match(TokT.Sym, ":")) continue;  // ← コロンがあれば次の文へ
                    break;
                }
            }

            Emit(new Op(OpCode.HALT));

            // 後処理：JMP/GOSUB の行番号 → PC に解決
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.Code == OpCode.JMP || op.Code == OpCode.GOSUB)
                {
                    if (lineToPc.TryGetValue(op.A, out var pc))
                        ops[i] = new Op(op.Code, a: pc, b: op.B, d: op.D, s: op.S);
                    else if (op.A >= 0 && op.A < ops.Count)
                    {
                        // 既にPC。変更しない
                    }
                    else
                        throw new Exception($"BAD JUMP TARGET {op.A}");
                }

            }


            // ONテーブル：行→PC へ変換
            var jumpTables = new List<int[]>();
            foreach (var list in onTablesLineNums)
            {
                var pcs = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    int line = list[i];
                    if (!lineToPc.TryGetValue(line, out var pc))
                        throw new Exception($"UNDEF'D STATEMENT (line {line})");
                    pcs[i] = pc;
                }
                jumpTables.Add(pcs);
            }

            return new CompiledProgram
            {
                Code = ops.ToArray(),
                Symbols = sym,
                PcToLine = opLines.ToArray(),
                JumpTables = jumpTables,
                LineToPc = new Dictionary<int, int>(lineToPc)
            };
        }

        void CompileStatement()
        {
            if (LT.T != TokT.Id) throw new Exception("SYNTAX ERROR");
            string kw = LT.X; p++;

    

            switch (kw)
            {
                case "REM":
                case "'":
                    while (LT.T != TokT.EOF && LT.T != TokT.EOL) p++; return;

                case "LET": CompileAssign(); return;
                case "PRINT": CompilePrint(); return;
                case "INPUT": CompileInput(); return;

                case "IF": CompileIf(); return;

                case "GOTO":
                    {
                        int tgt = (int)ParseExprNum();
                        Emit(new Op(OpCode.JMP, a: tgt));  // 後処理でPCに解決
                        return;
                    }

                case "END":
                case "STOP": Emit(new Op(OpCode.HALT)); return;

                case "LIST":
                case "RUN":
                case "NEW": return;

                case "GOSUB":
                    {
                        int tgt = (int)ParseExprNum();
                        Emit(new Op(OpCode.GOSUB, a: tgt)); // 後処理でPCに解決
                        return;
                    }

                case "RETURN": Emit(new Op(OpCode.RETSUB)); return;

                case "FOR": CompileFor(); return;
                case "NEXT": CompileNext(); return;

                case "DIM": CompileDim(); return;

                case "ON": CompileOn(); return;

                // ★ グラフィック命令（文として扱う）
                case "SCREEN": CompileGraphicsCall("SCREEN"); return;
                case "CLS": CompileGraphicsCall("CLS"); return;
                case "COLOR": CompileGraphicsCall("COLOR"); return;
                case "PSET": CompileGraphicsCall("PSET"); return;
                case "LINE": CompileLineStatement(); return;
                case "CIRCLE": CompileGraphicsCall("CIRCLE"); return;
                case "BOX": CompileGraphicsCall("BOX"); return;
                case "FLUSH": CompileGraphicsCall("FLUSH"); return;
                case "COLORHSV": CompileGraphicsCall("COLORHSV"); return;
                case "SAVEIMAGE": CompileGraphicsCall("SAVEIMAGE"); return;
                case "RANDOMIZE": CompileGraphicsCall("RANDOMIZE"); return;
                case "SLEEP": CompileGraphicsCall("SLEEP"); return;
                case "LOCATE": CompileGraphicsCall("LOCATE"); return;
                case "GLOCATE": CompileGraphicsCall("GLOCATE"); return;
                case "GPRINT": CompileGraphicsCall("GPRINT"); return;
                case "PAINT": CompileGraphicsCall("PAINT"); return;

                case "DEF": CompileDef(); return;
                case "WHILE":
                    {
                        // ★ ループ先頭（条件評価の手前）を記録
                        int startPc = ops.Count;

                        // 条件式を評価
                        CompileExpression();

                        // 偽なら脱出：飛び先は未定なので仮で置く
                        int jzPos = ops.Count;
                        Emit(new Op(OpCode.JZ, a: -1));

                        // 先頭PCとJZ位置を積む
                        whileStack.Push((startPc, jzPos));
                        return;
                    }

                case "WEND":
                    {
                        var (startPc, jzPos) = whileStack.Pop();
                        // 本体末尾から先頭へ戻る
                        Emit(new Op(OpCode.JMP, a: startPc));
                        // JZ の飛び先をここ（WENDの次）に確定
                        ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);
                        return;
                    }

                // DO ... LOOP [UNTIL expr]
                case "DO":
                    {
                        // ループ先頭PCを積む
                        doStack.Push(ops.Count);
                        return;
                    }
                case "LOOP":
                    {
                        int loopStartPc = doStack.Pop();

                        if (LT.T == TokT.Id && LT.X == "UNTIL")
                        {
                            p++;  // UNTIL
                            CompileExpression();  // cond
                            int jzPos = ops.Count;
                            Emit(new Op(OpCode.JZ, a: -1));  // 後で埋める

                            Emit(new Op(OpCode.JMP, a: loopStartPc));

                            // JZ の飛び先を「現在のPC」に書き換える
                            ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);
                        }
                        else
                        {
                            // 無条件ループ
                            Emit(new Op(OpCode.JMP, a: loopStartPc));
                        }
                        return;
                    }

                default:
                    p--; CompileAssign(); return; // LET省略代入
            }
        }

        // --- LINE だけ旧BASIC互換の特殊構文に対応 ---
        //   ・(x1,y1)-(x2,y2)[, color]
        //   ・-(x2,y2)[, color]    … 省略記法（直前のペン座標→(x2,y2)）
        //   ・x1,y1,x2,y2[, color] … フラット形式（保険）
        void CompileLineStatement()
        {
            int argc = 0;
            bool shorthand = false;

            if (Match(TokT.Sym, "("))
            {
                // (x1,y1)-(x2,y2)
                CompileExpression(); argc++; Expect(TokT.Sym, ",");
                CompileExpression(); argc++; Expect(TokT.Sym, ")");
                Expect(TokT.Op, "-"); Expect(TokT.Sym, "(");
                CompileExpression(); argc++; Expect(TokT.Sym, ",");
                CompileExpression(); argc++; Expect(TokT.Sym, ")");
            }
            else if (Match(TokT.Op, "-") && Match(TokT.Sym, "("))
            {
                // -(x2,y2)
                shorthand = true;
                CompileExpression(); argc++; Expect(TokT.Sym, ",");
                CompileExpression(); argc++; Expect(TokT.Sym, ")");
            }
            else
            {
                // LINE x1,y1,x2,y2 形式（互換の保険）
                CompileExpression(); argc++; Expect(TokT.Sym, ",");
                CompileExpression(); argc++; Expect(TokT.Sym, ",");
                CompileExpression(); argc++; Expect(TokT.Sym, ",");
                CompileExpression(); argc++;
            }

            // [, color]（パレット番号）を最後に1つだけ許可
            if (Match(TokT.Sym, ","))
            {
                CompileExpression(); argc++;   // color
            }

            int id = FnId.FromName("LINE");

            // 省略記法フラグを argc に埋め込んで VM に渡す（上位ビット）
            int argcWithFlag = shorthand ? (argc | (1 << 30)) : argc;

            Emit(new Op(OpCode.CALLFN, a: id, b: argcWithFlag));
        }


        static bool IsZeroArgFn(int id)    => id == FnId.RND || id == FnId.PI || id == FnId.TIMER;


        void CompileAssign()
        {
            var idTok = Expect(TokT.Id);
            string name = idTok.X;

            if (Match(TokT.Sym, "("))
            {
                // 配列代入：A(i)=v / A(i,j)=v
                int argc = 0;
                CompileExpression(); argc++;                         // i
                if (Match(TokT.Sym, ",")) { CompileExpression(); argc++; } // ,j (あれば)
                Expect(TokT.Sym, ")");

                Expect(TokT.Op, "=");
                CompileExpression();                                 // value

                int arr = sym.GetArraySlot(name);
                Emit(new Op(OpCode.STORE_ARR, a: arr, b: argc));     // b=次元数(1 or 2)
            }
            else
            {
                // スカラ代入
                int slot = sym.GetScalarSlot(name);
                Expect(TokT.Op, "=");
                CompileExpression();
                Emit(new Op(OpCode.STORE, a: slot));
            }
        }

        void CompilePrint()
        {
            bool first = true; bool suppress = false;
            while (LT.T != TokT.EOF && LT.T != TokT.EOL && !(LT.T == TokT.Sym && LT.X == ":"))
            {
                if (!first)
                {
                    if (Match(TokT.Sym, ",")) { Emit(new Op(OpCode.PRINT_SPC)); first = true; continue; }
                    if (Match(TokT.Sym, ";")) { suppress = true; first = true; continue; }
                }
                CompileExpression();
                Emit(new Op(OpCode.PRINT));
                first = false;
                if (LT.T == TokT.Sym && (LT.X == "," || LT.X == ";")) continue;
                break;
            }
            if (!suppress) Emit(new Op(OpCode.PRINT_NL));
        }

        void CompileInput()
        {
            // 先頭に文字列リテラルがあればプロンプトとして出力
            if (LT.T == TokT.Str)
            {
                string prompt = LT.X; p++;
                Emit(Op.Str(prompt));
                Emit(new Op(OpCode.PRINT));
                // ";" または "," を食べる
                if (Match(TokT.Sym, ";") || Match(TokT.Sym, ",")) { }
            }
            else
            {
                // デフォルトプロンプト（変数名?）
                // → 従来通り
            }

            var id = Expect(TokT.Id).X;
            int slot = sym.GetScalarSlot(id);

            // プロンプトのあとに変数名を出力する旧式 ("D? ") にする場合はこちら
            // Emit(Op.Str($"{id}? ")); Emit(new Op(OpCode.PRINT));

            Emit(new Op(OpCode.PRINT_SUPPRESS_NL));
            Emit(new Op(OpCode.CALLFN, a: FnId.INPUT, b: slot)); // a=INPUT, b=slot
        }

        void CompileIf()
        {
            // 条件式を評価
            CompileExpression();
            Expect(TokT.Id, "THEN");

            // 偽ならジャンプする命令を仮発行（飛び先は後で確定）
            int jzPos = ops.Count;
            Emit(new Op(OpCode.JZ, a: -1));

            // --- THEN の直後が「行番号」のケース ---
            if (LT.T == TokT.Num)
            {
                int thenLine = (int)ParseExprNum();

                if (Match(TokT.Id, "ELSE"))
                {
                    if (LT.T == TokT.Num)
                    {
                        // THEN <行> ELSE <行>  ← 今回の修正ポイント
                        int elseLine = (int)ParseExprNum();

                        // 真のとき：最初のJMPで thenLine へ
                        Emit(new Op(OpCode.JMP, a: thenLine));

                        // 偽のとき：JZ でここに飛んできて、そのまま elseLine へ
                        int elseJmpPos = ops.Count;
                        Emit(new Op(OpCode.JMP, a: elseLine));

                        // JZ の飛び先を「ELSE 用のJMP命令の位置」に設定
                        // これにより偽のときは thenLine ではなく elseLine へ確実に飛ぶ
                        ops[jzPos] = new Op(OpCode.JZ, a: elseJmpPos);
                    }
                    else
                    {
                        // THEN <行> ELSE 文…
                        // 真：thenLine、偽：この位置（ELSE文の先頭）へ
                        Emit(new Op(OpCode.JMP, a: thenLine));
                        ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);

                        // ELSE 節（複文 ":" 対応）
                        while (LT.T != TokT.EOF && LT.T != TokT.EOL)
                        {
                            CompileStatement();
                            if (Match(TokT.Sym, ":")) continue;
                            break;
                        }
                    }
                }
                else
                {
                    // ELSE なし：真なら thenLine、偽なら IF 文の終端へ
                    Emit(new Op(OpCode.JMP, a: thenLine));
                    ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);
                }
                return;
            }

            // --- THEN に「文」が続くケース（複文 : 対応、ELSE か 行末まで） ---
            while (LT.T != TokT.EOF && LT.T != TokT.EOL && !(LT.T == TokT.Id && LT.X == "ELSE"))
            {
                CompileStatement();
                if (Match(TokT.Sym, ":")) continue;
                break;
            }

            if (Match(TokT.Id, "ELSE"))
            {
                // THEN 節を実行した場合に ELSE 節を飛ばすための JMP を仮置き
                int jmpToEndPos = ops.Count;
                Emit(new Op(OpCode.JMP, a: -1));

                // 偽なら ELSE 節先頭へ
                ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);

                if (LT.T == TokT.Num)
                {
                    // ELSE <行>
                    int elseLine = (int)ParseExprNum();
                    Emit(new Op(OpCode.JMP, a: elseLine));
                }
                else
                {
                    // ELSE 文…
                    while (LT.T != TokT.EOF && LT.T != TokT.EOL)
                    {
                        CompileStatement();
                        if (Match(TokT.Sym, ":")) continue;
                        break;
                    }
                }

                // IF 文の終端（THEN 節からのスキップ先）を確定
                ops[jmpToEndPos] = new Op(OpCode.JMP, a: ops.Count);
            }
            else
            {
                // ELSE が無い：偽なら IF 文の終端へ
                ops[jzPos] = new Op(OpCode.JZ, a: ops.Count);
            }
        }

        // ========= 式パーサ =========
        void CompileExpression() => CompileOr();
        void CompileOr() { CompileAnd(); while (LT.T == TokT.Id && LT.X == "OR") { p++; CompileAnd(); Emit(new Op(OpCode.OR)); } }
        void CompileAnd() { CompileRel(); while (LT.T == TokT.Id && LT.X == "AND") { p++; CompileRel(); Emit(new Op(OpCode.AND)); } }

        void CompileRel()
        {
            // 左項
            CompileAddSub();

            // 比較演算子は「高々1つ」だけ許可
            if (LT.T == TokT.Op && (LT.X == "=" || LT.X == "<>" || LT.X == "<" || LT.X == "<=" || LT.X == ">" || LT.X == ">="))
            {
                string op = LT.X; p++;      // 演算子を1つだけ読む
                CompileAddSub();            // 右項

                Emit(op switch
                {
                    "=" => new Op(OpCode.CEQ),
                    "<>" => new Op(OpCode.CNE),
                    "<" => new Op(OpCode.CLT),
                    "<=" => new Op(OpCode.CLE),
                    ">" => new Op(OpCode.CGT),
                    ">=" => new Op(OpCode.CGE),
                    _ => throw new Exception("SYNTAX ERROR")
                });
            }
        }


        void CompileAddSub()
        {
            CompileMulDiv();
            while (LT.T == TokT.Op && (LT.X == "+" || LT.X == "-"))
            {
                var op = LT.X; p++;
                CompileMulDiv();
                Emit(op == "+" ? new Op(OpCode.ADD) : new Op(OpCode.SUB));
            }
        }

        void CompileMulDiv()
        {
            CompilePow();
            while (LT.T == TokT.Op && (LT.X == "*" || LT.X == "/" || LT.X == "MOD"))
            {
                var op = LT.X; p++;
                CompilePow();
                Emit(op switch
                {
                    "*" => new Op(OpCode.MUL),
                    "/" => new Op(OpCode.DIV),
                    "MOD" => new Op(OpCode.MOD),
                    _ => throw new Exception("SYNTAX ERROR")
                });
            }
        }

        void CompilePow() { CompileUnary(); while (LT.T == TokT.Op && LT.X == "^") { p++; CompileUnary(); Emit(new Op(OpCode.POW)); } }

        void CompileUnary()
        {
            if (LT.T == TokT.Op && LT.X == "+") { p++; CompileUnary(); return; }
            if (LT.T == TokT.Op && LT.X == "-") { p++; CompileUnary(); Emit(new Op(OpCode.NEG)); return; }
            if (LT.T == TokT.Id && LT.X == "NOT") { p++; CompileUnary(); Emit(new Op(OpCode.NOT)); return; }
            CompilePrimary();
        }

        void CompilePrimary()
        {
            if (Match(TokT.Num)) { Emit(Op.Num(double.Parse(toks[p - 1].X, CultureInfo.InvariantCulture))); return; }
            if (Match(TokT.Str)) { Emit(Op.Str(toks[p - 1].X)); return; }
            if (Match(TokT.Sym, "(")) { CompileExpression(); Expect(TokT.Sym, ")"); return; }

            if (LT.T == TokT.Id)
            {
                string name = LT.X; p++;
                // N88-BASIC 互換: "FN ADD(...)" を "ADD(...)" として扱う
                if (string.Equals(name, "FN", StringComparison.OrdinalIgnoreCase) && LT.T == TokT.Id)
                {
                    name = LT.X; p++;   // name="ADD" に差し替え
                }
                // ★ ユーザー定義関数（DEF FN）か？
                if (userFns.TryGetValue(name, out var def) && Match(TokT.Sym, "("))
                {
                    // 実引数をパースし、対応する隠しスロットに代入してから本体式を評価
                    var args = new List<int>();
                    int argc = 0;
                    if (!Match(TokT.Sym, ")"))
                    {
                        while (true)
                        {
                            CompileExpression(); // 実引数式をスタックに積む
                            argc++;
                            if (Match(TokT.Sym, ",")) continue;
                            Expect(TokT.Sym, ")"); break;
                        }
                    }
                    if (argc != def.paramNames.Count)
                        throw new Exception("ARGUMENT COUNT MISMATCH");

                    // 実引数を hiddenNames のスロットに STORE（順序: 定義順）
                    for (int i = argc - 1; i >= 0; i--)
                    {
                        int hiddenSlot = sym.GetScalarSlot(def.hiddenNames[i]); // 隠し変数スロット
                        Emit(new Op(OpCode.STORE, a: hiddenSlot));
                    }


                    // 本体式を文字列から一時的にパースして評価
                    CompileExpressionFromString(def.bodyText);
                    return;
                }

                // ★ 先に関数名かを判定
                if (FnId.TryFromName(name, out int fnId))
                {
                    // 引数あり/なしの通常パス
                    if (Match(TokT.Sym, "("))
                    {
                        int argc = 0;
                        if (!Match(TokT.Sym, ")"))
                        {
                            while (true) { CompileExpression(); argc++; if (Match(TokT.Sym, ",")) continue; break; }
                            Expect(TokT.Sym, ")");
                        }
                        Emit(new Op(OpCode.CALLFN, a: fnId, b: argc));
                        return;
                    }
                    else
                    {
                        // ★ かっこ省略の 0 引数関数（例：RND, PI, TIMER）
                        if (IsZeroArgFn(fnId))
                        {
                            Emit(new Op(OpCode.CALLFN, a: fnId, b: 0));
                            return;
                        }
                        // かっこが無く、0引数でもない → 従来どおり変数として扱う
                    }
                }

                // 配列 or スカラ変数
                if (Match(TokT.Sym, "("))
                {
                    int argcArr = 0;
                    CompileExpression(); argcArr++;
                    if (Match(TokT.Sym, ",")) { CompileExpression(); argcArr++; }
                    Expect(TokT.Sym, ")");
                    int arr = sym.GetArraySlot(name);
                    Emit(new Op(OpCode.LOAD_ARR, a: arr, b: argcArr));
                    return;
                }
                int slot = sym.GetScalarSlot(name);
                Emit(new Op(OpCode.LOAD, a: slot));
                return;
            }

            throw new Exception($"SYNTAX ERROR at line {currentLine}, col {LT.Col}: unexpected '{LT.X}'");
        }
        void CompileExpressionFromString(string expr)
        {
            // 既存のパーサ状態を退避
            var toksBak = toks; var pBak = p; var lineBak = currentLine;
            try
            {
                toks = new Lexer(expr, currentLine).Lex();
                p = 0;
                CompileExpression();
            }
            finally
            {
                toks = toksBak; p = pBak; currentLine = lineBak;
            }
        }


        double ParseExprNum()
        {
            if (Match(TokT.Num)) return double.Parse(toks[p - 1].X, CultureInfo.InvariantCulture);
            throw new Exception("EXPECTED NUMBER");
        }

        // ========= FOR/NEXT =========
        void CompileFor()
        {
            // FOR i = <start> TO <end> [STEP <step>]
            var id = Expect(TokT.Id).X; int slot = sym.GetScalarSlot(id);
            Expect(TokT.Op, "=");
            CompileExpression();                   // start
            Emit(new Op(OpCode.STORE, a: slot));   // i=start

            Expect(TokT.Id, "TO");
            CompileExpression();                   // end → スタック

            if (LT.T == TokT.Id && LT.X == "STEP") { p++; CompileExpression(); }  // step → スタック
            else { Emit(Op.Num(1.0)); }                                        // 省略時 step=1

            Emit(new Op(OpCode.FOR_INIT, a: slot));

            int checkAt = ops.Count;
            Emit(new Op(OpCode.FOR_CHECK, a: slot, b: -1)); // b は後で bodyPc に上書き

            int bodyPc = ops.Count;
            ops[checkAt] = new Op(OpCode.FOR_CHECK, a: slot, b: bodyPc);
        }

        void CompileNext()
        {
            // NEXT [var]
            int slot = -1;
            if (LT.T == TokT.Id) { slot = sym.GetScalarSlot(LT.X); p++; }
            Emit(new Op(OpCode.FOR_INCR, a: slot));
        }

        // ========= DIM =========
        void CompileDim()
        {
            while (true)
            {
                var id = Expect(TokT.Id).X;
                Expect(TokT.Sym, "(");

                // 1次元または2次元（最大2）
                int argc = 0;
                CompileExpression(); argc++;                         // 第一引数
                if (Match(TokT.Sym, ",")) { CompileExpression(); argc++; } // 第二引数(あれば)
                Expect(TokT.Sym, ")");

                if (argc < 1 || argc > 2) throw new Exception("DIM: ONLY 1D OR 2D SUPPORTED");

                int arr = sym.GetArraySlot(id);
                // VM がスタックから次元数ぶん size を読む。B=次元数
                Emit(new Op(OpCode.DIM_ARR, a: arr, b: argc));

                if (Match(TokT.Sym, ",")) continue;
                break;
            }
        }

        // ========= ON GOTO / ON GOSUB =========
        void CompileOn()
        {
            // ON <expr> GOTO l1,l2,... | GOSUB l1,l2,...
            CompileExpression(); // スタックに index
            bool isGosub;
            if (Match(TokT.Id, "GOTO")) isGosub = false;
            else if (Match(TokT.Id, "GOSUB")) isGosub = true;
            else throw new Exception("EXPECTED GOTO OR GOSUB");

            var lines = new List<int>();
            while (true)
            {
                if (LT.T != TokT.Num) throw new Exception("EXPECTED LINE NUMBER");
                lines.Add((int)double.Parse(LT.X, CultureInfo.InvariantCulture));
                p++;
                if (Match(TokT.Sym, ",")) continue;
                break;
            }
            int idx = onTablesLineNums.Count;
            onTablesLineNums.Add(lines);
            Emit(new Op(isGosub ? OpCode.ON_GOSUB : OpCode.ON_GOTO, a: idx));
        }

        // ★ グラフィック命令の文法（NAME (args) でも NAME args でも可）
        void CompileGraphicsCall(string name)
        {
            int argc = 0;

            if (Match(TokT.Sym, "("))
            {
                if (!Match(TokT.Sym, ")"))
                {
                    while (true)
                    {
                        CompileExpression(); argc++;
                        if (Match(TokT.Sym, ",")) continue;
                        Expect(TokT.Sym, ")"); break;
                    }
                }
            }
            else
            {
                bool first = true;
                while (LT.T != TokT.EOF && LT.T != TokT.EOL && !(LT.T == TokT.Sym && (LT.X == ":" || LT.X == ";")))
                {
                    if (!first)
                    {
                        if (!Match(TokT.Sym, ",")) break;
                    }
                    if (LT.T == TokT.Sym && (LT.X == ":" || LT.X == ";")) break;
                    CompileExpression(); argc++;
                    first = false;
                }
            }

            int id = FnId.FromName(name);
            Emit(new Op(OpCode.CALLFN, a: id, b: argc)); // argcはVM側では必須ではない（可変許容）
        }

        static List<string> SplitByColonRespectQuotes(string s)
        {
            var list = new List<string>();
            int st = 0; bool inStr = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') inStr = !inStr;
                else if (c == ':' && !inStr) { list.Add(s.Substring(st, i - st)); st = i + 1; }
            }
            list.Add(s.Substring(st));
            return list;
        }
        void CompileDef()
        {
            Expect(TokT.Id, "FN");
            string fnName = Expect(TokT.Id).X; // 例: FNSQR など（$可）

            // パラメータ
            var paramNames = new List<string>();
            var hiddenNames = new List<string>();

            Expect(TokT.Sym, "(");
            if (!Match(TokT.Sym, ")"))
            {
                while (true)
                {
                    string pn = Expect(TokT.Id).X; // 例: X または A$
                    paramNames.Add(pn);

                    // 隠し実体名（通常の識別子として有効な形）
                    string hidden = $"FNFN{fnName}{paramNames.Count}";

                    // 文字列パラメータなら末尾$を付与
                    if (pn.EndsWith("$") && !hidden.EndsWith("$")) hidden += "$";
                    hiddenNames.Add(hidden);

                    if (Match(TokT.Sym, ",")) continue;
                    Expect(TokT.Sym, ")"); break;
                }
            }

            Expect(TokT.Op, "=");

            // 行末までのトークンを複製し、パラメータ名→隠し名に置換してテキスト化
            var bodyTokens = new List<Tok>();
            while (LT.T != TokT.EOF && LT.T != TokT.EOL)
            {
                var t = toks[p++];
                if (t.T == TokT.Id)
                {
                    for (int i = 0; i < paramNames.Count; i++)
                    {
                        if (string.Equals(t.X, paramNames[i], StringComparison.OrdinalIgnoreCase))
                        {
                            t = new Tok(TokT.Id, hiddenNames[i], t.Col);
                            break;
                        }
                    }
                }
                bodyTokens.Add(t);
            }
            string bodyText = string.Join(" ",
                bodyTokens.ConvertAll(tt => tt.X));

            userFns[fnName] = (paramNames, hiddenNames, bodyText);
        }

    }


}
