
using System;
using System.Collections.Generic;

namespace MiniBasicIL
{
    enum TokT { Num, Str, Id, Op, Sym, EOL, EOF }

    readonly struct Tok
    {
        public readonly TokT T;
        public readonly string X;
        public readonly int Col;
        public Tok(TokT t, string x, int col = 0) { T = t; X = x; Col = col; }
    }

    // 字句解析器（行末コメント `'` / MOD を演算子）
    sealed class Lexer
    {
        private readonly string s;
        private int i = 0;

        private readonly int lineNumber;
        public Lexer(string s, int line = 0) { this.s = s ?? string.Empty; this.lineNumber = line; }

        char Peek() => i < s.Length ? s[i] : '\0';
        char Next() => i < s.Length ? s[i++] : '\0';
        bool Eof => i >= s.Length;

        public List<Tok> Lex()
        {
            var o = new List<Tok>();
            while (!Eof)
            {
                char c = Peek();

                if (char.IsWhiteSpace(c)) { Next(); continue; }

                if (c == '\'') { i = s.Length; break; }

                if (char.IsDigit(c))
                {
                    int st = i;
                    while (char.IsDigit(Peek())) Next();
                    if (Peek() == '.') { Next(); while (char.IsDigit(Peek())) Next(); }
                    o.Add(new Tok(TokT.Num, s[st..i], st));
                    continue;
                }

                if (c == '"')
                {
                    int st = i; Next();
                    int contStart = i;
                    while (true)
                    {
                        char d = Next();
                        if (d == '\0') throw new Exception("UNTERMINATED STRING");
                        if (d == '"') break;
                    }
                    o.Add(new Tok(TokT.Str, s[contStart..(i - 1)], st));
                    continue;
                }

                if (char.IsLetter(c))
                {
                    int st = i;
                    while (char.IsLetterOrDigit(Peek())) Next();
                    if (Peek() == '$') Next();
                    string word = s[st..i].ToUpperInvariant();
                    // ★ 追加: REM は行末までコメント扱いにする
                    if (word == "REM")
                    {
                        o.Add(new Tok(TokT.Id, word, st));
                        // 残りは解析せず、そのまま行終端へ
                        i = s.Length;
                        break;
                    }
                    if (word == "MOD") o.Add(new Tok(TokT.Op, word, st));
                    else o.Add(new Tok(TokT.Id, word, st));
                    continue;
                }

                if ((c == '<' || c == '>') && i + 1 < s.Length)
                {
                    string two = s.Substring(i, 2);
                    if (two is "<=" or ">=" or "<>") { o.Add(new Tok(TokT.Op, two, i)); i += 2; continue; }
                }

                int col = i;
                Next();
                if ("+-*/^=<>()[:],;".IndexOf(c) >= 0)
                {
                    if ("+-*/^=<>".IndexOf(c) >= 0) o.Add(new Tok(TokT.Op, c.ToString(), col));
                    else o.Add(new Tok(TokT.Sym, c.ToString(), col));
                }
                else
                {
                    throw new Exception($"UNKNOWN CHAR '{c}' at line {lineNumber}, col {col + 1}");
                }
            }
            o.Add(new Tok(TokT.EOL, "", i));
            o.Add(new Tok(TokT.EOF, "", i));
            return o;
        }
    }
}
