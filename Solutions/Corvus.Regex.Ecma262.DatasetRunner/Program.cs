// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Runs the library over the ECMA-262 regular expression test data (github.com/corvus-dotnet/ecma262-regex-test-data).
//
//   DatasetRunner <data directory> <wrong-cases output file> [file filter] [utf16|utf8|both] [--answers <file>]
//                 [--compliance <file>] [--edition current|es2020] [--unicode 13|17|18|latest]
//
// Each record is asked about under each grammar V8 found it valid in: the record's flags with u added (the
// `unicode` answers) and the record's flags alone (the `legacy` answers). A text is matched as UTF-16 and as UTF-8.
// A text with a lone surrogate has no UTF-8 form and is matched as UTF-16 alone. Records marked v8_deviation are
// left out. Any difference from V8 is a wrong answer; a refusal of a valid pattern, an acceptance of an invalid
// one, and a match that reaches its limit are counted apart and listed too. ill-formed-utf8.jsonl holds bytes that
// are not UTF-8, each one answer over UTF-8 under each grammar the pattern is valid in, whose right answer is a
// refusal: the ArgumentException of the members that take UTF-8. With --answers, the runner also writes its own
// answers where they differ from the data's (Overlay.cs), from which the data's overlays for other versions of Unicode
// are made. With --edition and --unicode, the patterns are read with that edition's grammar and that version's
// character database, and the data is the data as `node edition.js apply` or `node unicode.js apply` writes it for them.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Corvus.Ecma262.RegularExpressions;

int complianceAt = Array.IndexOf(args, "--compliance");
string? compliancePath = complianceAt >= 0 ? args[complianceAt + 1] : null;
if (complianceAt >= 0)
{
    args = [.. args.Take(complianceAt), .. args.Skip(complianceAt + 2)];
}

// The edition of the grammar and the version of Unicode, for the data as `node edition.js apply` and
// `node unicode.js apply` write it.
int editionAt = Array.IndexOf(args, "--edition");
if (editionAt >= 0)
{
    Factory.Edition = args[editionAt + 1] switch
    {
        "current" => RegexEdition.Current,
        "es2020" => RegexEdition.Es2020,
        string e => throw new ArgumentException($"The edition is current or es2020, not {e}."),
    };
    args = [.. args.Take(editionAt), .. args.Skip(editionAt + 2)];
}

int unicodeAt = Array.IndexOf(args, "--unicode");
if (unicodeAt >= 0)
{
    Factory.UnicodeVersion = args[unicodeAt + 1] switch
    {
        "13" or "13.0" => RegexUnicodeVersion.V13,
        "17" or "17.0" => RegexUnicodeVersion.V17,
        "18" or "18.0" => RegexUnicodeVersion.V18,
        "latest" => RegexUnicodeVersion.Latest,
        string v => throw new ArgumentException($"The Unicode version is 13, 17, 18 or latest, not {v}."),
    };
    args = [.. args.Take(unicodeAt), .. args.Skip(unicodeAt + 2)];
}

int answersAt = Array.IndexOf(args, "--answers");
string? answersPath = answersAt >= 0 ? args[answersAt + 1] : null;
if (answersAt >= 0)
{
    args = [.. args.Take(answersAt), .. args.Skip(answersAt + 2)];
    Overlay.Enabled = true;
}

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

    Parallel.For(0, lines.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
    {
        string line = lines[index];
        int lineNumber = index + 1;
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement r = doc.RootElement;
        if (r.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean())
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

            int ok = Captures.Judge(r, pattern, limit, Unescape, wrongCases.Enqueue, out int bad, out int lim, out int refused, out int accepted, lineNumber);
            Interlocked.Add(ref fileRight, ok);
            Interlocked.Add(ref fileWrong, bad);
            Interlocked.Add(ref fileLimited, lim);
            Interlocked.Add(ref fileRefused, refused);
            Interlocked.Add(ref refusedValid, refused);
            Interlocked.Add(ref fileAccepted, accepted);
            Interlocked.Add(ref acceptedInvalid, accepted);
            return;
        }

        string flags = r.GetProperty("flags").GetString()!;
        JsonElement valid = r.GetProperty("valid");
        bool validUnicode = valid.GetProperty("unicode").GetBoolean();
        bool validLegacy = valid.GetProperty("legacy").GetBoolean();
        r.TryGetProperty("tests", out JsonElement tests);
        // The three modes: the u flag ("unicode"), no flag ("legacy") and, in the file that has it, the v flag ("unicode_sets").
        List<(string Suffix, string AnswerName, bool ExpectedValid)> modes = [("u", "unicode", validUnicode), (string.Empty, "legacy", validLegacy)];
        if (valid.TryGetProperty("unicode_sets", out JsonElement validSets))
        {
            modes.Add(("v", "unicode_sets", validSets.GetBoolean()));
        }

        foreach ((string suffix, string answerName, bool expectedValid) in modes)
        {
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

            if (Overlay.Enabled && (regex is not null) != expectedValid)
            {
                Overlay.Valid(name, lineNumber, answerName, regex is not null);
                if (regex is not null && tests.ValueKind == JsonValueKind.Array && name != "ill-formed-utf8.jsonl")
                {
                    // A pattern valid here and not in the data has no answers there: each test is answered here.
                    int i = 0;
                    foreach (JsonElement t in tests.EnumerateArray())
                    {
                        Overlay.Answer(name, lineNumber, answerName, i++, regex.IsMatch(Unescape(t.GetProperty("text").GetRawText())) ? "true" : "false");
                    }
                }
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

            if (name == "ill-formed-utf8.jsonl")
            {
                if (utf8)
                {
                    foreach (JsonElement t in tests.EnumerateArray())
                    {
                        JudgeIllFormed(regex, pattern, modeFlags, t.GetProperty("bytes").GetString()!, name, ref fileRight, ref fileWrong, ref fileLimited);
                    }
                }

                continue;
            }

            int testIndex = -1;
            foreach (JsonElement t in tests.EnumerateArray())
            {
                testIndex++;
                if (!t.TryGetProperty(answerName, out JsonElement answer))
                {
                    continue;
                }

                string text = Unescape(t.GetProperty("text").GetRawText());
                bool expected = answer.GetBoolean();
                if (Overlay.Enabled)
                {
                    try
                    {
                        bool actual = regex.IsMatch(text);
                        if (actual != expected)
                        {
                            Overlay.Answer(name, lineNumber, answerName, testIndex, actual ? "true" : "false");
                        }
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // Counted as a limit below; a limit gives no answer to write.
                    }
                }
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

// The exec calls of test262, in a directory of their own (Exec.cs): judged with the default filter, or when the filter
// names them.
string execPath = Path.Combine(directory, "exec", "test262.jsonl");
if ((filter == "*.jsonl" || filter.StartsWith("exec", StringComparison.Ordinal)) && File.Exists(execPath))
{
    Exec.Counts exec = Exec.Judge(execPath, utf16, utf8, limit, Unescape, wrongCases.Enqueue);
    right += exec.Right;
    wrong += exec.Wrong;
    refusedValid += exec.Refused;
    limited += exec.Limited;
    records += exec.Records;
    Console.WriteLine($"{Exec.File,-24} right {exec.Right,10:N0}  wrong {exec.Wrong,6:N0}  refused-valid {exec.Refused,4}  accepted-invalid {0,4}  limit {exec.Limited,4}  {clock.Elapsed:mm\\:ss}");
}

File.WriteAllLines(wrongPath, wrongCases.OrderBy(c => c, StringComparer.Ordinal));
if (answersPath is not null)
{
    File.WriteAllLines(answersPath, Overlay.Written);
}
if (compliancePath is not null)
{
    WriteCompliance(compliancePath);
}

Console.WriteLine();
Console.WriteLine($"records {records:N0} (skipped {skipped} deviations)  right {right:N0}  wrong {wrong:N0}  refused-valid {refusedValid}  accepted-invalid {acceptedInvalid}  limit {limited}  errors {errors}  in {clock.Elapsed}");
return wrong + refusedValid + acceptedInvalid + errors == 0 ? 0 : 1;

// The engine's compliance as the benchmark page reads it (docs/implementing-a-source.md in
// corvus-regex-ecma262-benchmarks): the counts, this runner and the calls it makes in each encoding it asked in, and
// what it leaves out, which for this library is nothing.
void WriteCompliance(string path)
{
    using FileStream stream = File.Create(path);
    using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    json.WriteStartObject();
    json.WriteNumber("right", right);
    json.WriteNumber("wrong", wrong);
    json.WriteNumber("refused", refusedValid);
    json.WriteNumber("acceptedInvalid", acceptedInvalid);
    json.WriteNumber("limited", limited + errors);
    json.WriteStartObject("judge");
    json.WriteString("runner", "https://github.com/corvus-dotnet/Corvus.Regex.Ecma262/blob/main/Solutions/Corvus.Regex.Ecma262.DatasetRunner/Program.cs");
    json.WriteStartObject("calls");
    if (encodings is "both" or "utf16")
    {
        json.WriteStartArray("utf16");
        foreach (string call in (string[])["Regex.IsMatch(string)", "Regex.Match(string, int)", "Regex.Matches(string)", "Regex.Replace(string, string)", "Regex.Split(string)"])
        {
            json.WriteStringValue(call);
        }

        json.WriteEndArray();
    }

    if (encodings is "both" or "utf8")
    {
        json.WriteStartArray("utf8");
        foreach (string call in (string[])["Regex.IsMatch(ReadOnlySpan<byte>)", "Regex.TryMatch(ReadOnlySpan<byte>, int, Span<ValueCapture>, out ValueMatch)", "Regex.EnumerateMatches(ReadOnlySpan<byte>)", "Regex.Replace(ReadOnlySpan<byte>, ReadOnlySpan<byte>, IBufferWriter<byte>)"])
        {
            json.WriteStringValue(call);
        }

        json.WriteEndArray();
    }

    json.WriteEndObject();
    json.WriteEndObject();
    json.WriteStartArray("excludes");
    json.WriteEndArray();
    json.WriteEndObject();
}

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

void JudgeIllFormed(Regex regex, string pattern, string modeFlags, string hex, string name, ref long fileRight, ref long fileWrong, ref long fileLimited)
{
    byte[] bytes = new byte[hex.Length / 2];
    for (int k = 0; k < bytes.Length; k++)
    {
        bytes[k] = Convert.ToByte(hex.Substring(2 * k, 2), 16);
    }

    try
    {
        bool actual = regex.IsMatch(bytes);
        Interlocked.Increment(ref fileWrong);
        wrongCases.Enqueue($"WRONG\t{name}\t/{Escape(pattern)}/{modeFlags}\t{hex}\tutf8\tread as UTF-8 ({actual}), not refused");
    }
    catch (RegexMatchTimeoutException e)
    {
        Interlocked.Increment(ref fileLimited);
        wrongCases.Enqueue($"LIMIT\t{name}\t/{Escape(pattern)}/{modeFlags}\t{hex}\tutf8\t{(e.ExceededMemory ? "memory" : "time")}");
    }
    catch (ArgumentException)
    {
        Interlocked.Increment(ref fileRight);
    }
    catch (Exception e)
    {
        Interlocked.Increment(ref errors);
        wrongCases.Enqueue($"ERROR\t{name}\t/{Escape(pattern)}/{modeFlags}\t{hex}\tutf8\t{e.GetType().Name}: {First(e.Message)}");
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
