
using MiniBasicIL;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;

namespace MiniBasicIL
{
    public static class Program
    {
        enum ReplMode { BASIC, FORTH }
        static ReplMode mode = ReplMode.BASIC;
        static ForthILCompiler? forth = null;
        static VM.VMState? forthState = null;   // ← FORTH のメモリ状態を保持
        static ForthILCompiler forthCompiler = new ForthILCompiler();
        static VM? forthVm = null;

        // Program.cs 先頭の Program クラス内に追加（Mainの外）
        static string? ParseQuotedFileArg(string cmdline, int cmdLen)
        {
            // cmdLen は "SAVE " / "LOAD " の長さ（4）を想定
            if (cmdline.Length <= cmdLen) return null;
            var rest = cmdline.Substring(cmdLen).Trim();
            if (rest.Length < 2 || rest[0] != '"') return null;

            int close = rest.IndexOf('"', 1);
            if (close < 0) return null;  // 閉じクォートなし
            return rest.Substring(1, close - 1);
        }


        static void ReportRunError(Exception ex, VM? vm, string contextLabel)
        {
            if (contextLabel == "direct")
            {
                Console.WriteLine($"{ex.Message} ({contextLabel})");
                return;
            }
            var lineText = ex.Message.Contains("UNDEF'D STATEMENT") ? "?" : (vm?.LastLine.ToString() ?? "?");
            Console.WriteLine($"{ex.Message} ({contextLabel}, line {lineText})");
        }

        [STAThread]
        public static void Main()
        {
            var program = new SortedDictionary<int, string>();
            Console.WriteLine("--------------------------------------------");
            Console.WriteLine("  Retro Mini BASIC interpreter Version 0.1  ");
            Console.WriteLine("    by Cala Maclir since 2025               ");
            Console.WriteLine("--------------------------------------------");

            Console.WriteLine("Ready.");
            // =================== REPL ===================
            while (true)
            {
                Console.Write(mode == ReplMode.BASIC ? "BASIC> " : "FORTH> ");
                var line = Console.ReadLine();
                if (line == null) break;
                var trimmed = line.Trim();
                if (trimmed.Equals("EXIT", StringComparison.OrdinalIgnoreCase)) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var upper = trimmed.ToUpperInvariant();
                if (upper == "SWITCHFORTH")
                {
                    mode = ReplMode.FORTH;
                    forth ??= new ForthILCompiler();
                    Console.WriteLine("Switched to FORTH mode.");
                    continue;
                }
                if (upper == "SWITCHBASIC")
                {
                    mode = ReplMode.BASIC;
                    Console.WriteLine("Switched to BASIC mode.");
                    continue;
                }
                if (upper == "CLEARSTACK")
                {
                    // FORTH の持続メモリ（スタック／変数など）を破棄
                    // ※ このプロジェクトは FORTH の状態を VM.VMState として保持している前提
                    forthState = null;
                    Console.WriteLine("OK (FORTH runtime reset)");
                    continue;
                }
                if (upper == "CLEARALL")
                {
                    // 1) BASIC 側プログラム行を全消去
                    //    ここはあなたの環境の変数名に合わせてください。
                    //    典型: SortedDictionary<int,string> program;
                    try { program.Clear(); } catch { /* 別名なら無視 */ }

                    // 2) FORTH のユーザー辞書・シンボルを全消去
                    //    既に追加済みの ForthILCompiler.ClearAll() を呼ぶ
                    forthCompiler?.ClearAll();

                    // 3) FORTH のランタイム状態（スタック・変数・配列など）も破棄
                    //    VM 側は Run 開始時に forthState==null なら新規に準備される想定
                    forthState = null;

                    Console.WriteLine("OK (BASIC program + FORTH dict + FORTH runtime cleared)");
                    continue;
                }
                // Main 内の SAVE/LOAD 分岐を書き換え
                // --- SAVE ---
                if (upper.StartsWith("SAVE"))
                {
                    var path = ParseQuotedFileArg(trimmed, 4);
                    if (string.IsNullOrEmpty(path))
                    {
                        Console.WriteLine(@"USAGE: SAVE ""filename.bas""");
                        continue;
                    }
                    try
                    {
                        using var sw = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8);
                        foreach (var kv in program)
                            sw.WriteLine($"{kv.Key} {kv.Value}");
                        Console.WriteLine($"Saved: {path}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SAVE ERROR: {ex.Message}");
                    }
                    continue;
                }

                // --- LOAD ---
                if (upper.StartsWith("LOAD"))
                {
                    var path = ParseQuotedFileArg(trimmed, 4);
                    if (string.IsNullOrEmpty(path))
                    {
                        Console.WriteLine(@"USAGE: LOAD ""filename.bas""");
                        continue;
                    }
                    try
                    {
                        var newProg = new SortedDictionary<int, string>();
                        foreach (var line2 in System.IO.File.ReadAllLines(path))
                        {
                            if (string.IsNullOrWhiteSpace(line2)) continue;
                            int sp = line2.IndexOf(' ');
                            if (sp < 0) continue;
                            int ln = int.Parse(line2.Substring(0, sp).Trim(), CultureInfo.InvariantCulture);
                            string body = line2.Substring(sp + 1);
                            newProg[ln] = body;
                        }
                        program.Clear();
                        foreach (var kv in newProg) program[kv.Key] = kv.Value;
                        Console.WriteLine($"Loaded: {path}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"LOAD ERROR: {ex.Message}");
                    }
                    continue;
                }

                try
                {
                    if (mode == ReplMode.FORTH)
                    {
                        forth ??= new ForthILCompiler();
                        try
                        {
                            var prog = forthCompiler.CompileLine(line);
                            if (forthVm == null)
                            {
                                forthVm = new VM(prog);
                            }
                            else
                            {
                                forthVm.LoadNewProgram(prog);  // ←新しいコードをロードするメソッドを自作
                            }
                            forthVm.Run();   // st を維持したまま実行
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"FORTH ERROR: {ex.Message}");
                            // 例: "at col N" を拾って ^ を出す
                            var m = System.Text.RegularExpressions.Regex.Match(ex.Message, @"col (\d+)");
                            if (m.Success)
                            {
                                int col = int.Parse(m.Groups[1].Value);
                                Console.WriteLine("  >> " + line);
                                Console.WriteLine("  >> " + new string(' ', Math.Max(0, col - 1)) + "^");
                            }
                        }
                        continue;
                    }

                    // BASIC immediate / program edit path
                    int p = 0; while (p < line.Length && char.IsWhiteSpace(line[p])) p++;
                    int q = p; while (q < line.Length && char.IsDigit(line[q])) q++;
                    if (q > p)
                    {
                        int ln = int.Parse(line[p..q], CultureInfo.InvariantCulture);
                        string body = line[q..].TrimStart();
                        if (string.IsNullOrEmpty(body)) program.Remove(ln);
                        else program[ln] = body;
                        continue;
                    }

                    if (upper == "NEW") { program.Clear(); continue; }
                    if (upper == "LIST") { foreach (var kv in program) Console.WriteLine($"{kv.Key} {kv.Value}"); continue; }
                    if (upper == "RUN")
                    {
                        CompiledProgram cp;
                        try
                        {
                            var comp = new Compiler();
                            cp = comp.Compile(program);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{ex.Message} (at compile)");
                            continue;
                        }

                        VM vm = null!;
                        try
                        {
                            vm = new VM(cp, null);

                            var __sw = Stopwatch.StartNew();
                            vm.Run();
                            __sw.Stop();
                            Console.WriteLine($"[RUN] {__sw.Elapsed.TotalSeconds:F3} sec");

                        }
                        catch (Exception ex)
                        {
                            ReportRunError(ex, vm, "program");
                        }
                        continue;
                    }

                    var tmp = new SortedDictionary<int, string> { { 10, line }, { 20, "END" } };
                    VM? vm2 = null;
                    try
                    {
                        var comp2 = new Compiler();
                        var cp2 = comp2.Compile(tmp);
                        vm2 = new VM(cp2, null);
                        vm2.Run();
                    }
                    catch (Exception ex)
                    {
                        ReportRunError(ex, vm2, "direct");
                    }
                }
                catch (Exception ex)
                {
                    ReportRunError(ex, null, "repl");
                }
            }
        }
    }
}
