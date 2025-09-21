// IL.cs - Retro Mini BASIC interpreter (IL layer consolidated)
//   Contains: Symtab, OpCode, Op, CompiledProgram, FnId
//   Namespace matches existing code to keep usages unchanged.

using System;
using System.Collections.Generic;

namespace MiniBasicIL
{
    //==============================
    // シンボル表（スカラ＆配列）
    //==============================
    sealed class Symtab
    {
        // スカラ変数名→index（数値/文字列で別配列）
        private readonly Dictionary<string, int> nums = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> strs = new(StringComparer.OrdinalIgnoreCase);
        // 配列名→id（数値/文字列で別）
        private readonly Dictionary<string, int> arrNums = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> arrStrs = new(StringComparer.OrdinalIgnoreCase);

        public void Clear()
        {
            nums.Clear();
            strs.Clear();
            arrNums.Clear();
            arrStrs.Clear();
        }

        public int GetScalarSlot(string name)
        {
            if (name.EndsWith("$"))
            {
                if (!strs.TryGetValue(name, out var idx)) { idx = strs.Count; strs[name] = idx; }
                return (idx << 1) | 1; // LSB=1 は文字列
            }
            else
            {
                if (!nums.TryGetValue(name, out var idx)) { idx = nums.Count; nums[name] = idx; }
                return (idx << 1);   // LSB=0 は数値
            }
        }

        public int GetArraySlot(string name)
        {
            if (name.EndsWith("$"))
            {
                if (!arrStrs.TryGetValue(name, out var idx)) { idx = arrStrs.Count; arrStrs[name] = idx; }
                return (idx << 1) | 1; // LSB=1: 文字列配列
            }
            else
            {
                if (!arrNums.TryGetValue(name, out var idx)) { idx = arrNums.Count; arrNums[name] = idx; }
                return (idx << 1);   // LSB=0: 数値配列
            }
        }

        public int NumCount => nums.Count;
        public int StrCount => strs.Count;
        public int ArrNumCount => arrNums.Count;
        public int ArrStrCount => arrStrs.Count;

        public static bool IsStringSlot(int slot) => (slot & 1) == 1;
        public static int SlotToIndex(int slot) => slot >> 1;
    }

    //==============================
    // IL命令
    //==============================
    enum OpCode
    {
        // スタック操作/リテラル
        PUSH_NUM, PUSH_STR, LOAD, STORE,
        // 配列
        LOAD_ARR, STORE_ARR, DIM_ARR,
        // 間接アクセス（FORTH 用）
        LOAD_IND, STORE_IND, LOAD_ARR_IND, STORE_ARR_IND,
        // 算術
        ADD, SUB, MUL, DIV, POW, NEG, MOD,
        // 比較/論理
        CEQ, CNE, CLT, CLE, CGT, CGE, NOT, AND, OR,
        // 関数
        CALLFN,   // operand: fn id, argc（INPUTだけ特殊／グラフィックは可変）
        // 出力
        PRINT, PRINT_SPC, PRINT_SUPPRESS_NL, PRINT_NL,
        // 分岐/ジャンプ
        JMP, JZ,
        // FOR/NEXT
        FOR_INIT,   // a:slot  (stack: end, step)
        FOR_CHECK,  // a:slot, b:bodyPc
        FOR_INCR,   // a:slot or -1 (NEXT var/なし)
        // サブルーチン
        GOSUB, RETSUB,
        // ON GOTO/GOSUB（ジャンプテーブル）
        ON_GOTO, ON_GOSUB,
        // FORTH 補助
        DUP, DROP, SWAP, OVER, ROT,
        PRINT_STACK,   // "."
        PRINT_CR,      // CR
        EMIT_CHAR,     // EMIT
        BAND, BOR, BXOR, // ビット演算
        // 終了
        HALT
    }

    readonly struct Op
    {
        public readonly OpCode Code;
        public readonly int A, B;
        public readonly double D;
        public readonly string? S;
        public Op(OpCode c, int a = 0, int b = 0, double d = 0, string? s = null)
        { Code = c; A = a; B = b; D = d; S = s; }

        public static Op Num(double v) => new Op(OpCode.PUSH_NUM, d: v);
        public static Op Str(string v) => new Op(OpCode.PUSH_STR, s: v);
    }

    //==============================
    // コンパイル成果物
    //==============================
    sealed class CompiledProgram
    {
        public Op[] Code = Array.Empty<Op>();
        public Symtab Symbols = new();
        public int[] PcToLine = Array.Empty<int>();
        public List<int[]> JumpTables = new();
        public Dictionary<int, int> LineToPc = new();
    }

    //==============================
    // 関数ID
    //==============================
    static class FnId
    {
        // 文字列・数値・入出力
        public const int ABS = 1, INT = 2, VAL = 3, STRS = 4, LEN = 5, CHRS = 6, ASC = 7,
                         LEFTS = 8, RIGHTS = 9, MIDS = 10, RND = 11,
                         SPC = 12, TAB = 13,
                         INSTR = 14, STRINGS = 15,   // ★ 追加: INSTR, STRING$
                         INPUT = 100,
                         LOCATE = 120;

        // 数学/時間/乱数制御
        public const int SIN = 20, COS = 21, TAN = 22, SQR = 23,
                         ATN = 24, LOG = 25, EXP = 26, PI = 27,
                         RAD = 28, DEG = 29, SGN = 30, MIN = 31, MAX = 32, CLAMP = 33, RNDI = 34,
                         TIMER = 40, RANDOMIZE = 41;

        // グラフィック命令（200番台）
        public const int SCREEN = 200, GCLS = 201, GCOLOR = 202, GPSET = 203, GLINE = 204, GCIRCLE = 205, GBOX = 206, GFLUSH = 207,
                         GCOLORHSV = 208, GSAVE = 209, GSLEEP = 210,
                         GPOINT = 211, GLOCATE = 212, GPRINT = 213, GPAINT = 214;

        public static int FromName(string name) => name switch
        {
            // 文字列・数値系
            "ABS" => ABS,
            "INT" => INT,
            "VAL" => VAL,
            "STR$" => STRS,
            "LEN" => LEN,
            "CHR$" => CHRS,
            "ASC" => ASC,
            "LEFT$" => LEFTS,
            "RIGHT$" => RIGHTS,
            "MID$" => MIDS,
            "RND" => RND,
            "SPC" => SPC,
            "TAB" => TAB,
            // FromName / TryFromName 両方に
            "INSTR" => INSTR,
            "STRING$" => STRINGS,


            "LOCATE" => LOCATE,

            // 数学/時間
            "SIN" => SIN,
            "COS" => COS,
            "TAN" => TAN,
            "SQR" => SQR,
            "ATN" => ATN,
            "LOG" => LOG,
            "EXP" => EXP,
            "PI" => PI,
            "RAD" => RAD,
            "DEG" => DEG,
            "SGN" => SGN,
            "MIN" => MIN,
            "MAX" => MAX,
            "CLAMP" => CLAMP,
            "RNDI" => RNDI,
            "TIMER" => TIMER,
            "RANDOMIZE" => RANDOMIZE,

            // グラフィック
            "SCREEN" => SCREEN,
            "CLS" => GCLS,
            "COLOR" => GCOLOR,
            "PSET" => GPSET,
            "LINE" => GLINE,
            "CIRCLE" => GCIRCLE,
            "BOX" => GBOX,
            "FLUSH" => GFLUSH,
            "COLORHSV" => GCOLORHSV,
            "SAVEIMAGE" => GSAVE,
            "SLEEP" => GSLEEP,
            "POINT" => GPOINT,
            "GLOCATE" => GLOCATE,
            "GPRINT" => GPRINT,
            "PAINT" => GPAINT,

            _ => throw new Exception($"UNDEF'D FUNCTION: {name}")
        };

        public static bool TryFromName(string name, out int id)
        {
            switch (name)
            {
                // 文字列・数値系
                case "ABS": id = ABS; return true;
                case "INT": id = INT; return true;
                case "VAL": id = VAL; return true;
                case "STR$": id = STRS; return true;
                case "LEN": id = LEN; return true;
                case "CHR$": id = CHRS; return true;
                case "ASC": id = ASC; return true;
                case "LEFT$": id = LEFTS; return true;
                case "RIGHT$": id = RIGHTS; return true;
                case "MID$": id = MIDS; return true;
                case "RND": id = RND; return true;
                case "SPC": id = SPC; return true;
                case "TAB": id = TAB; return true;
                case "INSTR": id = INSTR; return true;
                case "STRING$": id = STRINGS; return true;

                case "LOCATE": id = LOCATE; return true;

                // 数学/時間
                case "SIN": id = SIN; return true;
                case "COS": id = COS; return true;
                case "TAN": id = TAN; return true;
                case "SQR": id = SQR; return true;
                case "ATN": id = ATN; return true;
                case "LOG": id = LOG; return true;
                case "EXP": id = EXP; return true;
                case "PI": id = PI; return true;
                case "RAD": id = RAD; return true;
                case "DEG": id = DEG; return true;
                case "SGN": id = SGN; return true;
                case "MIN": id = MIN; return true;
                case "MAX": id = MAX; return true;
                case "CLAMP": id = CLAMP; return true;
                case "RNDI": id = RNDI; return true;
                case "TIMER": id = TIMER; return true;
                case "RANDOMIZE": id = RANDOMIZE; return true;

                // グラフィック
                case "SCREEN": id = SCREEN; return true;
                case "CLS": id = GCLS; return true;
                case "COLOR": id = GCOLOR; return true;
                case "PSET": id = GPSET; return true;
                case "LINE": id = GLINE; return true;
                case "CIRCLE": id = GCIRCLE; return true;
                case "BOX": id = GBOX; return true;
                case "FLUSH": id = GFLUSH; return true;
                case "COLORHSV": id = GCOLORHSV; return true;
                case "SAVEIMAGE": id = GSAVE; return true;
                case "SLEEP": id = GSLEEP; return true;
                case "POINT": id = GPOINT; return true;
                case "GLOCATE": id = GLOCATE; return true;
                case "GPRINT": id = GPRINT; return true;
                case "PAINT": id = GPAINT; return true;

                default:
                    id = 0; return false;
            }
        }
    }
}
