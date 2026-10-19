// <copyright file="ValueMatchEnumerator.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Enumerates the matches of a pattern in a UTF-16 text as a global search, allocating nothing.</summary>
public ref struct ValueMatchEnumerator
{
    private readonly Regex regex;
    private readonly ReadOnlySpan<char> input;
    private readonly Span<ValueCapture> captures;
    private readonly bool resumes;
    private MatchState? state;
    private ScanCursor cursor;
    private CandidateScan scan;
    private int next;
    private bool done;

    internal ValueMatchEnumerator(Regex regex, ReadOnlySpan<char> input, int startAt)
        : this(regex, input, startAt, default)
    {
    }

    internal ValueMatchEnumerator(Regex regex, ReadOnlySpan<char> input, int startAt, Span<ValueCapture> captures)
    {
        this.regex = regex;
        this.input = input;
        this.captures = captures;
        this.resumes = captures.Length == 0 && regex.ResumesEmitted;
        this.next = startAt;
        this.Current = default;
    }

    /// <summary>Gets the current match.</summary>
    public ValueMatch Current { get; private set; }

    /// <summary>This enumerator, for <c>foreach</c>.</summary>
    /// <returns>This.</returns>
    public readonly ValueMatchEnumerator GetEnumerator() => this;

    /// <summary>Finds the next match.</summary>
    /// <returns><see langword="true"/> when there is one.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool MoveNext()
    {
        if (this.done || this.next > this.input.Length)
        {
            this.done = true;
            return false;
        }

        int from;
        int to;
        if (this.captures.Length != 0)
        {
            this.state ??= MatchState.Current();
            if (!this.regex.TryMatchCaptures(this.state, ref this.cursor, this.input, this.next, this.captures, out ValueMatch match))
            {
                this.done = true;
                return false;
            }

            from = match.Index;
            to = from + match.Length;
        }
        else if (this.resumes && this.scan.Kind == CandidateScan.Emitted)
        {
            if (!this.regex.TryResumeEmitted(this.state!, ref this.scan, this.input, this.next, out from, out to))
            {
                this.done = true;
                return false;
            }
        }
        else if (!this.regex.TryMatchBounds(this.state ??= MatchState.Current(), ref this.cursor, ref this.scan, this.input, this.next, out from, out to))
        {
            this.done = true;
            return false;
        }

        this.Current = new ValueMatch(from, to - from);
        this.next = to > from ? to : this.regex.AdvanceAfterEmptyMatch(this.input, to);
        return true;
    }
}

/// <summary>Enumerates the matches of a pattern in a UTF-8 text as a global search, allocating nothing.</summary>
public ref struct Utf8ValueMatchEnumerator
{
    private readonly Regex regex;
    private readonly ReadOnlySpan<byte> input;
    private readonly Span<ValueCapture> captures;
    private readonly bool resumes;
    private MatchState? state;
    private ScanCursor cursor;
    private CandidateScan scan;
    private int next;
    private bool done;

    internal Utf8ValueMatchEnumerator(Regex regex, ReadOnlySpan<byte> input, int startAt)
        : this(regex, input, startAt, default)
    {
    }

    internal Utf8ValueMatchEnumerator(Regex regex, ReadOnlySpan<byte> input, int startAt, Span<ValueCapture> captures)
    {
        this.regex = regex;
        this.input = input;
        this.captures = captures;
        this.resumes = captures.Length == 0 && regex.ResumesEmitted;
        this.next = startAt;
        this.Current = default;
    }

    /// <summary>Gets the current match.</summary>
    public ValueMatch Current { get; private set; }

    /// <summary>This enumerator, for <c>foreach</c>.</summary>
    /// <returns>This.</returns>
    public readonly Utf8ValueMatchEnumerator GetEnumerator() => this;

    /// <summary>Finds the next match.</summary>
    /// <returns><see langword="true"/> when there is one.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool MoveNext()
    {
        if (this.done || this.next > this.input.Length)
        {
            this.done = true;
            return false;
        }

        int from;
        int to;
        if (this.captures.Length != 0)
        {
            this.state ??= MatchState.Current();
            if (!this.regex.TryMatchCaptures(this.state, ref this.cursor, this.input, this.next, this.captures, out ValueMatch match))
            {
                this.done = true;
                return false;
            }

            from = match.Index;
            to = from + match.Length;
        }
        else if (this.resumes && this.scan.Kind == CandidateScan.Emitted)
        {
            if (!this.regex.TryResumeEmitted(this.state!, ref this.scan, this.input, this.next, out from, out to))
            {
                this.done = true;
                return false;
            }
        }
        else if (!this.regex.TryMatchBounds(this.state ??= MatchState.Current(), ref this.cursor, ref this.scan, this.input, this.next, out from, out to))
        {
            this.done = true;
            return false;
        }

        this.Current = new ValueMatch(from, to - from);
        this.next = to > from ? to : this.regex.AdvanceAfterEmptyMatch(this.input, to);
        return true;
    }
}
