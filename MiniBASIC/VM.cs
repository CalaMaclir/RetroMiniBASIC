
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace MiniBasicIL
{
    public readonly struct Val
    {
        public readonly double? Num;
        public readonly string? Str;
        public bool IsStr => Str != null;
        public Val(double n) { Num = n; Str = null; }
        public Val(string s) { Str = s; Num = null; }
        public double AsNum()
        {
            if (IsStr) throw new Exception("TYPE MISMATCH");
            return Num!.Value;
        }
        public override string ToString()
            => IsStr ? Str! : Num!.Value.ToString("G", CultureInfo.InvariantCulture);
        public static Val Truth(bool b) => new Val(b ? -1.0 : 0.0);
        public bool AsBool() => !IsStr && Num!.Value != 0.0;
    }

    sealed class VM
    {
        // NOTE: readonly を外して LoadNewProgram で差し替えられるようにする
        Op[] code;
        int[] pcToLine;
        Dictionary<int, int> lineToPc;

        double[] nums;
        string[] strs;

        readonly List<double[]?> numArr = new();
        readonly List<string[]?> strArr = new();
        readonly List<double[,]?> numArr2D = new();
        readonly List<string[,]?> strArr2D = new();

        List<int[]> jumpTables;

        readonly Stack<Val> st = new();
        readonly Stack<int> returnStack = new();
        readonly Stack<ForFrame> forStack = new();

        private Random rnd = new Random();
        private readonly DateTime startTime = DateTime.UtcNow;

        int col = 0;
        const int ZoneWidth = 14;

        public int LastLine { get; private set; } = 0;

        public sealed class VMState
        {
            public double[] Nums = Array.Empty<double>();
            public string[] Strs = Array.Empty<string>();
            public List<double[]?> NumArr = new();
            public List<string[]?> StrArr = new();
            public List<double[,]?> NumArr2D = new();
            public List<string[,]?> StrArr2D = new();
        }

        struct ForFrame
        {
            public int slot;
            public double end;
            public double step;
            public int checkPc;
            public int bodyPc;
        }

        static bool IsStrSlot(int slot) => Symtab.IsStringSlot(slot);
        static int ToIndex(int slot) => Symtab.SlotToIndex(slot);


        // VM クラス内（Run() などと同じスコープ）に追記
        public void ResetStacksAndMemory()
        {
            // スタック類
            st.Clear();
            returnStack.Clear();
            forStack.Clear();

            // スカラ（配列は長さ保持で中身だけ 0/空文字にする方が安全）
            if (nums != null) Array.Clear(nums, 0, nums.Length);
            if (strs != null) Array.Clear(strs, 0, strs.Length);

            // 配列（1D/2D）も内容クリア
            for (int i = 0; i < numArr.Count; i++) numArr[i] = null;
            for (int i = 0; i < strArr.Count; i++) strArr[i] = null;
            for (int i = 0; i < numArr2D.Count; i++) numArr2D[i] = null;
            for (int i = 0; i < strArr2D.Count; i++) strArr2D[i] = null;

            // 表示・内部状態
            LastLine = 0;
            col = 0;
        }

        // 「プログラムごと」初期化（コードも空の HALT のみに差し替える）
        public void ResetAll()
        {
            ResetStacksAndMemory();

            // コード等を最小に差し替え
            code = new[] { new Op(OpCode.HALT) };
            pcToLine = new[] { 0 };
            lineToPc = new Dictionary<int, int>();
            jumpTables.Clear();
            LastLine = 0;
        }

        


        public VM(CompiledProgram prog)
        {
            code = prog.Code;
            pcToLine = prog.PcToLine;
            lineToPc = prog.LineToPc;

            nums = new double[prog.Symbols.NumCount];
            strs = new string[prog.Symbols.StrCount];

            for (int i = 0; i < prog.Symbols.ArrNumCount; i++) numArr.Add(null);
            for (int i = 0; i < prog.Symbols.ArrStrCount; i++) strArr.Add(null);
            for (int i = 0; i < prog.Symbols.ArrNumCount; i++) numArr2D.Add(null);
            for (int i = 0; i < prog.Symbols.ArrStrCount; i++) strArr2D.Add(null);

            jumpTables = new List<int[]>(prog.JumpTables);
        }

        public VM(CompiledProgram prog, VMState? carry) : this(prog)
        {
            if (carry != null)
            {
                // 拡張してからコピー（不足があれば拡張）
                EnsureScalarCapacityByCount(carry.Nums.Length, carry.Strs.Length);
                EnsureArrayListCapacityByCount(carry.NumArr.Count, carry.StrArr.Count);

                Array.Copy(carry.Nums, nums, Math.Min(nums.Length, carry.Nums.Length));
                Array.Copy(carry.Strs, strs, Math.Min(strs.Length, carry.Strs.Length));

                for (int i = 0; i < carry.NumArr.Count; i++) numArr[i] = carry.NumArr[i];
                for (int i = 0; i < carry.StrArr.Count; i++) strArr[i] = carry.StrArr[i];
                for (int i = 0; i < carry.NumArr2D.Count; i++) numArr2D[i] = carry.NumArr2D[i];
                for (int i = 0; i < carry.StrArr2D.Count; i++) strArr2D[i] = carry.StrArr2D[i];
            }
        }

        public VMState ExportState()
        {
            return new VMState
            {
                Nums = (double[])nums.Clone(),
                Strs = (string[])strs.Clone(),
                NumArr = new List<double[]?>(numArr),
                StrArr = new List<string[]?>(strArr),
                NumArr2D = new List<double[,]?>(numArr2D),
                StrArr2D = new List<string[,]?>(strArr2D)
            };
        }

        // ---- 新規: プログラム差し替え（スタック・メモリ保持） ----
        public void LoadNewProgram(CompiledProgram prog)
        {
            // コード差し替え
            code = prog.Code;
            pcToLine = prog.PcToLine;
            lineToPc = prog.LineToPc;
            jumpTables = new List<int[]>(prog.JumpTables);

            // シンボル数に合わせてストレージを拡張（縮小はしない）
            EnsureScalarCapacityByCount(prog.Symbols.NumCount, prog.Symbols.StrCount);
            EnsureArrayListCapacityByCount(prog.Symbols.ArrNumCount, prog.Symbols.ArrStrCount);
        }

        // ---- 収容確保ヘルパ ----
        void EnsureScalarCapacity(int slot)
        {
            int idx = ToIndex(slot);
            if (IsStrSlot(slot))
            {
                if (idx >= strs.Length) Array.Resize(ref strs, idx + 1);
            }
            else
            {
                if (idx >= nums.Length) Array.Resize(ref nums, idx + 1);
            }
        }
        void EnsureScalarCapacityByCount(int needNum, int needStr)
        {
            if (needNum > nums.Length) Array.Resize(ref nums, needNum);
            if (needStr > strs.Length) Array.Resize(ref strs, needStr);
        }
        void EnsureArraySlotCapacity(int slot)
        {
            int idx = ToIndex(slot);
            if (IsStrSlot(slot))
            {
                while (idx >= strArr.Count) strArr.Add(null);
                while (idx >= strArr2D.Count) strArr2D.Add(null);
            }
            else
            {
                while (idx >= numArr.Count) numArr.Add(null);
                while (idx >= numArr2D.Count) numArr2D.Add(null);
            }
        }
        void EnsureArrayListCapacityByCount(int needNumArr, int needStrArr)
        {
            while (needNumArr > numArr.Count) numArr.Add(null);
            while (needStrArr > strArr.Count) strArr.Add(null);
            while (needNumArr > numArr2D.Count) numArr2D.Add(null);
            while (needStrArr > strArr2D.Count) strArr2D.Add(null);
        }

        public void Run()
        {
            int pc = 0;
            while (true)
            {
                LastLine = (pc >= 0 && pc < pcToLine.Length) ? pcToLine[pc] : 0;
                var op = code[pc++];
                switch (op.Code)
                {
                    case OpCode.PUSH_NUM: st.Push(new Val(op.D)); break;
                    case OpCode.PUSH_STR: st.Push(new Val(op.S!)); break;

                    case OpCode.LOAD:
                        {
                            EnsureScalarCapacity(op.A);
                            if (IsStrSlot(op.A)) st.Push(new Val(strs[ToIndex(op.A)] ?? ""));
                            else st.Push(new Val(nums[ToIndex(op.A)]));
                            break;
                        }
                    case OpCode.STORE:
                        {
                            EnsureScalarCapacity(op.A);
                            var v = st.Pop();
                            if (IsStrSlot(op.A))
                            {
                                if (!v.IsStr) v = new Val(v.ToString());
                                strs[ToIndex(op.A)] = v.Str!;
                            }
                            else
                            {
                                nums[ToIndex(op.A)] = v.AsNum();
                            }
                            break;
                        }

                    case OpCode.DIM_ARR:
                        {
                            int dims = op.B;
                            if (dims < 1 || dims > 2) throw new Exception("DIM: ONLY 1D OR 2D SUPPORTED");
                            var sizes = new int[dims];
                            for (int d = dims - 1; d >= 0; d--)
                            {
                                int sz = (int)Math.Floor(st.Pop().AsNum());
                                if (sz < 0) throw new Exception("BAD DIM");
                                sizes[d] = sz + 1;
                            }
                            EnsureArraySlotCapacity(op.A);
                            if (dims == 1)
                            {
                                if (IsStrSlot(op.A)) strArr[ToIndex(op.A)] = new string[sizes[0]];
                                else numArr[ToIndex(op.A)] = new double[sizes[0]];
                            }
                            else
                            {
                                int d0 = sizes[0], d1 = sizes[1];
                                if (IsStrSlot(op.A)) strArr2D[ToIndex(op.A)] = new string[d0, d1];
                                else numArr2D[ToIndex(op.A)] = new double[d0, d1];
                            }
                            break;
                        }

                    case OpCode.LOAD_ARR:
                        {
                            EnsureArraySlotCapacity(op.A);
                            if (op.B == 1)
                            {
                                int i0 = (int)st.Pop().AsNum();
                                if (IsStrSlot(op.A))
                                {
                                    var arr = strArr[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i0] ?? ""));
                                }
                                else
                                {
                                    var arr = numArr[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i0]));
                                }
                            }
                            else if (op.B == 2)
                            {
                                int j = (int)st.Pop().AsNum();
                                int i = (int)st.Pop().AsNum();
                                if (IsStrSlot(op.A))
                                {
                                    var arr = strArr2D[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i, j] ?? ""));
                                }
                                else
                                {
                                    var arr = numArr2D[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i, j]));
                                }
                            }
                            else { throw new Exception("BAD SUBSCRIPT"); }
                            break;
                        }

                    case OpCode.STORE_ARR:
                        {
                            EnsureArraySlotCapacity(op.A);
                            if (op.B == 1)
                            {
                                var val = st.Pop();
                                int i0 = (int)st.Pop().AsNum();
                                if (IsStrSlot(op.A))
                                {
                                    if (!val.IsStr) val = new Val(val.ToString());
                                    var arr = strArr[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i0] = val.Str!;
                                }
                                else
                                {
                                    var arr = numArr[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i0] = val.AsNum();
                                }
                            }
                            else if (op.B == 2)
                            {
                                var val = st.Pop();
                                int j = (int)st.Pop().AsNum();
                                int i = (int)st.Pop().AsNum();
                                if (IsStrSlot(op.A))
                                {
                                    if (!val.IsStr) val = new Val(val.ToString());
                                    var arr = strArr2D[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i, j] = val.Str!;
                                }
                                else
                                {
                                    var arr = numArr2D[ToIndex(op.A)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i, j] = val.AsNum();
                                }
                            }
                            else { throw new Exception("BAD SUBSCRIPT"); }
                            break;
                        }

                    // ===== 間接アクセス（Forth） =====
                    case OpCode.LOAD_IND:
                        {
                            int slot = (int)st.Pop().AsNum();
                            EnsureScalarCapacity(slot);
                            if (IsStrSlot(slot)) st.Push(new Val(strs[ToIndex(slot)] ?? ""));
                            else st.Push(new Val(nums[ToIndex(slot)]));
                            break;
                        }
                    case OpCode.STORE_IND:
                        {
                            int slot = (int)st.Pop().AsNum();
                            EnsureScalarCapacity(slot);
                            var v = st.Pop();
                            if (IsStrSlot(slot))
                            {
                                if (!v.IsStr) v = new Val(v.ToString());
                                strs[ToIndex(slot)] = v.Str!;
                            }
                            else
                            {
                                nums[ToIndex(slot)] = v.AsNum();
                            }
                            break;
                        }
                    case OpCode.LOAD_ARR_IND:
                        {
                            int dims = op.B;
                            if (dims == 1)
                            {
                                int i0 = (int)st.Pop().AsNum();
                                int addr = (int)st.Pop().AsNum();
                                EnsureArraySlotCapacity(addr);
                                if (IsStrSlot(addr))
                                {
                                    var arr = strArr[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i0] ?? ""));
                                }
                                else
                                {
                                    var arr = numArr[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i0]));
                                }
                            }
                            else if (dims == 2)
                            {
                                int j = (int)st.Pop().AsNum();
                                int i = (int)st.Pop().AsNum();
                                int addr = (int)st.Pop().AsNum();
                                EnsureArraySlotCapacity(addr);
                                if (IsStrSlot(addr))
                                {
                                    var arr = strArr2D[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i, j] ?? ""));
                                }
                                else
                                {
                                    var arr = numArr2D[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    st.Push(new Val(arr[i, j]));
                                }
                            }
                            else throw new Exception("BAD SUBSCRIPT");
                            break;
                        }
                    case OpCode.STORE_ARR_IND:
                        {
                            int dims = op.B;
                            if (dims == 1)
                            {
                                int i0 = (int)st.Pop().AsNum();
                                int addr = (int)st.Pop().AsNum();
                                var v = st.Pop();
                                EnsureArraySlotCapacity(addr);
                                if (IsStrSlot(addr))
                                {
                                    if (!v.IsStr) v = new Val(v.ToString());
                                    var arr = strArr[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i0] = v.Str!;
                                }
                                else
                                {
                                    var arr = numArr[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if ((uint)i0 >= (uint)arr.Length) throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i0] = v.AsNum();
                                }
                            }
                            else if (dims == 2)
                            {
                                int j = (int)st.Pop().AsNum();
                                int i = (int)st.Pop().AsNum();
                                int addr = (int)st.Pop().AsNum();
                                var v = st.Pop();
                                EnsureArraySlotCapacity(addr);
                                if (IsStrSlot(addr))
                                {
                                    if (!v.IsStr) v = new Val(v.ToString());
                                    var arr = strArr2D[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i, j] = v.Str!;
                                }
                                else
                                {
                                    var arr = numArr2D[ToIndex(addr)] ?? throw new Exception("UNDEF'D ARRAY");
                                    if (i < 0 || j < 0 || i >= arr.GetLength(0) || j >= arr.GetLength(1))
                                        throw new Exception("SUBSCRIPT OUT OF RANGE");
                                    arr[i, j] = v.AsNum();
                                }
                            }
                            else throw new Exception("BAD SUBSCRIPT");
                            break;
                        }

                    // ===== Arithmetic / logic =====
                    case OpCode.ADD: { var b = st.Pop(); var a = st.Pop(); if (a.IsStr || b.IsStr) st.Push(new Val(a.ToString() + b.ToString())); else st.Push(new Val(a.AsNum() + b.AsNum())); break; }
                    case OpCode.SUB: { var b = st.Pop(); var a = st.Pop(); st.Push(new Val(a.AsNum() - b.AsNum())); break; }
                    case OpCode.MUL: { var b = st.Pop(); var a = st.Pop(); st.Push(new Val(a.AsNum() * b.AsNum())); break; }
                    case OpCode.DIV: { var b = st.Pop(); var a = st.Pop(); var d = b.AsNum(); if (d == 0) throw new Exception("DIVISION BY ZERO"); st.Push(new Val(a.AsNum() / d)); break; }
                    case OpCode.POW: { var b = st.Pop(); var a = st.Pop(); st.Push(new Val(Math.Pow(a.AsNum(), b.AsNum()))); break; }
                    case OpCode.NEG: { var a = st.Pop(); st.Push(new Val(-a.AsNum())); break; }
                    case OpCode.MOD:
                        {
                            var b = st.Pop().AsNum();
                            var a = st.Pop().AsNum();
                            if (b == 0) throw new Exception("DIVISION BY ZERO");
                            st.Push(new Val(a % b));
                            break;
                        }

                    case OpCode.CEQ:
                        {
                            var b = st.Pop();
                            var a = st.Pop();
                            if (!a.IsStr && !b.IsStr)
                                st.Push(Val.Truth(a.AsNum() == b.AsNum()));
                            else if (a.IsStr && b.IsStr)
                                st.Push(Val.Truth(a.Str! == b.Str!));
                            else
                                st.Push(Val.Truth(a.ToString() == b.ToString()));
                            break;
                        }
                    case OpCode.CNE:
                        {
                            var b = st.Pop();
                            var a = st.Pop();
                            if (!a.IsStr && !b.IsStr)
                                st.Push(Val.Truth(a.AsNum() != b.AsNum()));
                            else if (a.IsStr && b.IsStr)
                                st.Push(Val.Truth(a.Str! != b.Str!));
                            else
                                st.Push(Val.Truth(a.ToString() != b.ToString()));
                            break;
                        }
                    case OpCode.CLT: { var b = st.Pop(); var a = st.Pop(); st.Push(Val.Truth(a.IsStr || b.IsStr ? string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) < 0 : a.AsNum() < b.AsNum())); break; }
                    case OpCode.CLE: { var b = st.Pop(); var a = st.Pop(); st.Push(Val.Truth(a.IsStr || b.IsStr ? string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) <= 0 : a.AsNum() <= b.AsNum())); break; }
                    case OpCode.CGT: { var b = st.Pop(); var a = st.Pop(); st.Push(Val.Truth(a.IsStr || b.IsStr ? string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) > 0 : a.AsNum() > b.AsNum())); break; }
                    case OpCode.CGE: { var b = st.Pop(); var a = st.Pop(); st.Push(Val.Truth(a.IsStr || b.IsStr ? string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) >= 0 : a.AsNum() >= b.AsNum())); break; }
                    case OpCode.NOT: { var a = st.Pop(); st.Push(Val.Truth(!a.AsBool())); break; }
                    case OpCode.AND: { var b = st.Pop(); var a = st.Pop(); st.Push(Val.Truth(a.AsBool() && b.AsBool())); break; }
                    case OpCode.OR: { var b = st.Pop(); var a = st.Pop(); st.Push(Val.Truth(a.AsBool() || b.AsBool())); break; }

                    case OpCode.CALLFN:
                        {
                            int fn = op.A; int argc = op.B;

                            // INPUT
                            if (fn == FnId.INPUT)
                            {
                                int slot = argc; // slot番号がBに入っている
                                string? line = Console.ReadLine();
                                EnsureScalarCapacity(slot);
                                if (Symtab.IsStringSlot(slot))
                                {
                                    strs[Symtab.SlotToIndex(slot)] = line ?? "";
                                }
                                else
                                {
                                    nums[Symtab.SlotToIndex(slot)] = ParseVal(line ?? "");
                                }
                                break;
                            }

                            // RANDOMIZE (0/1引数)
                            if (fn == FnId.RANDOMIZE)
                            {
                                if (argc >= 1) rnd = new Random((int)st.Pop().AsNum());
                                else rnd = new Random();
                                break;
                            }

                            // LOCATE (console cursor)
                            if (fn == FnId.LOCATE)
                            {
                                int col_ = 1, row_ = 1;
                                if (st.Count >= 1) { col_ = (int)st.Pop().AsNum(); }
                                if (st.Count >= 1) { row_ = (int)st.Pop().AsNum(); }
                                int x = Math.Max(0, col_ - 1);
                                int y = Math.Max(0, row_ - 1);
                                try
                                {
                                    int bw = Console.BufferWidth;
                                    int bh = Console.BufferHeight;
                                    Console.SetCursorPosition(
                                        Math.Clamp(x, 0, Math.Max(0, bw - 1)),
                                        Math.Clamp(y, 0, Math.Max(0, bh - 1))
                                    );
                                    this.col = Console.CursorLeft;
                                }
                                catch { }
                                break;
                            }
                            // INSTR (2 or 3 args)
                            // INSTR(hay$, needle$)            -> 1-based index, 0 if not found
                            // INSTR(start, hay$, needle$)     -> start 位置(1-based)から検索
                            if (fn == FnId.INSTR)
                            {
                                if (argc == 2)
                                {
                                    var b = st.Pop(); var a = st.Pop();
                                    string hay = a.ToString(); string needle = b.ToString();
                                    int idx = hay.IndexOf(needle, StringComparison.Ordinal);
                                    st.Push(new Val(idx >= 0 ? (double)(idx + 1) : 0.0));
                                }
                                else if (argc == 3)
                                {
                                    var c = st.Pop(); var b = st.Pop(); var a = st.Pop();
                                    int start = Math.Max(1, (int)a.AsNum()) - 1;
                                    string hay = b.ToString(); string needle = c.ToString();
                                    if (start > hay.Length) { st.Push(new Val(0.0)); }
                                    else
                                    {
                                        int idx = hay.IndexOf(needle, start, StringComparison.Ordinal);
                                        st.Push(new Val(idx >= 0 ? (double)(idx + 1) : 0.0));
                                    }
                                }
                                else throw new Exception("INSTR: ARG COUNT");
                                break;
                            }

                            // STRING$ (n [, c])
                            // n>=0, c=文字列なら先頭1文字 / 数値なら CHR$(int(c))
                            if (fn == FnId.STRINGS)
                            {
                                int n;
                                if (argc == 1)
                                {
                                    n = Math.Max(0, (int)st.Pop().AsNum());
                                    st.Push(new Val(new string(' ', n)));
                                }
                                else if (argc == 2)
                                {
                                    var chv = st.Pop();
                                    n = Math.Max(0, (int)st.Pop().AsNum());

                                    char ch;
                                    if (chv.IsStr)
                                    {
                                        var s = chv.ToString();
                                        ch = s.Length > 0 ? s[0] : ' ';
                                    }
                                    else
                                    {
                                        ch = (char)((int)chv.AsNum());
                                    }
                                    st.Push(new Val(new string(ch, n)));
                                }
                                else throw new Exception("STRING$: ARG COUNT");
                                break;
                            }

                            // --- Graphics ---
                            if (fn == FnId.SCREEN)
                            {
                                int h = 480, w = 640;
                                if (st.Count >= 1) h = (int)st.Pop().AsNum();
                                if (st.Count >= 1) w = (int)st.Pop().AsNum();
                                GfxHost.Instance.EnsureScreen(w, h);
                                break;
                            }
                            if (fn == FnId.GCLS) { GfxHost.Instance.Cls(); break; }

                            if (fn == FnId.GCOLOR)
                            {
                                if (argc == 1)
                                {
                                    int pal = (int)st.Pop().AsNum();
                                    var (R, G, B) = GetPalette(pal);
                                    GfxHost.Instance.ColorRGB(R, G, B);
                                }
                                else
                                {
                                    int b_ = 255, g_ = 255, r_ = 255;
                                    if (st.Count >= 1) b_ = (int)st.Pop().AsNum();
                                    if (st.Count >= 1) g_ = (int)st.Pop().AsNum();
                                    if (st.Count >= 1) r_ = (int)st.Pop().AsNum();
                                    GfxHost.Instance.ColorRGB(r_, g_, b_);
                                }
                                break;
                            }

                            if (fn == FnId.GPSET)
                            {
                                int? tr = null, tg = null, tb = null;
                                if (argc == 3) { int pal = (int)st.Pop().AsNum(); (tr, tg, tb) = GetPalette(pal); argc--; }
                                if (argc >= 2)
                                {
                                    int y = (int)st.Pop().AsNum();
                                    int x = (int)st.Pop().AsNum();
                                    GfxHost.Instance.DoWithTempColor(tr, tg, tb, () => GfxHost.Instance.PSet(x, y));
                                }
                                break;
                            }

                            if (fn == FnId.GLINE)
                            {
                                bool shorthand = (argc & (1<<30)) != 0;
                                argc &= ~(1<<30);

                                int? tr = null, tg = null, tb = null;
                                if (argc == 3 || argc == 5)
                                {
                                    int pal = (int)st.Pop().AsNum();
                                    (tr, tg, tb) = GetPalette(pal);
                                    argc--;
                                }
                                if (shorthand && argc == 2)
                                {
                                    int y2 = (int)st.Pop().AsNum();
                                    int x2 = (int)st.Pop().AsNum();
                                    var (x1, y1) = GfxHost.Instance.GetPen();
                                    GfxHost.Instance.DoWithTempColor(tr, tg, tb, () => GfxHost.Instance.Line(x1, y1, x2, y2));
                                }
                                else if (!shorthand && argc == 4)
                                {
                                    int y2 = (int)st.Pop().AsNum();
                                    int x2 = (int)st.Pop().AsNum();
                                    int y1 = (int)st.Pop().AsNum();
                                    int x1 = (int)st.Pop().AsNum();
                                    GfxHost.Instance.DoWithTempColor(tr, tg, tb, () => GfxHost.Instance.Line(x1, y1, x2, y2));
                                }
                                break;
                            }

                            if (fn == FnId.GCIRCLE)
                            {
                                int? tr = null, tg = null, tb = null;
                                if (argc == 4) { int pal = (int)st.Pop().AsNum(); (tr, tg, tb)=GetPalette(pal); argc--; }
                                if (argc >= 3)
                                {
                                    int r = (int)st.Pop().AsNum();
                                    int y = (int)st.Pop().AsNum();
                                    int x = (int)st.Pop().AsNum();
                                    GfxHost.Instance.DoWithTempColor(tr, tg, tb, () => GfxHost.Instance.Circle(x, y, r));
                                }
                                break;
                            }

                            if (fn == FnId.GBOX)
                            {
                                int? tr = null, tg = null, tb = null;
                                bool fill = false;

                                if (argc >= 6)
                                {
                                    int pal = (int)st.Pop().AsNum(); (tr, tg, tb)=GetPalette(pal); argc--;
                                    fill = Math.Abs((int)st.Pop().AsNum()) != 0; argc--;
                                }
                                else if (argc == 5)
                                {
                                    fill = Math.Abs((int)st.Pop().AsNum()) != 0; argc--;
                                }

                                if (argc >= 4)
                                {
                                    int y2 = (int)st.Pop().AsNum();
                                    int x2 = (int)st.Pop().AsNum();
                                    int y1 = (int)st.Pop().AsNum();
                                    int x1 = (int)st.Pop().AsNum();
                                    GfxHost.Instance.DoWithTempColor(tr, tg, tb, () => GfxHost.Instance.Box(x1, y1, x2, y2, fill));
                                }
                                break;
                            }
                            // VM.cs - Run() の CALLFN ハンドラ内
                            if (fn == FnId.GPAINT)
                            {
                                int? tr = null, tg = null, tb = null;
                                if (argc == 3) { int pal = (int)st.Pop().AsNum(); (tr, tg, tb) = GetPalette(pal); argc--; }
                                if (argc >= 2)
                                {
                                    int y = (int)st.Pop().AsNum();
                                    int x = (int)st.Pop().AsNum();
                                    GfxHost.Instance.DoWithTempColor(tr, tg, tb,
                                        () => GfxHost.Instance.PaintFill(x, y));
                                }
                                break;
                            }


                            if (fn == FnId.GFLUSH) { GfxHost.Instance.Flush(); break; }

                            if (fn == FnId.GLOCATE)
                            {
                                int y = 0, x = 0;
                                if (st.Count >= 1) { y = (int)st.Pop().AsNum(); }
                                if (st.Count >= 1) { x = (int)st.Pop().AsNum(); }
                                GfxHost.Instance.TextLocate(x, y);
                                break;
                            }

                            if (fn == FnId.GPRINT)
                            {
                                var parts = new string[argc];
                                for (int i = argc - 1; i >= 0; i--) parts[i] = st.Pop().ToString();
                                string s = string.Concat(parts);
                                GfxHost.Instance.TextPrint(s);
                                break;
                            }
                            if (fn == FnId.GCOLORHSV)
                            {
                                double v = 1, s = 1, h = 0;
                                if (st.Count >= 1) v = st.Pop().AsNum();
                                if (st.Count >= 1) s = st.Pop().AsNum();
                                if (st.Count >= 1) h = st.Pop().AsNum();
                                (int R, int G, int B) = HSVtoRGB(h, s, v);
                                GfxHost.Instance.ColorRGB(R, G, B);
                                break;
                            }

                            if (fn == FnId.GSAVE) { var path = st.Pop().ToString(); GfxHost.Instance.Save(path); break; }
                            if (fn == FnId.GSLEEP) { int ms = (int)st.Pop().AsNum(); Thread.Sleep(ms); break; }

                            if (fn == FnId.GPOINT)
                            {
                                int y = (int)st.Pop().AsNum();
                                int x = (int)st.Pop().AsNum();
                                bool on = GfxHost.Instance.GetPixelNonBlack(x, y);
                                st.Push(Val.Truth(on));
                                break;
                            }

                            var args = Array.Empty<Val>();
                            if (argc > 0)
                            {
                                args = new Val[argc];
                                for (int i = argc - 1; i >= 0; i--) args[i] = st.Pop();
                            }

                            Val res = fn switch
                            {
                                FnId.ABS => new Val(Math.Abs(args[0].AsNum())),
                                FnId.INT => new Val(Math.Floor(args[0].AsNum())),
                                FnId.VAL => new Val(ParseVal(args[0].ToString())),
                                FnId.STRS => new Val(args[0].ToString()),
                                FnId.LEN => new Val((double)args[0].ToString().Length),
                                FnId.CHRS => new Val(((char)Convert.ToInt32(args[0].AsNum())).ToString()),
                                FnId.ASC => new Val((args[0].ToString().Length == 0) ? 0.0 : (double)(int)args[0].ToString()[0]),
                                FnId.LEFTS => new Val(SubStrLeft(args[0].ToString(), (int)args[1].AsNum())),
                                FnId.RIGHTS => new Val(SubStrRight(args[0].ToString(), (int)args[1].AsNum())),
                                FnId.MIDS => new Val(SubStrMid(args[0].ToString(), (int)args[1].AsNum(), args.Length >= 3 ? (int)args[2].AsNum() : int.MaxValue)),
                                FnId.RND => new Val(rnd.NextDouble()),
                                FnId.SPC => new Val(new string(' ', Math.Max(0, (int)args[0].AsNum()))),
                                FnId.TAB => new Val(TABSpaces((int)args[0].AsNum(), col)),
                                FnId.SIN => new Val(Math.Sin(args[0].AsNum())),
                                FnId.COS => new Val(Math.Cos(args[0].AsNum())),
                                FnId.TAN => new Val(Math.Tan(args[0].AsNum())),
                                FnId.SQR => new Val(Math.Sqrt(args[0].AsNum())),
                                FnId.ATN => new Val(Math.Atan(args[0].AsNum())),
                                FnId.LOG => new Val(LogChecked(args[0].AsNum())),
                                FnId.EXP => new Val(Math.Exp(args[0].AsNum())),
                                FnId.PI => new Val(Math.PI),
                                FnId.RAD => new Val(args[0].AsNum() * Math.PI / 180.0),
                                FnId.DEG => new Val(args[0].AsNum() * 180.0 / Math.PI),
                                FnId.SGN => new Val(args[0].AsNum() > 0 ? 1.0 : (args[0].AsNum() < 0 ? -1.0 : 0.0)),
                                FnId.MIN => new Val(Math.Min(args[0].AsNum(), args[1].AsNum())),
                                FnId.MAX => new Val(Math.Max(args[0].AsNum(), args[1].AsNum())),
                                FnId.CLAMP => new Val(Math.Clamp(args[0].AsNum(), args[1].AsNum(), args[2].AsNum())),
                                FnId.TIMER => new Val((DateTime.UtcNow - startTime).TotalSeconds),
                                FnId.RNDI => new Val((double)RndIntInclusive((int)args[0].AsNum())),
                                _ => throw new Exception("UNDEF'D FUNCTION")
                            };
                            st.Push(res);
                            break;
                        }

                    case OpCode.PRINT:
                        {
                            var v = st.Pop();
                            string s = v.ToString();
                            Console.Write(s);
                            col += s.Length;
                            break;
                        }
                    case OpCode.PRINT_SPC:
                        {
                            int spaces = ZoneWidth - (col % ZoneWidth);
                            if (spaces <= 0) spaces = ZoneWidth;
                            Console.Write(new string(' ', spaces));
                            col += spaces;
                            break;
                        }
                    case OpCode.PRINT_SUPPRESS_NL: break;
                    case OpCode.PRINT_NL: Console.WriteLine(); col = 0; break;

                    case OpCode.JMP: pc = op.A; break;
                    case OpCode.JZ:
                        {
                            var v = st.Pop();
                            if (!v.AsBool()) pc = op.A;
                            break;
                        }

                    case OpCode.GOSUB: returnStack.Push(pc); pc = op.A; break;
                    case OpCode.RETSUB:
                        if (returnStack.Count == 0) throw new Exception("RETURN WITHOUT GOSUB");
                        pc = returnStack.Pop(); break;

                    case OpCode.FOR_INIT:
                        {
                            double step = st.Pop().AsNum();
                            double end = st.Pop().AsNum();
                            forStack.Push(new ForFrame { slot = op.A, end = end, step = step, checkPc = pc, bodyPc = -1 });
                            break;
                        }
                    case OpCode.FOR_CHECK:
                        {
                            var fr = forStack.Pop();
                            if (fr.bodyPc < 0) fr.bodyPc = op.B;

                            double cur = nums[ToIndex(fr.slot)];
                            bool cont = fr.step >= 0 ? (cur <= fr.end) : (cur >= fr.end);

                            if (cont)
                            {
                                forStack.Push(fr);
                                pc = fr.bodyPc;
                            }
                            break;
                        }
                    case OpCode.FOR_INCR:
                        {
                            if (forStack.Count == 0) throw new Exception("NEXT WITHOUT FOR");

                            if (op.A >= 0)
                            {
                                ForFrame fr;
                                while (true)
                                {
                                    if (forStack.Count == 0) throw new Exception("NEXT WITHOUT FOR");
                                    var t = forStack.Pop();
                                    if (t.slot == op.A) { fr = t; break; }
                                }
                                nums[ToIndex(fr.slot)] += fr.step;
                                double cur = nums[ToIndex(fr.slot)];
                                bool cont = fr.step >= 0 ? (cur <= fr.end) : (cur >= fr.end);
                                if (cont) { forStack.Push(fr); pc = fr.checkPc; }
                            }
                            else
                            {
                                var fr = forStack.Pop();
                                nums[ToIndex(fr.slot)] += fr.step;
                                double cur = nums[ToIndex(fr.slot)];
                                bool cont = fr.step >= 0 ? (cur <= fr.end) : (cur >= fr.end);
                                if (cont) { forStack.Push(fr); pc = fr.checkPc; }
                            }
                            break;
                        }

                    case OpCode.ON_GOTO:
                        {
                            int k = (int)st.Pop().AsNum();
                            if (k >= 1 && k <= jumpTables[op.A].Length)
                                pc = jumpTables[op.A][k - 1];
                            break;
                        }
                    case OpCode.ON_GOSUB:
                        {
                            int k = (int)st.Pop().AsNum();
                            if (k >= 1 && k <= jumpTables[op.A].Length)
                            {
                                returnStack.Push(pc);
                                pc = jumpTables[op.A][k - 1];
                            }
                            break;
                        }

                    case OpCode.HALT: return;

                    // FORTH stack helpers
                    case OpCode.DUP: { var a = st.Peek(); st.Push(a); break; }
                    case OpCode.DROP: { st.Pop(); break; }
                    case OpCode.SWAP: { var a = st.Pop(); var b = st.Pop(); st.Push(a); st.Push(b); break; }
                    case OpCode.OVER: { var a = st.Pop(); var b = st.Peek(); st.Push(a); st.Push(b); break; }
                    case OpCode.ROT: { var a = st.Pop(); var b = st.Pop(); var c = st.Pop(); st.Push(b); st.Push(a); st.Push(c); break; }

                    case OpCode.PRINT_STACK:
                        {
                            var v = st.Pop();
                            Console.Write(v.ToString());
                            Console.Write(' ');
                            break;
                        }
                    case OpCode.PRINT_CR: { Console.WriteLine(); break; }
                    case OpCode.EMIT_CHAR:
                        {
                            var ch = (int)st.Pop().AsNum();
                            Console.Write(((char)ch).ToString());
                            break;
                        }

                    case OpCode.BAND:
                        {
                            var b = (long)st.Pop().AsNum();
                            var a = (long)st.Pop().AsNum();
                            st.Push(new Val((double)(a & b)));
                            break;
                        }
                    case OpCode.BOR:
                        {
                            var b = (long)st.Pop().AsNum();
                            var a = (long)st.Pop().AsNum();
                            st.Push(new Val((double)(a | b)));
                            break;
                        }
                    case OpCode.BXOR:
                        {
                            var b = (long)st.Pop().AsNum();
                            var a = (long)st.Pop().AsNum();
                            st.Push(new Val((double)(a ^ b)));
                            break;
                        }

                    default: throw new Exception($"UNKNOWN OPCODE {op.Code}");
                }
            }
        }

        static double ParseVal(string s)
        {
            s = s.Trim();
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
        }
        static string SubStrLeft(string s, int n) { if (n < 0) n = 0; if (n > s.Length) n = s.Length; return s.Substring(0, n); }
        static string SubStrRight(string s, int n) { if (n < 0) n = 0; if (n > s.Length) n = s.Length; return s.Substring(s.Length - n, n); }
        static string SubStrMid(string s, int start, int len)
        {
            if (start < 1) start = 1; int idx = Math.Min(start - 1, s.Length);
            if (len < 0) len = 0; int take = Math.Min(len, s.Length - idx);
            return s.Substring(idx, take);
        }
        static string TABSpaces(int n, int currentCol)
        {
            if (n <= 1) n = 1;
            if (n - 1 <= currentCol) return "";
            int need = n - 1 - currentCol;
            return new string(' ', need);
        }
        static double LogChecked(double x)
        {
            if (x <= 0) throw new Exception("DOMAIN ERROR");
            return Math.Log(x);
        }

        static (int, int, int) HSVtoRGB(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Clamp(s, 0, 1);
            v = Math.Clamp(v, 0, 1);
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r=c; g=x; b=0; }
            else if (h<120) { r=x; g=c; b=0; }
            else if (h<180) { r=0; g=c; b=x; }
            else if (h<240) { r=0; g=x; b=c; }
            else if (h<300) { r=x; g=0; b=c; }
            else { r=c; g=0; b=x; }
            int R = (int)Math.Round((r + m) * 255);
            int G = (int)Math.Round((g + m) * 255);
            int B = (int)Math.Round((b + m) * 255);
            return (Math.Clamp(R, 0, 255), Math.Clamp(G, 0, 255), Math.Clamp(B, 0, 255));
        }

        static int RndIntInclusive(int n)
        {
            if (n <= 0) return 0;
            var rng = new Random();
            return rng.Next(0, n + 1);
        }

        static (int, int, int) GetPalette(int n)
        {
            n = Math.Clamp(n, 0, 15);
            return n switch
            {
                0 => (0, 0, 0),
                1 => (0, 0, 128),
                2 => (0, 128, 0),
                3 => (0, 128, 128),
                4 => (128, 0, 0),
                5 => (128, 0, 128),
                6 => (128, 128, 0),
                7 => (192, 192, 192),
                8 => (128, 128, 128),
                9 => (0, 0, 255),
                10 => (0, 255, 0),
                11 => (0, 255, 255),
                12 => (255, 0, 0),
                13 => (255, 0, 255),
                14 => (255, 255, 0),
                15 => (255, 255, 255),
                _ => (255, 255, 255)
            };
        }
    }
}
