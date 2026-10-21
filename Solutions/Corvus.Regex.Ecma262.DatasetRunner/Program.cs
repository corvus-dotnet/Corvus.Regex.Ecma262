// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Runs the library over the ECMA-262 regular expression test data (github.com/corvus-dotnet/ecma262-regex-test-data).
//
//   DatasetRunner <data directory> <wrong-cases output file> [file filter] [utf16|utf8|both]
//
// Each record is asked about under each grammar V8 found it valid in: the record's flags with u added (the
// `unicode` answers), the record's flags alone (the `legacy` answers) and, in every record that has them (those of
// unicode-sets.jsonl and of the files from test262), the flags with v added (the `unicode_sets` answers). A text is
// matched as UTF-16 and as UTF-8. A text with a lone surrogate has no UTF-8 form and is matched as UTF-16 alone,
// whatever the file. Records and tests marked v8_deviation are left out, but for those marked `unicode-version`,
// where V8 has the Unicode of 16 and test262 that of 17: the library has Unicode 17, so they are judged by
// test262's verdicts (`test262_valid`) and answers (`test262_answer`) in the grammars those name, and left out in
// the others. A marked record is judged in each grammar its `test262_valid` names, by that verdict, and its tests
// in that grammar by their `test262_answer`; a marked test (of an unmarked record) in each grammar its
// `test262_answer` names, by that answer. Any difference from V8 (or, there, from test262) is a wrong answer; a
// refusal of a valid pattern, an acceptance of an invalid one, and a match that reaches its limit are counted apart
// and listed too. captures.jsonl is judged on exec, the global search, the sticky flag, replace and split.
//
// These are the judging rules the runners of the other ports follow.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Corvus.Ecma262.RegularExpressions;

string directory = args[0];
string wrongPath = args[1];
string filter = args.Length > 2 ? args[2] : "*.jsonl";
string encodings = args.Length > 3 ? args[3] : "both";
int generated = args.Length > 4 && args[4].StartsWith("--generated", StringComparison.Ordinal) ? (args[4].Length > 12 ? int.Parse(args[4].Substring(12), System.Globalization.CultureInfo.InvariantCulture) : 1) : 0;
bool utf16 = encodings is "both" or "utf16";
bool utf8 = encodings is "both" or "utf8";
TimeSpan limit = TimeSpan.FromSeconds(2);

long right = 0, wrong = 0, refusedValid = 0, acceptedInvalid = 0, limited = 0, errors = 0, records = 0, skipped = 0;
ConcurrentQueue<string> wrongCases = new();
Stopwatch clock = Stopwatch.StartNew();

foreach (string file in Directory.GetFiles(directory, filter).OrderBy(f => f, StringComparer.Ordinal))
{
    string name = Path.GetFileName(file);
    long fileRight = 0, fileWrong = 0, fileRefused = 0, fileAccepted = 0, fileLimited = 0;
    string[] lines = File.ReadAllLines(file);
    if (generated > 0)
    {
        // Every pattern of the file (one in N) as straight-line code, compiled and loaded, before the answers are
        // checked with it in place of the interpreter.
        Factory.Prepare(lines, generated, Unescape, limit);
        Console.WriteLine($"{name,-24} generated {Factory.Count,10:N0} programs in {clock.Elapsed:mm\\:ss}");
    }

    Parallel.ForEach(lines, new ParallelOptions { MaxDegreeOfParallelism = 4 }, line =>
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement r = doc.RootElement;
        if (Deviations.IsSkipped(r))
        {
            Interlocked.Increment(ref skipped);
            return;
        }

        Interlocked.Increment(ref records);
        string pattern = Unescape(r.GetProperty("pattern").GetRawText());
        if (name == "captures.jsonl")
        {
            if (!utf16)
            {
                return;
            }

            int ok = Captures.Judge(r, pattern, limit, Unescape, wrongCases.Enqueue, out int bad, out int lim);
            Interlocked.Add(ref fileRight, ok);
            Interlocked.Add(ref fileWrong, bad);
            Interlocked.Add(ref fileLimited, lim);
            return;
        }

        string flags = r.GetProperty("flags").GetString()!;
        JsonElement valid = r.GetProperty("valid");
        bool validUnicode = valid.GetProperty("unicode").GetBoolean();
        bool validLegacy = valid.GetProperty("legacy").GetBoolean();
        r.TryGetProperty("tests", out JsonElement tests);
        // The three modes: the u flag ("unicode"), no flag ("legacy") and, in the records that have it, the v flag ("unicode_sets").
        List<(string Suffix, string AnswerName, bool V8Valid)> modes = [("u", "unicode", validUnicode), (string.Empty, "legacy", validLegacy)];
        if (valid.TryGetProperty("unicode_sets", out JsonElement validSets))
        {
            modes.Add(("v", "unicode_sets", validSets.GetBoolean()));
        }

        // A record marked for the Unicode version is judged in the grammars test262 gives a verdict for, by it.
        bool recordByTest262 = Deviations.IsRecordByTest262(r);
        foreach ((string suffix, string answerName, bool v8Valid) in modes)
        {
            if (!Deviations.TryGetExpectedValid(r, answerName, v8Valid, out bool expectedValid))
            {
                continue;
            }

            string modeFlags = flags + suffix;
            Regex? regex = null;
            string? error = null;
            try
            {
                regex = Factory.Make(pattern, Regex.ParseFlags(modeFlags), limit);
            }
            catch (RegexParseException e)
            {
                error = "PARSE: " + e.Message;
            }
            catch (RegexPatternTooLargeException e)
            {
                error = "TOO-LARGE: " + e.Message;
            }
            catch (Exception e)
            {
                error = "ERROR: " + e.GetType().Name + ": " + First(e.Message);
                Interlocked.Increment(ref errors);
            }

            if (regex is null)
            {
                if (expectedValid)
                {
                    Interlocked.Increment(ref refusedValid);
                    Interlocked.Increment(ref fileRefused);
                    wrongCases.Enqueue($"REFUSED\t{name}\t/{Escape(pattern)}/{modeFlags}\t{error}");
                }

                continue;
            }

            if (!expectedValid)
            {
                Interlocked.Increment(ref acceptedInvalid);
                Interlocked.Increment(ref fileAccepted);
                wrongCases.Enqueue($"ACCEPTED-INVALID\t{name}\t/{Escape(pattern)}/{modeFlags}");
                continue;
            }

            if (tests.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement t in tests.EnumerateArray())
            {
                // A test marked for the Unicode version, or any test of a record so marked, is judged by test262's
                // answer in this grammar, and left out where it has none. A test marked otherwise is left out.
                if (!Deviations.TryGetExpected(t, recordByTest262, answerName, out bool expected))
                {
                    continue;
                }

                string text = Unescape(t.GetProperty("text").GetRawText());
                if (utf16)
                {
                    Judge(regex, pattern, modeFlags, text, expected, name, "utf16", ref fileRight, ref fileWrong, ref fileLimited, () => regex.IsMatch(text));
                }

                if (utf8 && !HasLoneSurrogate(text))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    Judge(regex, pattern, modeFlags, text, expected, name, "utf8", ref fileRight, ref fileWrong, ref fileLimited, () => regex.IsMatch(bytes));
                }
            }
        }
    });

    Interlocked.Add(ref right, fileRight);
    Interlocked.Add(ref wrong, fileWrong);
    Interlocked.Add(ref limited, fileLimited);
    Console.WriteLine($"{name,-24} right {fileRight,10:N0}  wrong {fileWrong,6:N0}  refused-valid {fileRefused,4}  accepted-invalid {fileAccepted,4}  limit {fileLimited,4}  {clock.Elapsed:mm\\:ss}");
}

File.WriteAllLines(wrongPath, wrongCases.OrderBy(c => c, StringComparer.Ordinal));
Console.WriteLine();
Console.WriteLine($"records {records:N0} (skipped {skipped} deviations)  right {right:N0}  wrong {wrong:N0}  refused-valid {refusedValid}  accepted-invalid {acceptedInvalid}  limit {limited}  errors {errors}  in {clock.Elapsed}");
return wrong + refusedValid + acceptedInvalid + errors == 0 ? 0 : 1;

void Judge(Regex regex, string pattern, string modeFlags, string text, bool expected, string name, string encoding, ref long fileRight, ref long fileWrong, ref long fileLimited, Func<bool> run)
{
    try
    {
        bool actual = run();
        if (actual == expected)
        {
            Interlocked.Increment(ref fileRight);
        }
        else
        {
            Interlocked.Increment(ref fileWrong);
            wrongCases.Enqueue($"WRONG\t{name}\t/{Escape(pattern)}/{modeFlags}\t{Escape(text)}\t{encoding}\texpected {expected}");
        }
    }
    catch (RegexMatchTimeoutException e)
    {
        Interlocked.Increment(ref fileLimited);
        wrongCases.Enqueue($"LIMIT\t{name}\t/{Escape(pattern)}/{modeFlags}\t{Escape(text)}\t{encoding}\t{(e.ExceededMemory ? "memory" : "time")}");
    }
    catch (Exception e)
    {
        Interlocked.Increment(ref errors);
        wrongCases.Enqueue($"ERROR\t{name}\t/{Escape(pattern)}/{modeFlags}\t{Escape(text)}\t{encoding}\t{e.GetType().Name}: {First(e.Message)}");
    }
}

// Reads a JSON string literal, lone surrogates included (System.Text.Json refuses them).
static string Unescape(string literal)
{
    StringBuilder sb = new(literal.Length);
    for (int k = 1; k < literal.Length - 1; k++)
    {
        char c = literal[k];
        if (c != '\\')
        {
            sb.Append(c);
            continue;
        }

        c = literal[++k];
        switch (c)
        {
            case 'u':
                sb.Append((char)Convert.ToInt32(literal.Substring(k + 1, 4), 16));
                k += 4;
                break;
            case 'n':
                sb.Append('\n');
                break;
            case 'r':
                sb.Append('\r');
                break;
            case 't':
                sb.Append('\t');
                break;
            case 'b':
                sb.Append('\b');
                break;
            case 'f':
                sb.Append('\f');
                break;
            default:
                sb.Append(c);
                break;
        }
    }

    return sb.ToString();
}

static bool HasLoneSurrogate(string s)
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

static string Escape(string s)
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

static string First(string s)
{
    int nl = s.IndexOf('\n');
    return nl < 0 ? s : s.Substring(0, nl);
}
