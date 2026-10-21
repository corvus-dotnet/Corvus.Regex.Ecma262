// <copyright file="Regex.Spans.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Gives the replacement for one match in a UTF-16 text by writing it.</summary>
/// <param name="input">The text.</param>
/// <param name="match">The match.</param>
/// <param name="captures">What each group captured: the match as element 0, then each group in order.</param>
/// <param name="destination">Where to write the replacement, which is used as it is.</param>
public delegate void SpanMatchEvaluator(ReadOnlySpan<char> input, ValueMatch match, ReadOnlySpan<ValueCapture> captures, IBufferWriter<char> destination);

/// <summary>Gives the replacement for one match in a UTF-8 text by writing it.</summary>
/// <param name="utf8">The text.</param>
/// <param name="match">The match.</param>
/// <param name="captures">What each group captured: the match as element 0, then each group in order.</param>
/// <param name="destination">Where to write the replacement, which is used as it is.</param>
public delegate void Utf8MatchEvaluator(ReadOnlySpan<byte> utf8, ValueMatch match, ReadOnlySpan<ValueCapture> captures, IBufferWriter<byte> destination);

/// <summary>Enumerates the pieces a UTF-16 text splits into at the matches of a pattern, as positions, allocating nothing (see <see cref="Regex.EnumerateSplits(ReadOnlySpan{char})"/>).</summary>
public ref struct ValueSplitEnumerator
{
    private readonly Regex regex;
    private readonly ReadOnlySpan<char> input;
    private int left;
    private int p;
    private bool done;

    internal ValueSplitEnumerator(Regex regex, ReadOnlySpan<char> input, int count)
    {
        this.regex = regex;
        this.input = input;
        this.left = count;
        this.Current = default;
    }

    /// <summary>Gets the current piece.</summary>
    public ValueMatch Current { get; private set; }

    /// <summary>This enumerator, for <c>foreach</c>.</summary>
    /// <returns>This.</returns>
    public readonly ValueSplitEnumerator GetEnumerator() => this;

    /// <summary>Finds the next piece.</summary>
    /// <returns><see langword="true"/> when there is one.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public bool MoveNext()
    {
        if (this.done || this.left == 0)
        {
            this.done = true;
            return false;
        }

        if (this.input.Length == 0)
        {
            // An empty text is one piece, itself, unless the pattern matches it.
            this.done = true;
            bool matches = this.regex.TryMatchBounds(this.input, 0, sticky: true, out int f, out _) && f == 0;
            this.Current = new ValueMatch(0, 0);
            return !matches;
        }

        if (this.p > this.input.Length)
        {
            this.done = true;
            return false;
        }

        bool more = this.regex.NextSplit(this.input, this.p, this.p, out int pieceEnd, out int matchEnd);
        this.Current = new ValueMatch(this.p, pieceEnd - this.p);
        this.left--;
        this.p = more ? matchEnd : this.input.Length + 1;
        if (!more)
        {
            this.done = true;
        }

        return true;
    }
}

/// <summary>Enumerates the pieces a UTF-8 text splits into at the matches of a pattern, as positions, allocating nothing (see <see cref="Regex.EnumerateSplits(ReadOnlySpan{byte})"/>).</summary>
public ref struct Utf8ValueSplitEnumerator
{
    private readonly Regex regex;
    private readonly ReadOnlySpan<byte> input;
    private int left;
    private int p;
    private bool done;

    internal Utf8ValueSplitEnumerator(Regex regex, ReadOnlySpan<byte> input, int count)
    {
        this.regex = regex;
        this.input = input;
        this.left = count;
        this.Current = default;
    }

    /// <summary>Gets the current piece.</summary>
    public ValueMatch Current { get; private set; }

    /// <summary>This enumerator, for <c>foreach</c>.</summary>
    /// <returns>This.</returns>
    public readonly Utf8ValueSplitEnumerator GetEnumerator() => this;

    /// <summary>Finds the next piece.</summary>
    /// <returns><see langword="true"/> when there is one.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public bool MoveNext()
    {
        if (this.done || this.left == 0)
        {
            this.done = true;
            return false;
        }

        if (this.input.Length == 0)
        {
            this.done = true;
            bool matches = this.regex.TryMatchBounds(this.input, 0, sticky: true, out int f, out _) && f == 0;
            this.Current = new ValueMatch(0, 0);
            return !matches;
        }

        if (this.p > this.input.Length)
        {
            this.done = true;
            return false;
        }

        bool more = this.regex.NextSplit(this.input, this.p, this.p, out int pieceEnd, out int matchEnd);
        this.Current = new ValueMatch(this.p, pieceEnd - this.p);
        this.left--;
        this.p = more ? matchEnd : this.input.Length + 1;
        if (!more)
        {
            this.done = true;
        }

        return true;
    }
}

/// <summary>
/// The members that match, replace and split without allocating: the matches with what each group captured
/// into a span the caller gives, the replacements into a span or an <see cref="IBufferWriter{T}"/>, the pieces of
/// a split as positions.
/// </summary>
public sealed partial class Regex
{
    private byte[][]? groupNamesUtf8;

    /// <summary>Where the replacement loop writes.</summary>
    /// <typeparam name="T">The unit of the text.</typeparam>
    private interface IOutput<T>
    {
        /// <summary>Appends text.</summary>
        /// <param name="text">The text.</param>
        /// <returns><see langword="false"/> when it does not fit.</returns>
        bool Append(ReadOnlySpan<T> text);
    }

    /// <summary>
    /// Enumerates the matches in a UTF-16 text, allocating nothing, and reports what each group captured: after
    /// each <c>MoveNext</c> the span holds the match as element 0 and each group in order.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="captures">Receives the captures of each match; its length must be at least <see cref="GroupCount"/> + 1.</param>
    /// <returns>The enumerator.</returns>
    public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, Span<ValueCapture> captures)
    {
        this.ValidateCaptures(captures.Length);
        return new(this, input, 0, captures);
    }

    /// <summary>Enumerates the matches in a UTF-16 text at or after a position, allocating nothing, and reports what each group captured.</summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <param name="captures">Receives the captures of each match; its length must be at least <see cref="GroupCount"/> + 1.</param>
    /// <returns>The enumerator.</returns>
    public ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<char> input, int startAt, Span<ValueCapture> captures)
    {
        ValidateStart(input.Length, startAt);
        this.ValidateCaptures(captures.Length);
        return new(this, input, this.Normalize(input, startAt), captures);
    }

    /// <summary>Enumerates the matches in a UTF-8 text, allocating nothing, and reports what each group captured.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="captures">Receives the captures of each match; its length must be at least <see cref="GroupCount"/> + 1.</param>
    /// <returns>The enumerator.</returns>
    public Utf8ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<byte> utf8, Span<ValueCapture> captures)
    {
        this.ValidateCaptures(captures.Length);
        return new(this, utf8, 0, captures);
    }

    /// <summary>Enumerates the matches in a UTF-8 text at or after a position, allocating nothing, and reports what each group captured.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="startAt">The position, in bytes, at the start of a character.</param>
    /// <param name="captures">Receives the captures of each match; its length must be at least <see cref="GroupCount"/> + 1.</param>
    /// <returns>The enumerator.</returns>
    public Utf8ValueMatchEnumerator EnumerateMatches(ReadOnlySpan<byte> utf8, int startAt, Span<ValueCapture> captures)
    {
        ValidateStart(utf8.Length, startAt);
        this.ValidateCaptures(captures.Length);
        return new(this, utf8, this.Normalize(utf8, startAt), captures);
    }

    /// <summary>
    /// Replaces the matches in a UTF-16 text (every match with <see cref="RegexFlags.Global"/>, the first
    /// otherwise) and writes the result to a span, allocating nothing. The replacement is expanded as
    /// <see cref="Match.Result"/> expands it.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="replacement">The replacement.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <param name="written">The length of the result, when it fits.</param>
    /// <returns><see langword="false"/> when the result does not fit, in which case what was written is not meaningful.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public bool TryReplace(ReadOnlySpan<char> input, ReadOnlySpan<char> replacement, Span<char> destination, out int written)
    {
#if NET
        SpanOutput<char> output = new(destination);
        bool ok = this.ReplaceInto<char, Utf16Encoding, SpanOutput<char>>(input, replacement, ref output);
        written = output.Written;
        return ok;
#else
        // No ref struct can be the output here (ref struct interfaces need the modern runtime), so the thread's
        // pooled writer takes the result and the destination gets a copy; nothing is allocated once it is warm.
        PooledBufferWriter<char> writer = PooledBufferWriter<char>.Rent(input.Length + 16);
        try
        {
            WriterOutput<char> output = new(writer);
            this.ReplaceInto<char, Utf16Encoding, WriterOutput<char>>(input, replacement, ref output);
            written = writer.WrittenLength;
            return writer.WrittenSpan.TryCopyTo(destination);
        }
        finally
        {
            writer.Release();
        }
#endif
    }

    /// <summary>Replaces the matches in a UTF-8 text and writes the result to a span, allocating nothing (see <see cref="TryReplace(ReadOnlySpan{char}, ReadOnlySpan{char}, Span{char}, out int)"/>).</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="replacement">The replacement, as UTF-8.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <param name="written">The length of the result, when it fits.</param>
    /// <returns><see langword="false"/> when the result does not fit.</returns>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public bool TryReplace(ReadOnlySpan<byte> utf8, ReadOnlySpan<byte> replacement, Span<byte> destination, out int written)
    {
#if NET
        SpanOutput<byte> output = new(destination);
        bool ok = this.ReplaceInto<byte, Utf8Encoding, SpanOutput<byte>>(utf8, replacement, ref output);
        written = output.Written;
        return ok;
#else
        PooledBufferWriter<byte> writer = PooledBufferWriter<byte>.Rent(utf8.Length + 16);
        try
        {
            WriterOutput<byte> output = new(writer);
            this.ReplaceInto<byte, Utf8Encoding, WriterOutput<byte>>(utf8, replacement, ref output);
            written = writer.WrittenLength;
            return writer.WrittenSpan.TryCopyTo(destination);
        }
        finally
        {
            writer.Release();
        }
#endif
    }

    /// <summary>Replaces the matches in a UTF-16 text and writes the result to a buffer writer, allocating nothing itself (see <see cref="TryReplace(ReadOnlySpan{char}, ReadOnlySpan{char}, Span{char}, out int)"/>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="replacement">The replacement.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public void Replace(ReadOnlySpan<char> input, ReadOnlySpan<char> replacement, IBufferWriter<char> destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        WriterOutput<char> output = new(destination);
        this.ReplaceInto<char, Utf16Encoding, WriterOutput<char>>(input, replacement, ref output);
    }

    /// <summary>Replaces the matches in a UTF-8 text and writes the result to a buffer writer, allocating nothing itself.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="replacement">The replacement, as UTF-8.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public void Replace(ReadOnlySpan<byte> utf8, ReadOnlySpan<byte> replacement, IBufferWriter<byte> destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        WriterOutput<byte> output = new(destination);
        this.ReplaceInto<byte, Utf8Encoding, WriterOutput<byte>>(utf8, replacement, ref output);
    }

    /// <summary>
    /// Replaces the matches in a UTF-16 text (every match with <see cref="RegexFlags.Global"/>, the first
    /// otherwise) with what an evaluator writes for each, and writes the result to a buffer writer, allocating
    /// nothing itself.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="evaluator">The evaluator.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public void Replace(ReadOnlySpan<char> input, SpanMatchEvaluator evaluator, IBufferWriter<char> destination)
    {
        if (evaluator is null)
        {
            throw new ArgumentNullException(nameof(evaluator));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        int capturesLength = this.GroupCount + 1;
        using StackBuffer.Pooled<ValueCapture> capturesPool = new(capturesLength);
        Span<ValueCapture> captures = capturesLength <= StackBuffer.MaxLength ? stackalloc ValueCapture[capturesLength] : capturesPool.Span;
        Program program = this.CaptureProgram();
        bool global = (this.Flags & RegexFlags.Global) != 0;
        int last = 0;
        int next = 0;
        while (next <= input.Length)
        {
            bool ok = Matcher.Match<char, Utf16Encoding>(program, input, next, this.Sticky, this.Limit, slots, out MatchStop stop);
            if (stop != MatchStop.None)
            {
                throw this.Abandoned(stop);
            }

            if (!ok)
            {
                break;
            }

            Fill(true, slots, captures, out ValueMatch match);
            Write(destination, input.Slice(last, match.Index - last));
            evaluator(input, match, captures, destination);
            last = match.Index + match.Length;
            if (!global)
            {
                break;
            }

            next = match.Length > 0 ? last : this.AdvanceAfterEmptyMatch(input, last);
        }

        Write(destination, input.Slice(last));
    }

    /// <summary>Replaces the matches in a UTF-8 text with what an evaluator writes for each, and writes the result to a buffer writer, allocating nothing itself.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="evaluator">The evaluator.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <exception cref="RegexMatchTimeoutException">A match reached its limit.</exception>
    public void Replace(ReadOnlySpan<byte> utf8, Utf8MatchEvaluator evaluator, IBufferWriter<byte> destination)
    {
        if (evaluator is null)
        {
            throw new ArgumentNullException(nameof(evaluator));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        int capturesLength = this.GroupCount + 1;
        using StackBuffer.Pooled<ValueCapture> capturesPool = new(capturesLength);
        Span<ValueCapture> captures = capturesLength <= StackBuffer.MaxLength ? stackalloc ValueCapture[capturesLength] : capturesPool.Span;
        Program program = this.CaptureProgram();
        bool global = (this.Flags & RegexFlags.Global) != 0;
        int last = 0;
        int next = 0;
        while (next <= utf8.Length)
        {
            bool ok = Matcher.Match<byte, Utf8Encoding>(program, utf8, next, this.Sticky, this.Limit, slots, out MatchStop stop);
            if (stop != MatchStop.None)
            {
                throw this.Abandoned(stop);
            }

            if (!ok)
            {
                break;
            }

            Fill(true, slots, captures, out ValueMatch match);
            Write(destination, utf8.Slice(last, match.Index - last));
            evaluator(utf8, match, captures, destination);
            last = match.Index + match.Length;
            if (!global)
            {
                break;
            }

            next = match.Length > 0 ? last : this.AdvanceAfterEmptyMatch(utf8, last);
        }

        Write(destination, utf8.Slice(last));
    }

    /// <summary>
    /// Enumerates the pieces a UTF-16 text splits into at the matches, as positions, allocating nothing. The
    /// pieces are those of <see cref="Split(string)"/> without the captures of the matches, which it puts between
    /// them.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <returns>The enumerator.</returns>
    public ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input) => new(this, input, int.MaxValue);

    /// <summary>Enumerates the pieces a UTF-16 text splits into at the matches, up to a number of pieces, allocating nothing.</summary>
    /// <param name="input">The text.</param>
    /// <param name="count">The most pieces to give. 0 gives none.</param>
    /// <returns>The enumerator.</returns>
    public ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<char> input, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return new(this, input, count);
    }

    /// <summary>Enumerates the pieces a UTF-8 text splits into at the matches, as positions, allocating nothing (see <see cref="EnumerateSplits(ReadOnlySpan{char})"/>).</summary>
    /// <param name="utf8">The text.</param>
    /// <returns>The enumerator.</returns>
    public Utf8ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<byte> utf8) => new(this, utf8, int.MaxValue);

    /// <summary>Enumerates the pieces a UTF-8 text splits into at the matches, up to a number of pieces, allocating nothing.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="count">The most pieces to give. 0 gives none.</param>
    /// <returns>The enumerator.</returns>
    public Utf8ValueSplitEnumerator EnumerateSplits(ReadOnlySpan<byte> utf8, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return new(this, utf8, count);
    }

    /// <summary>The first match at or after a position, with what each group captured, for the enumerators.</summary>
    /// <summary>The match at or after a position and its groups, with a state the caller holds for a run of matches.</summary>
    internal bool TryMatchCaptures(MatchState st, ref ScanCursor cursor, ReadOnlySpan<char> input, int startAt, Span<ValueCapture> captures, out ValueMatch match)
    {
        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        bool ok = Matcher.MatchWith<char, Utf16Encoding>(st, ref cursor, this.CaptureProgram(), input, startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        return Fill(ok, slots, captures, out match);
    }

    /// <summary>The match at or after a position in UTF-8 and its groups, with a state the caller holds for a run of matches.</summary>
    internal bool TryMatchCaptures(MatchState st, ref ScanCursor cursor, ReadOnlySpan<byte> utf8, int startAt, Span<ValueCapture> captures, out ValueMatch match)
    {
        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        bool ok = Matcher.MatchWith<byte, Utf8Encoding>(st, ref cursor, this.CaptureProgram(), utf8, startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        return Fill(ok, slots, captures, out match);
    }

    internal bool TryMatchCaptures(ReadOnlySpan<char> input, int startAt, Span<ValueCapture> captures, out ValueMatch match)
    {
        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        bool ok = Matcher.Match<char, Utf16Encoding>(this.CaptureProgram(), input, startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        return Fill(ok, slots, captures, out match);
    }

    /// <summary>The first match at or after a position, with what each group captured, for the enumerators.</summary>
    internal bool TryMatchCaptures(ReadOnlySpan<byte> utf8, int startAt, Span<ValueCapture> captures, out ValueMatch match)
    {
        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        bool ok = Matcher.Match<byte, Utf8Encoding>(this.CaptureProgram(), utf8, startAt, this.Sticky, this.Limit, slots, out MatchStop stop);
        if (stop != MatchStop.None)
        {
            throw this.Abandoned(stop);
        }

        return Fill(ok, slots, captures, out match);
    }

    /// <summary>
    /// The next piece of a split at or after a position: the text up to the next match that ends after the last
    /// piece, by the rules of <c>String.prototype.split</c> (an empty match where the last piece ended splits
    /// nothing, nor does one at the end of the text).
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="p">The start of the piece, where the last match ended.</param>
    /// <param name="q">Where to look for the next match from.</param>
    /// <param name="pieceEnd">The end of the piece.</param>
    /// <param name="matchEnd">The end of the match that ends it, where the next piece starts.</param>
    /// <returns><see langword="true"/> when a match ends the piece; <see langword="false"/> when the piece runs to the end of the text.</returns>
    internal bool NextSplit(ReadOnlySpan<char> input, int p, int q, out int pieceEnd, out int matchEnd)
    {
        while (q < input.Length)
        {
            if (!this.TryMatchBounds(input, q, sticky: false, out int from, out int to) || from >= input.Length)
            {
                break;
            }

            int e = Math.Min(to, input.Length);
            if (e == p || to == from)
            {
                // An empty match where the last piece ended, or an empty match anywhere: the search goes on from
                // the next character (an empty match elsewhere does split, as the sticky probe of the
                // specification finds it at the character after the last piece, so it is tried from there).
                if (e != p && to == from)
                {
                    pieceEnd = from;
                    matchEnd = from;
                    return true;
                }

                q = this.AdvanceAfterEmptyMatch(input, from);
                continue;
            }

            pieceEnd = from;
            matchEnd = e;
            return true;
        }

        pieceEnd = input.Length;
        matchEnd = input.Length;
        return false;
    }

    /// <summary>The next piece of a split at or after a position, over UTF-8 (see <see cref="NextSplit(ReadOnlySpan{char}, int, int, out int, out int)"/>).</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="p">The start of the piece.</param>
    /// <param name="q">Where to look for the next match from.</param>
    /// <param name="pieceEnd">The end of the piece.</param>
    /// <param name="matchEnd">Where the next piece starts.</param>
    /// <returns><see langword="true"/> when a match ends the piece.</returns>
    internal bool NextSplit(ReadOnlySpan<byte> utf8, int p, int q, out int pieceEnd, out int matchEnd)
    {
        while (q < utf8.Length)
        {
            if (!this.TryMatchBounds(utf8, q, sticky: false, out int from, out int to) || from >= utf8.Length)
            {
                break;
            }

            int e = Math.Min(to, utf8.Length);
            if (e == p || to == from)
            {
                if (e != p && to == from)
                {
                    pieceEnd = from;
                    matchEnd = from;
                    return true;
                }

                q = this.AdvanceAfterEmptyMatch(utf8, from);
                continue;
            }

            pieceEnd = from;
            matchEnd = e;
            return true;
        }

        pieceEnd = utf8.Length;
        matchEnd = utf8.Length;
        return false;
    }

    private static void Write<T>(IBufferWriter<T> destination, ReadOnlySpan<T> text)
    {
        if (text.Length == 0)
        {
            return;
        }

        Span<T> span = destination.GetSpan(text.Length);
        text.CopyTo(span);
        destination.Advance(text.Length);
    }

    private static int IndexOfUnit<TUnit, TEncoding>(ReadOnlySpan<TUnit> text, int from, int ascii)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        int i = text.Slice(from).IndexOf(AsciiOf<TUnit>(ascii));
        return i < 0 ? -1 : from + i;
    }

    private static TUnit AsciiOf<TUnit>(int c)
        where TUnit : unmanaged
    {
        if (typeof(TUnit) == typeof(byte))
        {
            byte b = (byte)c;
            return System.Runtime.CompilerServices.Unsafe.As<byte, TUnit>(ref b);
        }

        char ch = (char)c;
        return System.Runtime.CompilerServices.Unsafe.As<char, TUnit>(ref ch);
    }

    private void ValidateCaptures(int length)
    {
        if (length < this.GroupCount + 1)
        {
            throw new ArgumentException("The captures span is too short.", nameof(length));
        }
    }

    /// <summary>The names of the groups as UTF-8, in the order of <see cref="GroupNames"/>, made once.</summary>
    private byte[][] GroupNamesUtf8()
    {
        byte[][]? names = this.groupNamesUtf8;
        if (names is null)
        {
            names = new byte[this.GroupNames.Count][];
            for (int k = 0; k < names.Length; k++)
            {
                names[k] = System.Text.Encoding.UTF8.GetBytes(this.GroupNames[k].Key);
            }

            this.groupNamesUtf8 = names;
        }

        return names;
    }

    /// <summary>
    /// Appends the input from one position to another. Over UTF-8 a position can fall between the halves of a
    /// supplementary character (at its third byte, where ECMA-262 without the u flag puts a code unit boundary); UTF-8
    /// cannot carry half a character, so each half at the edge of a piece is written as U+FFFD, as a lossy conversion
    /// of the UTF-16 answer writes it, and the result stays valid UTF-8.
    /// </summary>
    private static bool AppendInput<TUnit, TOutput>(ReadOnlySpan<TUnit> input, int from, int to, ref TOutput output)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TOutput : IOutput<TUnit>
#if NET
        , allows ref struct
#endif
    {
        if (typeof(TUnit) != typeof(byte) || from >= to)
        {
            return output.Append(input.Slice(from, to - from));
        }

        ReadOnlySpan<byte> bytes = MemoryMarshal.Cast<TUnit, byte>(input);
        bool lowHalf = IsBetweenHalves(bytes, from);
        bool highHalf = IsBetweenHalves(bytes, to);
        int start = lowHalf ? from + 2 : from;
        int end = highHalf ? to - 2 : to;
        ReadOnlySpan<byte> replacementCharacter = [0xEF, 0xBF, 0xBD];
        ReadOnlySpan<TUnit> fffd = MemoryMarshal.Cast<byte, TUnit>(replacementCharacter);
        return (!lowHalf || output.Append(fffd))
            && (start >= end || output.Append(input.Slice(start, end - start)))
            && (!highHalf || output.Append(fffd));
    }

    /// <summary>Whether a byte position is between the halves of a supplementary character: at the third byte of a well-formed four-byte sequence.</summary>
    private static bool IsBetweenHalves(ReadOnlySpan<byte> bytes, int at) =>
        at >= 2 && at + 1 < bytes.Length && bytes[at - 2] >= 0xF0 && bytes[at - 2] <= 0xF4
        && (bytes[at - 1] & 0xC0) == 0x80 && (bytes[at] & 0xC0) == 0x80 && (bytes[at + 1] & 0xC0) == 0x80;

    /// <summary>
    /// The replacement loop: every match (or the first) replaced by the expansion of the template, the text
    /// between kept, written to the output. Returns false when the output refuses what is written (a span that
    /// is full).
    /// </summary>
    private bool ReplaceInto<TUnit, TEncoding, TOutput>(ReadOnlySpan<TUnit> input, ReadOnlySpan<TUnit> replacement, ref TOutput output)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
        where TOutput : IOutput<TUnit>
#if NET
        , allows ref struct
#endif
    {
        // The pattern decides how many groups there are: the slots are on the stack only when there are few, and rented beyond.
        int slotsLength = 2 * (this.GroupCount + 1);
        using StackBuffer.Pooled<int> slotsPool = new(slotsLength);
        Span<int> slots = slotsLength <= StackBuffer.MaxLength ? stackalloc int[slotsLength] : slotsPool.Span;
        Program program = this.CaptureProgram();
        bool global = (this.Flags & RegexFlags.Global) != 0;
        bool plain = replacement.IndexOf(AsciiOf<TUnit>('$')) < 0;
        int last = 0;
        int next = 0;
        while (next <= input.Length)
        {
            bool ok = Matcher.Match<TUnit, TEncoding>(program, input, next, this.Sticky, this.Limit, slots, out MatchStop stop);
            if (stop != MatchStop.None)
            {
                throw this.Abandoned(stop);
            }

            if (!ok)
            {
                break;
            }

            int from = slots[0];
            int to = slots[1];
            if (!AppendInput(input, last, from, ref output))
            {
                return false;
            }

            if (plain ? !output.Append(replacement) : !this.Expand<TUnit, TEncoding, TOutput>(input, replacement, slots, ref output))
            {
                return false;
            }

            last = to;
            if (!global)
            {
                break;
            }

            next = to > from ? to : this.AdvanceAfterEmptyMatchOf<TUnit, TEncoding>(input, to);
        }

        return AppendInput(input, last, input.Length, ref output);
    }

    /// <summary>The expansion of a replacement template for one match (GetSubstitution), written to the output.</summary>
    private bool Expand<TUnit, TEncoding, TOutput>(ReadOnlySpan<TUnit> input, ReadOnlySpan<TUnit> replacement, scoped ReadOnlySpan<int> slots, ref TOutput output)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
        where TOutput : IOutput<TUnit>
#if NET
        , allows ref struct
#endif
    {
        TEncoding enc = default;
        int m = (slots.Length / 2) - 1;
        int start = slots[0];
        int end = slots[1];
        int k = 0;
        int literal = 0;
        while (k < replacement.Length)
        {
            if (enc.Unit(replacement, k) != '$' || k + 1 >= replacement.Length)
            {
                k++;
                continue;
            }

            int d = enc.Unit(replacement, k + 1);
            int consumed;
            ReadOnlySpan<TUnit> piece;
            int pieceAt = -1;
            switch (d)
            {
                case '$':
                    piece = replacement.Slice(k, 1);
                    consumed = 2;
                    break;
                case '&':
                    piece = input.Slice(start, end - start);
                    pieceAt = start;
                    consumed = 2;
                    break;
                case '`':
                    piece = input.Slice(0, start);
                    pieceAt = 0;
                    consumed = 2;
                    break;
                case '\'':
                    piece = input.Slice(end);
                    pieceAt = end;
                    consumed = 2;
                    break;
                case '<':
                {
                    int gt = this.GroupNames.Count == 0 ? -1 : IndexOfUnit<TUnit, TEncoding>(replacement, k + 2, '>');
                    if (gt < 0)
                    {
                        k++;
                        continue;
                    }

                    piece = default;
                    int number = this.GroupNumberOf<TUnit, TEncoding>(replacement.Slice(k + 2, gt - k - 2), slots);
                    if (number > 0)
                    {
                        piece = input.Slice(slots[2 * number], slots[(2 * number) + 1] - slots[2 * number]);
                        pieceAt = slots[2 * number];
                    }

                    consumed = gt + 1 - k;
                    break;
                }

                default:
                {
                    if (d < '0' || d > '9')
                    {
                        k++;
                        continue;
                    }

                    int digits = 1;
                    int index = d - '0';
                    if (k + 2 < replacement.Length)
                    {
                        int third = enc.Unit(replacement, k + 2);
                        if (third >= '0' && third <= '9')
                        {
                            int two = (index * 10) + (third - '0');
                            if (two >= 1 && two <= m)
                            {
                                digits = 2;
                                index = two;
                            }
                        }
                    }

                    if (index < 1 || index > m)
                    {
                        k++;
                        continue;
                    }

                    int from = slots[2 * index];
                    piece = from < 0 ? default : input.Slice(from, slots[(2 * index) + 1] - from);
                    pieceAt = from;
                    consumed = 1 + digits;
                    break;
                }
            }

            if (!output.Append(replacement.Slice(literal, k - literal)) || !(pieceAt < 0 ? output.Append(piece) : AppendInput(input, pieceAt, pieceAt + piece.Length, ref output)))
            {
                return false;
            }

            k += consumed;
            literal = k;
        }

        return output.Append(replacement.Slice(literal));
    }

    /// <summary>The number of the first group of a name that took part in the match (the name as units of the template), or 0.</summary>
    private int GroupNumberOf<TUnit, TEncoding>(ReadOnlySpan<TUnit> name, scoped ReadOnlySpan<int> slots)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        for (int k = 0; k < this.GroupNames.Count; k++)
        {
            bool same = typeof(TUnit) == typeof(byte)
                ? name.SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, TUnit>(this.GroupNamesUtf8()[k]))
                : name.SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.Cast<char, TUnit>(this.GroupNames[k].Key.AsSpan()));
            if (!same)
            {
                continue;
            }

            foreach (int number in this.GroupNames[k].Value)
            {
                if (slots[2 * number] >= 0)
                {
                    return number;
                }
            }

            return 0;
        }

        return 0;
    }

    private int AdvanceAfterEmptyMatchOf<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int pos)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        return typeof(TUnit) == typeof(byte)
            ? this.AdvanceAfterEmptyMatch(System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(input), pos)
            : this.AdvanceAfterEmptyMatch(System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(input), pos);
    }

    /// <summary>Writes to a buffer writer.</summary>
    /// <typeparam name="T">The unit of the text.</typeparam>
    private struct WriterOutput<T> : IOutput<T>
    {
        private readonly IBufferWriter<T> writer;

        /// <summary>Initializes a new instance of the <see cref="WriterOutput{T}"/> struct.</summary>
        /// <param name="writer">The writer.</param>
        public WriterOutput(IBufferWriter<T> writer)
        {
            this.writer = writer;
        }

        /// <inheritdoc/>
        public readonly bool Append(ReadOnlySpan<T> text)
        {
            Write(this.writer, text);
            return true;
        }
    }

#if NET
    /// <summary>Writes to a span, and refuses what does not fit.</summary>
    /// <typeparam name="T">The unit of the text.</typeparam>
    private ref struct SpanOutput<T>
        : IOutput<T>
    {
        private readonly Span<T> destination;

        /// <summary>Initializes a new instance of the <see cref="SpanOutput{T}"/> struct.</summary>
        /// <param name="destination">The span.</param>
        public SpanOutput(Span<T> destination)
        {
            this.destination = destination;
            this.Written = 0;
        }

        /// <summary>Gets the length written.</summary>
        public int Written { get; private set; }

        /// <inheritdoc/>
        public bool Append(ReadOnlySpan<T> text)
        {
            if (!text.TryCopyTo(this.destination.Slice(this.Written)))
            {
                return false;
            }

            this.Written += text.Length;
            return true;
        }
    }
#endif
}
