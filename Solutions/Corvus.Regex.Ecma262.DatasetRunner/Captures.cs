// <copyright file="Captures.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Judges captures.jsonl: exec, the global search, the sticky flag, replace and split, as the generator of the data
// (generate-captures.js) describes them. Positions in the data are UTF-16 code units; the UTF-8 forms are judged
// through a map from each code unit to its byte offset, where the unit inside a surrogate pair is the position
// between the halves of the character (its third byte).

using System.Text;
using System.Text.Json;
using Corvus.Ecma262.RegularExpressions;

internal static class Captures
{
    private const string Replacement = "<$&|$1|$2|$10|$<a>|$$|$`|$'>";

    /// <summary>Judges one record. Returns the number of right answers and appends each wrong one.</summary>
    public static int Judge(JsonElement r, string pattern, TimeSpan limit, Func<string, string> unescape, Action<string> wrong, out int wrongCount, out int limited, int line)
    {
        int right = 0;
        wrongCount = 0;
        limited = 0;
        string flags = r.GetProperty("flags").GetString()!;
        JsonElement valid = r.GetProperty("valid");
        if (Overlay.Enabled)
        {
            Answers(r, pattern, flags, valid, limit, unescape, line);
        }

        foreach (JsonElement t in r.GetProperty("tests").EnumerateArray())
        {
            if (t.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean())
            {
                // V8's answers depart from ECMA-262 here (a match that starts inside a surrogate pair).
                continue;
            }

            string text = unescape(t.GetProperty("text").GetRawText());
            foreach ((string mode, bool isValid) in new[] { ("unicode", valid.GetProperty("unicode").GetBoolean()), ("legacy", valid.GetProperty("legacy").GetBoolean()) })
            {
                if (!isValid || !t.TryGetProperty(mode, out JsonElement a))
                {
                    continue;
                }

                string modeFlags = mode == "unicode" ? flags + "u" : flags;
                string where = $"/{Escape(pattern)}/{modeFlags}\t{Escape(text)}";
                try
                {
                    right += JudgeMode(pattern, modeFlags, text, a, limit, where, unescape, wrong, ref wrongCount);
                }
                catch (RegexMatchTimeoutException)
                {
                    limited++;
                    wrong($"LIMIT\tcaptures.jsonl\t{where}");
                }
            }
        }

        return right;
    }

    /// <summary>With --answers: the engine's verdict and answers under each grammar, where they differ from the data's.</summary>
    private static void Answers(JsonElement r, string pattern, string flags, JsonElement valid, TimeSpan limit, Func<string, string> unescape, int line)
    {
        foreach (string mode in new[] { "unicode", "legacy" })
        {
            bool isValid = valid.GetProperty(mode).GetBoolean();
            RegexFlags modeFlags = Regex.ParseFlags(mode == "unicode" ? flags + "u" : flags);
            Regex? re = null;
            try
            {
                re = Factory.Make(pattern, modeFlags, limit);
            }
            catch (Exception e) when (e is RegexParseException or RegexPatternTooLargeException)
            {
            }

            if ((re is not null) != isValid)
            {
                Overlay.Valid("captures.jsonl", line, mode, re is not null);
            }

            if (re is null)
            {
                continue;
            }

            Regex g = Factory.Make(pattern, modeFlags | RegexFlags.Global, limit);
            Regex y = Factory.Make(pattern, modeFlags | RegexFlags.Sticky, limit);
            int i = -1;
            foreach (JsonElement t in r.GetProperty("tests").EnumerateArray())
            {
                i++;
                if (t.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean())
                {
                    continue;
                }

                try
                {
                    string answer = Overlay.CapturesAnswer(re, g, y, unescape(t.GetProperty("text").GetRawText()), Replacement);
                    if (!isValid || !t.TryGetProperty(mode, out JsonElement a) || a.GetRawText() != answer)
                    {
                        Overlay.Answer("captures.jsonl", line, mode, i, answer);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // A limit gives no answer to write.
                }
            }
        }
    }

    private static int JudgeMode(string pattern, string modeFlags, string text, JsonElement a, TimeSpan limit, string where, Func<string, string> unescape, Action<string> wrong, ref int wrongCount)
    {
        int right = 0;
        RegexFlags flags = Regex.ParseFlags(modeFlags);
        Regex re = Factory.Make(pattern, flags, limit);
        Regex g = Factory.Make(pattern, flags | RegexFlags.Global, limit);
        Regex y = Factory.Make(pattern, flags | RegexFlags.Sticky, limit);
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        bool lone = HasLoneSurrogate(text);
        int[] byteAt = lone ? [] : ByteOffsets(text);

        // exec
        int[][]? exec = Exec(a.GetProperty("exec"));
        Check(ref right, ref wrongCount, wrong, "exec", where, Same(exec, re.Match(text)));
        if (!lone)
        {
            Check(ref right, ref wrongCount, wrong, "exec-utf8", where, SameUtf8(exec, re, utf8, 0, false, byteAt));
        }

        // all
        List<int[][]?> all = [];
        foreach (JsonElement m in a.GetProperty("all").EnumerateArray())
        {
            all.Add(Exec(m));
        }

        bool allOk = true;
        int n = 0;
        foreach (Match m in g.Matches(text))
        {
            if (n >= all.Count)
            {
                // One match more than the data has: wrong, unless the data kept only the first twenty.
                allOk &= all.Count == 20;
                break;
            }

            if (!Same(all[n], m))
            {
                allOk = false;
                break;
            }

            n++;
        }

        if (all.Count < 20 && n != all.Count)
        {
            allOk = false;
        }

        if (all.Count == 20 && n < 20)
        {
            allOk = false;
        }

        Check(ref right, ref wrongCount, wrong, "all", where, allOk);
        if (!lone)
        {
            bool utf8Ok = true;
            n = 0;
            foreach (ValueMatch vm in g.EnumerateMatches(utf8))
            {
                if (n >= all.Count)
                {
                    utf8Ok &= all.Count == 20;
                    break;
                }

                int[][]? e = all[n];
                if (e is null || byteAt[e[0][0]] != vm.Index || byteAt[e[0][1]] - byteAt[e[0][0]] != vm.Length)
                {
                    utf8Ok = false;
                    break;
                }

                n++;
            }

            if ((all.Count < 20 && n != all.Count) || (all.Count == 20 && n < 20))
            {
                utf8Ok = false;
            }

            Check(ref right, ref wrongCount, wrong, "all-utf8", where, utf8Ok);
        }

        // sticky at 1
        if (text.Length >= 1)
        {
            int[][]? sticky = Exec(a.GetProperty("sticky"));
            Check(ref right, ref wrongCount, wrong, "sticky", where, Same(sticky, y.Match(text, 1)));
            if (!lone && byteAt[1] < utf8.Length + 1 && IsCharStart(utf8, byteAt[1]))
            {
                Check(ref right, ref wrongCount, wrong, "sticky-utf8", where, SameUtf8(sticky, y, utf8, byteAt[1], true, byteAt));
            }
        }

        // replace
        Check(ref right, ref wrongCount, wrong, "replace", where, a.GetProperty("replace").GetRawText() == JsonText(re.Replace(text, Replacement)));
        Check(ref right, ref wrongCount, wrong, "replaceAll", where, a.GetProperty("replaceAll").GetRawText() == JsonText(g.Replace(text, Replacement)));
        if (!lone)
        {
            // The same replacements over the UTF-8 text, through the members that write UTF-8 to a buffer writer.
            // V8's answer converted to UTF-8, as the judging rule says: a lone half becomes U+FFFD, since UTF-8 cannot
            // carry one.
            Check(ref right, ref wrongCount, wrong, "replace-utf8", where, ToUtf8Lossy(unescape(a.GetProperty("replace").GetRawText())) == ReplaceUtf8(re, utf8));
            Check(ref right, ref wrongCount, wrong, "replaceAll-utf8", where, ToUtf8Lossy(unescape(a.GetProperty("replaceAll").GetRawText())) == ReplaceUtf8(g, utf8));
        }

        // split
        Check(ref right, ref wrongCount, wrong, "split", where, SameSplit(a.GetProperty("split"), re.Split(text)));
        Check(ref right, ref wrongCount, wrong, "split3", where, SameSplit(a.GetProperty("split3"), re.Split(text, 3)));
        return right;
    }

    private static void Check(ref int right, ref int wrongCount, Action<string> wrong, string what, string where, bool ok)
    {
        if (ok)
        {
            right++;
        }
        else
        {
            wrongCount++;
            wrong($"WRONG\tcaptures.jsonl\t{where}\t{what}");
        }
    }

    private static int[][]? Exec(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        List<int[]> groups = [];
        foreach (JsonElement g in e.EnumerateArray())
        {
            groups.Add(g.ValueKind == JsonValueKind.Null ? [-1, -1] : [g[0].GetInt32(), g[1].GetInt32()]);
        }

        return [.. groups];
    }

    private static bool Same(int[][]? expected, Match m)
    {
        if (expected is null)
        {
            return !m.Success;
        }

        if (!m.Success || m.Groups.Count != expected.Length)
        {
            return false;
        }

        for (int k = 0; k < expected.Length; k++)
        {
            Group g = m.Groups[k];
            if (expected[k][0] < 0)
            {
                if (g.Success)
                {
                    return false;
                }
            }
            else if (!g.Success || g.Index != expected[k][0] || g.Index + g.Length != expected[k][1])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameUtf8(int[][]? expected, Regex re, byte[] utf8, int startAt, bool sticky, int[] byteAt)
    {
        ValueCapture[] caps = new ValueCapture[re.GroupCount + 1];
        bool ok = re.TryMatch(utf8, startAt, caps, out ValueMatch vm);
        if (expected is null)
        {
            return !ok;
        }

        if (!ok || caps.Length != expected.Length)
        {
            return false;
        }

        if (vm.Index != byteAt[expected[0][0]] || vm.Index + vm.Length != byteAt[expected[0][1]])
        {
            return false;
        }

        for (int k = 1; k < expected.Length; k++)
        {
            if (expected[k][0] < 0)
            {
                if (caps[k].Success)
                {
                    return false;
                }
            }
            else if (!caps[k].Success || caps[k].Index != byteAt[expected[k][0]] || caps[k].Index + caps[k].Length != byteAt[expected[k][1]])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSplit(JsonElement expected, string?[] actual)
    {
        if (expected.GetArrayLength() != actual.Length)
        {
            return false;
        }

        int k = 0;
        foreach (JsonElement e in expected.EnumerateArray())
        {
            string? s = actual[k++];
            if (e.ValueKind == JsonValueKind.Null ? s is not null : s is null || e.GetRawText() != JsonText(s))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly byte[] Replacement8 = Encoding.UTF8.GetBytes(Replacement);

    private static string ToUtf8Lossy(string s) => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(s));

    private static string ReplaceUtf8(Regex re, byte[] utf8)
    {
        Utf8Writer writer = new();
        re.Replace(utf8, Replacement8, writer);
        return Encoding.UTF8.GetString(writer.Buffer, 0, writer.Written);
    }

    /// <summary>A growing buffer writer (the runner also targets .NET Framework, which has no ArrayBufferWriter).</summary>
    private sealed class Utf8Writer : System.Buffers.IBufferWriter<byte>
    {
        public byte[] Buffer { get; private set; } = new byte[256];

        public int Written { get; private set; }

        public void Advance(int count) => this.Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            this.Ensure(sizeHint);
            return this.Buffer.AsMemory(this.Written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            this.Ensure(sizeHint);
            return this.Buffer.AsSpan(this.Written);
        }

        private void Ensure(int sizeHint)
        {
            int need = this.Written + Math.Max(sizeHint, 1);
            if (need > this.Buffer.Length)
            {
                byte[] bigger = new byte[Math.Max(need, this.Buffer.Length * 2)];
                Array.Copy(this.Buffer, bigger, this.Written);
                this.Buffer = bigger;
            }
        }
    }

    /// <summary>A string as the data writes it: JSON, ASCII, every other character a \u escape in lower case hex.</summary>
    internal static string JsonText(string s)
    {
        StringBuilder sb = new(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 0x20 || c > 0x7e)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>For each UTF-16 code unit index of a text (and the index past its end), the byte offset in UTF-8; the low half of a pair is its character's third byte.</summary>
    private static int[] ByteOffsets(string s)
    {
        int[] at = new int[s.Length + 1];
        int b = 0;
        for (int k = 0; k < s.Length; k++)
        {
            at[k] = b;
            char c = s[k];
            if (char.IsHighSurrogate(c) && k + 1 < s.Length && char.IsLowSurrogate(s[k + 1]))
            {
                at[k + 1] = b + 2;
                b += 4;
                k++;
            }
            else
            {
                b += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
            }
        }

        at[s.Length] = b;
        return at;
    }

    private static bool IsCharStart(byte[] utf8, int pos) => pos >= utf8.Length || (utf8[pos] & 0xC0) != 0x80;

    private static bool HasLoneSurrogate(string s)
    {
        for (int k = 0; k < s.Length; k++)
        {
            if (char.IsHighSurrogate(s[k]))
            {
                if (k + 1 < s.Length && char.IsLowSurrogate(s[k + 1]))
                {
                    k++;
                    continue;
                }

                return true;
            }

            if (char.IsLowSurrogate(s[k]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Escape(string s)
    {
        StringBuilder sb = new();
        foreach (char c in s)
        {
            if (c < 0x20 || c > 0x7e || c == '\\')
            {
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
