// <copyright file="Exec.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Judges exec/test262.jsonl: each call of exec that test262's tests make, as the data's README says ("How a runner
// judges an engine"). A record is a pattern with its own flags (g, y and d among them) and its calls; a call is a text,
// the lastIndex exec reads (0 without g or y) and V8's answer: null, or the positions of the match and of each group,
// the named groups and, with g or y, the lastIndex exec leaves. Each call is one answer over UTF-16 and, when the text
// holds no lone surrogate and lastIndex does not fall between the halves of a pair, one more over UTF-8, with positions
// in bytes (through Captures.ByteOffsets). A pattern the engine refuses is one refused-valid, and its calls go
// unanswered.

using System.Text;
using System.Text.Json;
using Corvus.Ecma262.RegularExpressions;

internal static class Exec
{
    /// <summary>The file's place in the data directory.</summary>
    public const string File = "exec/test262.jsonl";

    /// <summary>The counts of one file, as the runner prints them.</summary>
    public sealed class Counts
    {
        public long Right;
        public long Wrong;
        public long Refused;
        public long Limited;
        public long Records;
    }

    /// <summary>Judges every call of the file.</summary>
    public static Counts Judge(string path, bool utf16, bool utf8, TimeSpan limit, Func<string, string> unescape, Action<string> wrong)
    {
        Counts counts = new();
        string[] lines = System.IO.File.ReadAllLines(path);
        Parallel.For(0, lines.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
        {
            using JsonDocument doc = JsonDocument.Parse(lines[index]);
            JsonElement r = doc.RootElement;
            Interlocked.Increment(ref counts.Records);
            string pattern = unescape(r.GetProperty("pattern").GetRawText());
            string flags = r.GetProperty("flags").GetString()!;
            Regex re;
            try
            {
                re = Factory.Make(pattern, Regex.ParseFlags(flags), limit);
            }
            catch (Exception e) when (e is RegexParseException or RegexPatternTooLargeException)
            {
                Interlocked.Increment(ref counts.Refused);
                wrong($"REFUSED\t{File}\t/{Captures.Escape(pattern)}/{flags}\t{First(e.Message)}");
                return;
            }

            bool carries = flags.IndexOf('g') >= 0 || flags.IndexOf('y') >= 0;
            int i = -1;
            foreach (JsonElement t in r.GetProperty("tests").EnumerateArray())
            {
                i++;
                if (t.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean())
                {
                    continue;
                }

                string text = unescape(t.GetProperty("text").GetRawText());
                // A lastIndex past the end of the text gives null; test262 sets it as far as 2^53 - 1 and Number.MAX_VALUE,
                // so it is read as a double, and the text's length plus one stands for any larger.
                double read = t.GetProperty("lastIndex").GetDouble();
                int lastIndex = read > text.Length ? text.Length + 1 : (int)read;
                JsonElement answer = t.GetProperty("answer");
                string where = $"/{Captures.Escape(pattern)}/{flags}\t{Captures.Escape(text)}\t{t.GetProperty("lastIndex").GetRawText()}";
                if (Overlay.Enabled)
                {
                    try
                    {
                        string mine = AnswerOf(re, text, lastIndex, carries);
                        if (answer.GetRawText() != mine)
                        {
                            Overlay.Answer(File, index + 1, GrammarOf(flags), i, mine);
                        }
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // A limit gives no answer to write.
                    }
                }

                if (utf16)
                {
                    Judge(counts, wrong, where, "utf16", () => SameUtf16(re, text, lastIndex, carries, answer));
                }

                if (utf8 && !Captures.HasLoneSurrogate(text) && !InsidePair(text, lastIndex))
                {
                    Judge(counts, wrong, where, "utf8", () => SameUtf8(re, text, lastIndex, carries, answer));
                }
            }
        });

        return counts;
    }

    private static void Judge(Counts counts, Action<string> wrong, string where, string encoding, Func<bool> same)
    {
        try
        {
            if (same())
            {
                Interlocked.Increment(ref counts.Right);
            }
            else
            {
                Interlocked.Increment(ref counts.Wrong);
                wrong($"WRONG\t{File}\t{where}\t{encoding}");
            }
        }
        catch (RegexMatchTimeoutException)
        {
            Interlocked.Increment(ref counts.Limited);
            wrong($"LIMIT\t{File}\t{where}\t{encoding}");
        }
    }

    /// <summary>The grammar of a record's flags, for the overlay's lines: v, u, or neither.</summary>
    private static string GrammarOf(string flags) => flags.IndexOf('v') >= 0 ? "unicode_sets" : flags.IndexOf('u') >= 0 ? "unicode" : "legacy";

    /// <summary>Whether a position is between the halves of a surrogate pair, where UTF-8 has no position.</summary>
    private static bool InsidePair(string s, int at) =>
        at > 0 && at < s.Length && char.IsLowSurrogate(s[at]) && char.IsHighSurrogate(s[at - 1]);

    /// <summary>exec over UTF-16: from lastIndex with g or y (at it alone with y), from 0 without; null past the end.</summary>
    private static Match? Run(Regex re, string text, int lastIndex, bool carries)
    {
        if (carries && lastIndex > text.Length)
        {
            return null;
        }

        Match m = re.Match(text, carries ? lastIndex : 0);
        return m.Success ? m : null;
    }

    private static bool SameUtf16(Regex re, string text, int lastIndex, bool carries, JsonElement answer)
    {
        Match? m = Run(re, text, lastIndex, carries);
        if (answer.ValueKind == JsonValueKind.Null || m is null)
        {
            return answer.ValueKind == JsonValueKind.Null && m is null;
        }

        JsonElement captures = answer.GetProperty("captures");
        if (m.Groups.Count != captures.GetArrayLength())
        {
            return false;
        }

        for (int k = 0; k < m.Groups.Count; k++)
        {
            if (!SameSpan(captures[k], m.Groups[k].Success, m.Groups[k].Index, m.Groups[k].Index + m.Groups[k].Length, null))
            {
                return false;
            }
        }

        if (!SameGroups(re, answer, name => m.Groups[name] is Group g && g.Success ? (g.Index, g.Index + g.Length) : null, null))
        {
            return false;
        }

        return !carries || answer.GetProperty("lastIndex").GetInt32() == m.Index + m.Length;
    }

    private static bool SameUtf8(Regex re, string text, int lastIndex, bool carries, JsonElement answer)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        int[] byteAt = Captures.ByteOffsets(text);
        ValueCapture[] caps = new ValueCapture[re.GroupCount + 1];
        bool ok = (!carries || lastIndex <= text.Length) && re.TryMatch(utf8, carries ? byteAt[lastIndex] : 0, caps, out _);
        if (answer.ValueKind == JsonValueKind.Null || !ok)
        {
            return answer.ValueKind == JsonValueKind.Null && !ok;
        }

        JsonElement captures = answer.GetProperty("captures");
        if (caps.Length != captures.GetArrayLength())
        {
            return false;
        }

        for (int k = 0; k < caps.Length; k++)
        {
            if (!SameSpan(captures[k], caps[k].Success, caps[k].Index, caps[k].Index + caps[k].Length, byteAt))
            {
                return false;
            }
        }

        // A name's group is the one of that name that took part (several groups may share a name, in alternatives).
        Dictionary<string, List<int>> numbers = [];
        foreach (string name in re.GetGroupNames())
        {
            numbers[name] = [];
        }

        for (int k = 1; k <= re.GroupCount; k++)
        {
            if (numbers.TryGetValue(re.GroupNameFromNumber(k), out List<int>? list))
            {
                list.Add(k);
            }
        }

        (int, int)? Named(string name)
        {
            foreach (int k in numbers[name])
            {
                if (caps[k].Success)
                {
                    return (caps[k].Index, caps[k].Index + caps[k].Length);
                }
            }

            return null;
        }

        if (!SameGroups(re, answer, Named, byteAt))
        {
            return false;
        }

        return !carries || byteAt[answer.GetProperty("lastIndex").GetInt32()] == caps[0].Index + caps[0].Length;
    }

    /// <summary>A span of the answer (null for a group that took no part) against the engine's, through byteAt for UTF-8.</summary>
    private static bool SameSpan(JsonElement expected, bool success, int start, int end, int[]? byteAt)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            return !success;
        }

        int s = expected[0].GetInt32(), e = expected[1].GetInt32();
        return success && start == (byteAt is null ? s : byteAt[s]) && end == (byteAt is null ? e : byteAt[e]);
    }

    /// <summary>The named groups: present exactly when the pattern has names, with the same names and spans.</summary>
    private static bool SameGroups(Regex re, JsonElement answer, Func<string, (int Start, int End)?> named, int[]? byteAt)
    {
        string[] names = re.GetGroupNames();
        if (!answer.TryGetProperty("groups", out JsonElement groups))
        {
            return names.Length == 0;
        }

        int count = 0;
        foreach (JsonProperty p in groups.EnumerateObject())
        {
            count++;
            if (Array.IndexOf(names, p.Name) < 0)
            {
                return false;
            }

            (int Start, int End)? span = named(p.Name);
            if (!SameSpan(p.Value, span is not null, span?.Start ?? -1, span?.End ?? -1, byteAt))
            {
                return false;
            }
        }

        return count == names.Length;
    }

    /// <summary>With --answers: the engine's answer to a call, in the file's form.</summary>
    private static string AnswerOf(Regex re, string text, int lastIndex, bool carries)
    {
        Match? m = Run(re, text, lastIndex, carries);
        if (m is null)
        {
            return "null";
        }

        StringBuilder sb = new("{\"captures\":[");
        for (int k = 0; k < m.Groups.Count; k++)
        {
            Group g = m.Groups[k];
            sb.Append(k == 0 ? string.Empty : ",").Append(g.Success ? $"[{g.Index},{g.Index + g.Length}]" : "null");
        }

        sb.Append(']');
        string[] names = re.GetGroupNames();
        if (names.Length > 0)
        {
            sb.Append(",\"groups\":{");
            for (int k = 0; k < names.Length; k++)
            {
                Group g = m.Groups[names[k]];
                sb.Append(k == 0 ? string.Empty : ",").Append(Captures.JsonText(names[k])).Append(':')
                    .Append(g.Success ? $"[{g.Index},{g.Index + g.Length}]" : "null");
            }

            sb.Append('}');
        }

        if (carries)
        {
            sb.Append(",\"lastIndex\":").Append(m.Index + m.Length);
        }

        return sb.Append('}').ToString();
    }

    private static string First(string s)
    {
        int nl = s.IndexOf('\n');
        return nl < 0 ? s : s.Substring(0, nl);
    }
}
