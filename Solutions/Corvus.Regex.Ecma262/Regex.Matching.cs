// <copyright file="Regex.Matching.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The members that report where a match is and what it captured, and those built on them.</summary>
public sealed partial class Regex
{
    /// <summary>Gets each group name with the numbers of its groups.</summary>
    internal List<KeyValuePair<string, int[]>> GroupNames { get; }

    /// <summary>The first match of a pattern in a text, with no flags.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The match, or <see cref="Match.Empty"/>.</returns>
    public static Match Match(string input, string pattern) => new Regex(pattern).Match(input);

    /// <summary>The first match of a pattern in a text.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The match, or <see cref="Match.Empty"/>.</returns>
    public static Match Match(string input, string pattern, RegexFlags flags) => new Regex(pattern, flags).Match(input);

    /// <summary>Every match of a pattern in a text.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The matches.</returns>
    public static MatchCollection Matches(string input, string pattern, RegexFlags flags = RegexFlags.None) => new Regex(pattern, flags).Matches(input);

    /// <summary>Replaces the matches of a pattern in a text (every match with <see cref="RegexFlags.Global"/>, the first otherwise).</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="replacement">The replacement (see <see cref="Match.Result"/>).</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The text with the replacements made.</returns>
    public static string Replace(string input, string pattern, string replacement, RegexFlags flags = RegexFlags.None) => new Regex(pattern, flags).Replace(input, replacement);

    /// <summary>Splits a text at the matches of a pattern, as <c>String.prototype.split</c> does.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The pieces.</returns>
    public static string?[] Split(string input, string pattern, RegexFlags flags = RegexFlags.None) => new Regex(pattern, flags).Split(input);

    /// <summary>
    /// Escapes a text so that it matches itself as a pattern, as <c>RegExp.escape</c> of ECMAScript 2025 does: a
    /// first character that is a letter or a digit, the syntax characters, the other punctuators, white space, line
    /// terminators and lone surrogates are written as escapes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The pattern.</returns>
    public static string Escape(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        StringBuilder sb = new(text.Length + 8);
        for (int k = 0; k < text.Length; k++)
        {
            char c = text[k];
            if (k == 0 && ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
            {
                sb.Append("\\x").Append(((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }

            if ("^$\\.*+?()[]{}|/".IndexOf(c) >= 0)
            {
                sb.Append('\\').Append(c);
                continue;
            }

            switch (c)
            {
                case '\t':
                    sb.Append("\\t");
                    continue;
                case '\n':
                    sb.Append("\\n");
                    continue;
                case '\v':
                    sb.Append("\\v");
                    continue;
                case '\f':
                    sb.Append("\\f");
                    continue;
                case '\r':
                    sb.Append("\\r");
                    continue;
                default:
                    break;
            }

            if (char.IsHighSurrogate(c) && k + 1 < text.Length && char.IsLowSurrogate(text[k + 1]))
            {
                // A surrogate pair is a character, and stands for itself.
                sb.Append(c).Append(text[k + 1]);
                k++;
                continue;
            }

            if (",-=<>#&!%:;@~'`\"".IndexOf(c) >= 0 || IsWhiteSpaceOrLineTerminator(c) || char.IsSurrogate(c))
            {
                if (c <= 0xFF)
                {
                    sb.Append("\\x").Append(((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                }

                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Reverses <see cref="Escape"/>: reads the escapes it writes, and a backslash before any other character as that character.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The text.</returns>
    public static string Unescape(string pattern)
    {
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        StringBuilder sb = new(pattern.Length);
        for (int k = 0; k < pattern.Length; k++)
        {
            char c = pattern[k];
            if (c != '\\' || k + 1 >= pattern.Length)
            {
                sb.Append(c);
                continue;
            }

            char d = pattern[++k];
            switch (d)
            {
                case 't':
                    sb.Append('\t');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'v':
                    sb.Append('\v');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 'x' when k + 2 < pattern.Length && TryHex(pattern, k + 1, 2, out int x):
                    sb.Append((char)x);
                    k += 2;
                    break;
                case 'u' when k + 4 < pattern.Length && TryHex(pattern, k + 1, 4, out int u):
                    sb.Append((char)u);
                    k += 4;
                    break;
                default:
                    sb.Append(d);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>The first match in a text.</summary>
    /// <param name="input">The text.</param>
    /// <returns>The match, or <see cref="Match.Empty"/>.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public Match Match(string input) => this.Match(input, 0);

    /// <summary>The first match in a text at or after a position (at that position alone with <see cref="RegexFlags.Sticky"/>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <returns>The match, or <see cref="Match.Empty"/>.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public Match Match(string input, int startAt)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        ValidateStart(input.Length, startAt);
        return this.MatchAt(input, this.Normalize(input.AsSpan(), startAt));
    }

    /// <summary>Every match in a text, as a global search finds them.</summary>
    /// <param name="input">The text.</param>
    /// <returns>The matches.</returns>
    public MatchCollection Matches(string input) => this.Matches(input, 0);

    /// <summary>Every match in a text at or after a position.</summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <returns>The matches.</returns>
    public MatchCollection Matches(string input, int startAt) => new(this.Match(input, startAt));

    /// <summary>The number of matches in a text.</summary>
    /// <param name="input">The text.</param>
    /// <returns>The number.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public int Count(string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return this.Count(input.AsSpan());
    }

    /// <summary>The number of matches in a UTF-16 text, allocating nothing.</summary>
    /// <param name="input">The text.</param>
    /// <returns>The number.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public int Count(ReadOnlySpan<char> input)
    {
        int n = 0;
        foreach (ValueMatch m in this.EnumerateMatches(input))
        {
            n++;
        }

        return n;
    }

    /// <summary>The number of matches in a UTF-8 text, allocating nothing.</summary>
    /// <param name="utf8">The text.</param>
    /// <returns>The number.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public int Count(ReadOnlySpan<byte> utf8)
    {
        int n = 0;
        foreach (ValueMatch m in this.EnumerateMatches(utf8))
        {
            n++;
        }

        return n;
    }

    /// <summary>Enumerates the matches in a UTF-16 text, allocating nothing.</summary>
    /// <param name="input">The text.</param>
    /// <returns>The enumerator.</returns>
    public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input) => new(this, input, 0);

    /// <summary>Enumerates the matches in a UTF-16 text at or after a position, allocating nothing.</summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <returns>The enumerator.</returns>
    public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, int startAt)
    {
        ValidateStart(input.Length, startAt);
        return new(this, input, this.Normalize(input, startAt));
    }

    /// <summary>Enumerates the matches in a UTF-8 text, allocating nothing.</summary>
    /// <param name="utf8">The text.</param>
    /// <returns>The enumerator.</returns>
    public Utf8ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<byte> utf8) => new(this, utf8, 0);

    /// <summary>Enumerates the matches in a UTF-8 text at or after a position, allocating nothing.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="startAt">The position, in bytes, at the start of a character.</param>
    /// <returns>The enumerator.</returns>
    public Utf8ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<byte> utf8, int startAt)
    {
        ValidateStart(utf8.Length, startAt);
        return new(this, utf8, this.Normalize(utf8, startAt));
    }

    /// <summary>
    /// Finds the first match in a UTF-16 text at or after a position and reports what each group captured, allocating
    /// nothing.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <param name="captures">Receives the match as element 0 and each group in order; its length must be at least <see cref="GroupCount"/> + 1.</param>
    /// <param name="match">The match.</param>
    /// <returns><see langword="true"/> when there is a match.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool TryMatch(ReadOnlySpan<char> input, int startAt, Span<ValueCapture> captures, out ValueMatch match)
    {
        ValidateStart(input.Length, startAt);
        if (captures.Length < this.GroupCount + 1)
        {
            throw new ArgumentException("The captures span is too short.", nameof(captures));
        }

        Span<int> slots = stackalloc int[2 * (this.GroupCount + 1)];
        bool ok = Matcher.Match<char, Utf16Encoding>(this.CaptureProgram(), input, this.Normalize(input, startAt), this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        return Fill(ok, slots, captures, out match);
    }

    /// <summary>
    /// Finds the first match in a UTF-8 text at or after a position and reports what each group captured, allocating
    /// nothing.
    /// </summary>
    /// <param name="utf8">The text.</param>
    /// <param name="startAt">The position, in bytes, at the start of a character.</param>
    /// <param name="captures">Receives the match as element 0 and each group in order; its length must be at least <see cref="GroupCount"/> + 1.</param>
    /// <param name="match">The match.</param>
    /// <returns><see langword="true"/> when there is a match.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool TryMatch(ReadOnlySpan<byte> utf8, int startAt, Span<ValueCapture> captures, out ValueMatch match)
    {
        ValidateStart(utf8.Length, startAt);
        if (captures.Length < this.GroupCount + 1)
        {
            throw new ArgumentException("The captures span is too short.", nameof(captures));
        }

        Span<int> slots = stackalloc int[2 * (this.GroupCount + 1)];
        bool ok = Matcher.Match<byte, Utf8Encoding>(this.CaptureProgram(), utf8, this.Normalize(utf8, startAt), this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        return Fill(ok, slots, captures, out match);
    }

    /// <summary>
    /// Replaces the matches in a text: every match with <see cref="RegexFlags.Global"/>, the first otherwise, as
    /// <c>String.prototype.replace</c> does. The replacement is expanded as <see cref="Match.Result"/> expands it.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="replacement">The replacement.</param>
    /// <returns>The text with the replacements made.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public string Replace(string input, string replacement)
    {
        if (replacement is null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        return this.ReplaceCore(input, replacement, null);
    }

    /// <summary>
    /// Replaces the matches in a text (every match with <see cref="RegexFlags.Global"/>, the first otherwise) with
    /// what an evaluator gives for each, used as it is.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="evaluator">The evaluator.</param>
    /// <returns>The text with the replacements made.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public string Replace(string input, MatchEvaluator evaluator)
    {
        if (evaluator is null)
        {
            throw new ArgumentNullException(nameof(evaluator));
        }

        return this.ReplaceCore(input, null, evaluator);
    }

    /// <summary>Splits a text at the matches, as <c>String.prototype.split</c> does: the captures of each match are put between the pieces (null for a group that did not take part), and an empty match at the start or the end of the text splits nothing.</summary>
    /// <param name="input">The text.</param>
    /// <returns>The pieces.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public string?[] Split(string input) => this.Split(input, int.MaxValue);

    /// <summary>Splits a text at the matches, up to a number of pieces (the <c>limit</c> of <c>String.prototype.split</c>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="count">The most pieces to return, captures included. 0 gives none.</param>
    /// <returns>The pieces.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public string?[] Split(string input, int count)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        List<string?> pieces = [];
        if (count == 0)
        {
            return [];
        }

        int size = input.Length;
        Span<int> slots = stackalloc int[2 * (this.GroupCount + 1)];
        Program program = this.CaptureProgram();
        if (size == 0)
        {
            bool matches = Matcher.Match<char, Utf16Encoding>(program, input.AsSpan(), 0, sticky: true, this.Limit, slots, out MatchStop stop0);
            if (stop0 != MatchStop.None)
            {
                throw this.Abandoned(stop0);
            }

            return matches ? [] : [input];
        }

        // The specification probes every position from q with a sticky match; the first position the probe
        // succeeds at is the start of the leftmost match at or after q, which one search finds.
        int p = 0;
        int q = 0;
        while (q < size)
        {
            bool ok = Matcher.Match<char, Utf16Encoding>(program, input.AsSpan(), q, sticky: false, this.Limit, slots, out MatchStop stop);
            if (stop != MatchStop.None)
            {
                throw this.Abandoned(stop);
            }

            if (!ok || slots[0] >= size)
            {
                break;
            }

            q = slots[0];
            int e = Math.Min(slots[1], size);
            if (e == p)
            {
                q = this.AdvanceAfterEmptyMatch(input.AsSpan(), q);
                continue;
            }

            pieces.Add(input.Substring(p, q - p));
            if (pieces.Count == count)
            {
                return [.. pieces];
            }

            for (int g = 1; g <= this.GroupCount; g++)
            {
                int from = slots[2 * g];
                pieces.Add(from < 0 ? null : input.Substring(from, slots[(2 * g) + 1] - from));
                if (pieces.Count == count)
                {
                    return [.. pieces];
                }
            }

            p = e;
            q = p;
        }

        pieces.Add(input.Substring(p));
        return [.. pieces];
    }

    /// <summary>The names of the named groups, in the order the pattern first uses them.</summary>
    /// <returns>The names.</returns>
    public string[] GetGroupNames()
    {
        string[] names = new string[this.GroupNames.Count];
        for (int k = 0; k < names.Length; k++)
        {
            names[k] = this.GroupNames[k].Key;
        }

        return names;
    }

    /// <summary>The number of the first group of a name, or -1 when the pattern has no group of that name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The number.</returns>
    public int GroupNumberFromName(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        foreach (KeyValuePair<string, int[]> named in this.GroupNames)
        {
            if (named.Key == name)
            {
                return named.Value[0];
            }
        }

        return -1;
    }

    /// <summary>The name of a group, or its number as text when it has none.</summary>
    /// <param name="number">The number, from 0 to <see cref="GroupCount"/>.</param>
    /// <returns>The name.</returns>
    public string GroupNameFromNumber(int number)
    {
        foreach (KeyValuePair<string, int[]> named in this.GroupNames)
        {
            if (Array.IndexOf(named.Value, number) >= 0)
            {
                return named.Key;
            }
        }

        return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The first match at or after a position, as a <see cref="RegularExpressions.Match"/>.</summary>
    internal Match MatchAt(string input, int startAt)
    {
        int[] slots = new int[2 * (this.GroupCount + 1)];
        bool ok = Matcher.Match<char, Utf16Encoding>(this.CaptureProgram(), input.AsSpan(), startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        if (!ok)
        {
            return RegularExpressions.Match.Empty;
        }

        int next = slots[1] > slots[0] ? slots[1] : this.AdvanceAfterEmptyMatch(input.AsSpan(), slots[1]);
        return new Match(this, input, slots, this.GroupNames, next);
    }

    /// <summary>Where the first match at or after a position is, with no captures.</summary>
    internal bool TryMatchBounds(ReadOnlySpan<char> input, int startAt, out int from, out int to) => this.TryMatchBounds(input, startAt, this.Sticky, out from, out to);

    /// <summary>Where the first match at or after a position is, with no captures, over UTF-8.</summary>
    internal bool TryMatchBounds(ReadOnlySpan<byte> input, int startAt, out int from, out int to) => this.TryMatchBounds(input, startAt, this.Sticky, out from, out to);

    /// <summary>Where the first match at or after a position is, with no captures, sticky or not as asked.</summary>
    /// <summary>Whether the pattern matches at or after a position, with a state the caller holds for a run of matches (see <see cref="MatchState.Current"/>).</summary>
    internal bool TryMatchBounds(MatchState st, ref ScanCursor cursor, ReadOnlySpan<char> input, int startAt, out int from, out int to)
    {
        CandidateScan scan = default;
        return this.TryMatchBounds(st, ref cursor, ref scan, input, startAt, out from, out to);
    }

    /// <summary>The same, with the generated code's search for starts kept between the matches of an enumeration: a match after one that search found resumes it.</summary>
    internal bool TryMatchBounds(MatchState st, ref ScanCursor cursor, ref CandidateScan scan, ReadOnlySpan<char> input, int startAt, out int from, out int to)
    {
        if (scan.Kind != 0 && !this.Sticky)
        {
            Span<int> resumed = stackalloc int[2 * (this.program.Groups + 1)];
            bool again = Matcher.ResumeWith<char, Utf16Encoding>(st, ref cursor, ref scan, this.program, input, startAt, this.Limit, resumed, out MatchStop why);
            if (why != MatchStop.None)
            {
                throw this.Abandoned(why);
            }

            from = resumed[0];
            to = resumed[1];
            return again;
        }

        if (this.program.Shape != SimpleShape.None && this.program.Runner is not { OwnsSearch: true })
        {
            if (!this.program.UsesCursor)
            {
                return Matcher.Simple<char, Utf16Encoding>(this.program, st, input, startAt, this.Sticky, out from, out to);
            }

            st.LoadCursor(in cursor);
            bool found = Matcher.Simple<char, Utf16Encoding>(this.program, st, input, startAt, this.Sticky, out from, out to);
            st.SaveCursor(ref cursor);
            return found;
        }

        Span<int> slots = stackalloc int[2 * (this.program.Groups + 1)];
        bool ok = Matcher.MatchWith<char, Utf16Encoding>(st, ref cursor, ref scan, this.program, input, startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        from = slots[0];
        to = slots[1];
        return ok;
    }

    /// <summary>Gets a value indicating whether an enumeration's later matches can resume the generated code's loop directly (see <see cref="Matcher.ResumeEmitted{TUnit, TEncoding}"/>): not sticky, no group tracked, no cursor, and the generated code finding the starts.</summary>
    internal bool ResumesEmitted => !this.Sticky && this.program.Groups == 0 && !this.program.UsesCursor && this.program.Runner is { OwnsSearch: true };

    /// <summary>The next match of an enumeration in UTF-16, resuming the generated code's loop (see <see cref="ResumesEmitted"/>).</summary>
    internal bool TryResumeEmitted(MatchState st, ref CandidateScan scan, ReadOnlySpan<char> input, int startAt, out int from, out int to)
    {
        bool ok = Matcher.ResumeEmitted<char, Utf16Encoding>(st, ref scan, this.program, input, startAt, this.Limit, out from, out to);
        if (st.Stop != MatchStop.None)
        {
            throw this.Abandoned(st.Stop);
        }

        return ok;
    }

    /// <summary>The next match of an enumeration in UTF-8, resuming the generated code's loop (see <see cref="ResumesEmitted"/>).</summary>
    internal bool TryResumeEmitted(MatchState st, ref CandidateScan scan, ReadOnlySpan<byte> input, int startAt, out int from, out int to)
    {
        bool ok = Matcher.ResumeEmitted<byte, Utf8Encoding>(st, ref scan, this.program, input, startAt, this.Limit, out from, out to);
        if (st.Stop != MatchStop.None)
        {
            throw this.Abandoned(st.Stop);
        }

        return ok;
    }

    /// <summary>Whether the pattern matches at or after a position in UTF-8, with a state the caller holds for a run of matches.</summary>
    internal bool TryMatchBounds(MatchState st, ref ScanCursor cursor, ReadOnlySpan<byte> input, int startAt, out int from, out int to)
    {
        CandidateScan scan = default;
        return this.TryMatchBounds(st, ref cursor, ref scan, input, startAt, out from, out to);
    }

    /// <summary>The same, with the generated code's search for starts kept between the matches of an enumeration: a match after one that search found resumes it.</summary>
    internal bool TryMatchBounds(MatchState st, ref ScanCursor cursor, ref CandidateScan scan, ReadOnlySpan<byte> input, int startAt, out int from, out int to)
    {
        if (scan.Kind != 0 && !this.Sticky)
        {
            Span<int> resumed = stackalloc int[2 * (this.program.Groups + 1)];
            bool again = Matcher.ResumeWith<byte, Utf8Encoding>(st, ref cursor, ref scan, this.program, input, startAt, this.Limit, resumed, out MatchStop why);
            if (why != MatchStop.None)
            {
                throw this.Abandoned(why);
            }

            from = resumed[0];
            to = resumed[1];
            return again;
        }

        if (this.program.Shape != SimpleShape.None && this.program.Runner is not { OwnsSearch: true })
        {
            if (!this.program.UsesCursor)
            {
                return Matcher.Simple<byte, Utf8Encoding>(this.program, st, input, startAt, this.Sticky, out from, out to);
            }

            st.LoadCursor(in cursor);
            bool found = Matcher.Simple<byte, Utf8Encoding>(this.program, st, input, startAt, this.Sticky, out from, out to);
            st.SaveCursor(ref cursor);
            return found;
        }

        Span<int> slots = stackalloc int[2 * (this.program.Groups + 1)];
        bool ok = Matcher.MatchWith<byte, Utf8Encoding>(st, ref cursor, ref scan, this.program, input, startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        from = slots[0];
        to = slots[1];
        return ok;
    }

    internal bool TryMatchBounds(ReadOnlySpan<char> input, int startAt, bool sticky, out int from, out int to)
    {
        if (this.program.Shape != SimpleShape.None && this.program.Runner is not { OwnsSearch: true })
        {
            return Matcher.Simple<char, Utf16Encoding>(this.program, null, input, startAt, sticky, out from, out to);
        }

        Span<int> slots = stackalloc int[2 * (this.program.Groups + 1)];
        bool ok = Matcher.Match<char, Utf16Encoding>(this.program, input, startAt, sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        from = slots[0];
        to = slots[1];
        return ok;
    }

    /// <summary>Where the first match at or after a position is, with no captures, over UTF-8, sticky or not as asked.</summary>
    internal bool TryMatchBounds(ReadOnlySpan<byte> input, int startAt, bool sticky, out int from, out int to)
    {
        if (this.program.Shape != SimpleShape.None && this.program.Runner is not { OwnsSearch: true })
        {
            return Matcher.Simple<byte, Utf8Encoding>(this.program, null, input, startAt, sticky, out from, out to);
        }

        Span<int> slots = stackalloc int[2 * (this.program.Groups + 1)];
        bool ok = Matcher.Match<byte, Utf8Encoding>(this.program, input, startAt, sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        from = slots[0];
        to = slots[1];
        return ok;
    }

    /// <summary>The position a global search goes on from after an empty match at a position: one code point on with the u flag, one code unit otherwise (AdvanceStringIndex).</summary>
    internal int AdvanceAfterEmptyMatch(ReadOnlySpan<char> input, int pos)
    {
        if (pos >= input.Length)
        {
            return pos + 1;
        }

        if (this.unicode)
        {
            default(Utf16Encoding).Decode(input, pos, out int w);
            return pos + w;
        }

        return pos + 1;
    }

    /// <summary>The position a global search goes on from after an empty match at a position, over UTF-8.</summary>
    internal int AdvanceAfterEmptyMatch(ReadOnlySpan<byte> input, int pos)
    {
        if (pos >= input.Length)
        {
            return pos + 1;
        }

        if (this.unicode)
        {
            default(Utf8Encoding).Decode(input, pos, out int w);
            return pos + w;
        }

        return default(Utf8Encoding).AdvanceUnit(input, pos);
    }

    private bool Sticky => (this.Flags & RegexFlags.Sticky) != 0;

    /// <summary>
    /// The position a search from <paramref name="startAt"/> begins at. With the u flag ECMA-262 reads the text as
    /// code points, and a <c>lastIndex</c> between the halves of a surrogate pair means the character that holds it,
    /// so the search begins at the pair (RegExpBuiltinExec). Without it every code unit is a position.
    /// </summary>
    private int Normalize(ReadOnlySpan<char> input, int startAt)
    {
        if (this.unicode && startAt > 0 && startAt < input.Length && char.IsLowSurrogate(input[startAt]) && char.IsHighSurrogate(input[startAt - 1]))
        {
            return startAt - 1;
        }

        return startAt;
    }

    /// <summary>
    /// The position a search from <paramref name="startAt"/> begins at over UTF-8: a position inside a character is
    /// moved back to the character's first byte, except the position between the halves of a four-byte character
    /// (its third byte), which is a code unit position without the u flag.
    /// </summary>
    private int Normalize(ReadOnlySpan<byte> input, int startAt)
    {
        if (startAt <= 0 || startAt >= input.Length || (input[startAt] & 0xC0) != 0x80)
        {
            return startAt;
        }

        int lead = startAt;
        while (lead > 0 && (input[lead] & 0xC0) == 0x80)
        {
            lead--;
        }

        if (!this.unicode && input[lead] >= 0xF0 && startAt == lead + 2)
        {
            return startAt;
        }

        return lead;
    }

    private static bool Fill(bool ok, Span<int> slots, Span<ValueCapture> captures, out ValueMatch match)
    {
        if (!ok)
        {
            match = default;
            return false;
        }

        match = new ValueMatch(slots[0], slots[1] - slots[0]);
        for (int g = 0; g < slots.Length / 2; g++)
        {
            int from = slots[2 * g];
            captures[g] = new ValueCapture(from, from < 0 ? 0 : slots[(2 * g) + 1] - from);
        }

        return true;
    }

    private static bool IsWhiteSpaceOrLineTerminator(char c)
    {
        return c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r' || c == ' ' || c == '\u00a0' || c == '\u1680'
            || (c >= '\u2000' && c <= '\u200a') || c == '\u2028' || c == '\u2029' || c == '\u202f' || c == '\u205f'
            || c == '\u3000' || c == '\ufeff';
    }

    private static bool TryHex(string s, int at, int digits, out int value)
    {
        value = 0;
        for (int k = 0; k < digits; k++)
        {
            char c = s[at + k];
            int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            if (d < 0)
            {
                return false;
            }

            value = (value * 16) + d;
        }

        return true;
    }

    private string ReplaceCore(string input, string? replacement, MatchEvaluator? evaluator)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (replacement is not null)
        {
            // The result is built in pooled memory and copied out once: the string is the only allocation.
            using PooledBufferWriter<char> writer = new(input.Length + 16);
            WriterOutput<char> output = new(writer);
            if (!this.ReplaceInto<char, Utf16Encoding, WriterOutput<char>>(input.AsSpan(), replacement.AsSpan(), ref output))
            {
                return input;
            }

            return writer.WrittenLength == input.Length && writer.WrittenSpan.SequenceEqual(input.AsSpan()) ? input : writer.ToString();
        }

        Match m = this.MatchAt(input, 0);
        if (!m.Success)
        {
            return input;
        }

        StringBuilder sb = new(input.Length);
        int last = 0;
        bool global = (this.Flags & RegexFlags.Global) != 0;
        while (m.Success)
        {
            sb.Append(input, last, m.Index - last);
            if (replacement is not null)
            {
                m.AppendResult(sb, replacement);
            }
            else
            {
                sb.Append(evaluator!(m));
            }

            last = m.Index + m.Length;
            if (!global)
            {
                break;
            }

            m = m.NextMatch();
        }

        sb.Append(input, last, input.Length - last);
        return sb.ToString();
    }
}
