// <copyright file="Overlay.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// With --answers <file>, the runner writes the engine's own answers wherever they differ from the data's, one JSON
// line each, in the shape of the data's records: a validity ({"file", "line", "grammar", "valid"}) or a test's answer
// ({"file", "line", "grammar", "test", "answer"}), the line counted from 1 and the test from 0. The test data's
// overlays for other versions of Unicode (data/unicode/<version>.jsonl) are made this way, by this runner built with
// that version's tables: its answers are V8's everywhere at the data's own version, so with other tables they differ
// only where the tables do. Run with the tables the library ships, it writes nothing; that is the check that the
// lines it writes come from the tables alone.

using System.Collections.Concurrent;
using System.Text;
using Corvus.Ecma262.RegularExpressions;

internal static class Overlay
{
    private static readonly ConcurrentQueue<string> Lines = new();

    /// <summary>Gets or sets a value indicating whether the runner writes its answers where they differ from the data's.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Gets the lines written, in the order of the data.</summary>
    public static IEnumerable<string> Written => Lines.OrderBy(l => l, StringComparer.Ordinal);

    /// <summary>The engine's verdict on a pattern under a grammar, where it differs from the data's.</summary>
    public static void Valid(string file, int line, string grammar, bool valid) =>
        Lines.Enqueue($"{{\"file\":\"{file}\",\"line\":{line},\"grammar\":\"{grammar}\",\"valid\":{(valid ? "true" : "false")}}}");

    /// <summary>The engine's answer to a test under a grammar (JSON, in the data's shape), where it differs from the data's.</summary>
    public static void Answer(string file, int line, string grammar, int test, string answer) =>
        Lines.Enqueue($"{{\"file\":\"{file}\",\"line\":{line},\"grammar\":\"{grammar}\",\"test\":{test},\"answer\":{answer}}}");

    /// <summary>The answer of captures.jsonl to a text under a grammar, as generate-captures.js writes it.</summary>
    public static string CapturesAnswer(Regex re, Regex global, Regex sticky, string text, string replacement)
    {
        StringBuilder sb = new();
        sb.Append("{\"exec\":").Append(Exec(re.Match(text)));
        sb.Append(",\"all\":[");
        int n = 0;
        foreach (Match m in global.Matches(text))
        {
            if (n == 20)
            {
                // The data keeps the first twenty matches of a global search.
                break;
            }

            sb.Append(n++ == 0 ? string.Empty : ",").Append(Exec(m));
        }

        sb.Append("],\"sticky\":").Append(text.Length >= 1 ? Exec(sticky.Match(text, 1)) : "null");
        sb.Append(",\"replace\":").Append(Captures.JsonText(re.Replace(text, replacement)));
        sb.Append(",\"replaceAll\":").Append(Captures.JsonText(global.Replace(text, replacement)));
        sb.Append(",\"split\":").Append(Split(re.Split(text)));
        sb.Append(",\"split3\":").Append(Split(re.Split(text, 3)));
        return sb.Append('}').ToString();
    }

    private static string Exec(Match m)
    {
        if (!m.Success)
        {
            return "null";
        }

        StringBuilder sb = new("[");
        for (int k = 0; k < m.Groups.Count; k++)
        {
            Group g = m.Groups[k];
            sb.Append(k == 0 ? string.Empty : ",").Append(g.Success ? $"[{g.Index},{g.Index + g.Length}]" : "null");
        }

        return sb.Append(']').ToString();
    }

    private static string Split(string?[] parts) =>
        "[" + string.Join(",", parts.Select(p => p is null ? "null" : Captures.JsonText(p))) + "]";
}
