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
    private int next;
    private bool done;

    internal ValueMatchEnumerator(Regex regex, ReadOnlySpan<char> input, int startAt)
    {
        this.regex = regex;
        this.input = input;
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

        if (!this.regex.TryMatchBounds(this.input, this.next, out int from, out int to))
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
    private int next;
    private bool done;

    internal Utf8ValueMatchEnumerator(Regex regex, ReadOnlySpan<byte> input, int startAt)
    {
        this.regex = regex;
        this.input = input;
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

        if (!this.regex.TryMatchBounds(this.input, this.next, out int from, out int to))
        {
            this.done = true;
            return false;
        }

        this.Current = new ValueMatch(from, to - from);
        this.next = to > from ? to : this.regex.AdvanceAfterEmptyMatch(this.input, to);
        return true;
    }
}
