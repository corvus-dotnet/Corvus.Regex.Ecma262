// <copyright file="Matcher.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of vm.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of vm.go).

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Why a match was abandoned. Neither is an answer.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public enum MatchStop : byte
{
    /// <summary>The match was not abandoned.</summary>
    None,

    /// <summary>The match ran for longer than its limit.</summary>
    Time,

    /// <summary>The match needed more backtracking memory than its limit: its stack reached <see cref="Matcher.MaxStack"/> entries.</summary>
    Memory,
}

/// <summary>One entry of the backtracking stack. A choice entry says where to resume. An undo entry restores a value that was overwritten after the entries below it were pushed.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct Entry
{
    /// <summary>Resumes at <see cref="Pc"/> with the position <see cref="Pos"/>.</summary>
    public const byte Choice = 0;

    /// <summary>Belongs to a greedy single character loop that stopped at <see cref="Pos"/>. Resuming gives back one character, down to the position <see cref="A"/>.</summary>
    public const byte Rep = 1;

    /// <summary>Belongs to a lazy single character loop that has taken <see cref="A"/> characters and stands at <see cref="Pos"/>. Resuming takes one more.</summary>
    public const byte RepLazy = 2;

    /// <summary>Marks the start of a positive lookaround, with the position to restore.</summary>
    public const byte Look = 3;

    /// <summary>Marks the start of a negative lookaround, with the position to restore and the instruction to continue at when its body has failed.</summary>
    public const byte LookNeg = 4;

    /// <summary>Lies under the choices of a memo point. When it is reached, every way from the point has failed, and the bit <see cref="Pos"/> of the memory is set.</summary>
    public const byte Mark = 5;

    /// <summary>Restores the entry position of group <see cref="Pc"/> to <see cref="Pos"/>.</summary>
    public const byte Tmp = 6;

    /// <summary>Restores the start and end of group <see cref="Pc"/> to <see cref="Pos"/> and <see cref="A"/>.</summary>
    public const byte Cap = 7;

    /// <summary>Restores the count and the iteration start of loop <see cref="Pc"/> to <see cref="Pos"/> and <see cref="A"/>.</summary>
    public const byte Counter = 8;

    /// <summary>Initializes a new instance of the <see cref="Entry"/> struct.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="pc">The instruction, group or loop.</param>
    /// <param name="pos">The position.</param>
    /// <param name="a">The other value.</param>
    public Entry(byte kind, int pc, int pos, int a)
    {
        this.Kind = kind;
        this.Pc = pc;
        this.Pos = pos;
        this.A = a;
    }

    /// <summary>Gets or sets the kind.</summary>
    public byte Kind { get; set; }

    /// <summary>Gets or sets the instruction, group or loop.</summary>
    public int Pc { get; set; }

    /// <summary>Gets or sets the position.</summary>
    public int Pos { get; set; }

    /// <summary>Gets or sets the other value.</summary>
    public int A { get; set; }
}

/// <summary>
/// A search for the starts to try, kept between one start and the next: the search's kind, where it looks next,
/// and what the last start left for the next (the floor a walk-back may reach, the run a failed start may skip).
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct CandidateScan
{
    /// <summary>The starts are the occurrences of one of several literals, each of which begins a match.</summary>
    public const int Alternation = 1;

    /// <summary>The starts are a fixed number of characters before the occurrences of a literal.</summary>
    public const int Fixed = 2;

    /// <summary>The starts are the units that can begin a member of the first set, up to a last position.</summary>
    public const int First = 3;

    /// <summary>The kind.</summary>
    public int Kind;

    /// <summary>Where the search looks next.</summary>
    public int Next;

    /// <summary>The position a walk-back may not pass (<see cref="Fixed"/>), or the last start (<see cref="First"/>).</summary>
    public int Floor;

    /// <summary>The last start given (<see cref="First"/>), or -1.</summary>
    public int Last;

    /// <summary>The character at the last start, and its width.</summary>
    public int LastChar;

    /// <summary>The width of the character at the last start.</summary>
    public int LastWidth;
}

/// <summary>
/// What an enumeration learns about its text and carries from match to match: the range known to hold no rare
/// line terminator, and the range known to hold no form the program's search leaves out. One pass over the text
/// then serves every match rather than each match scanning again.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct ScanCursor
{
    /// <summary>The start of the range with no rare line terminator.</summary>
    public int RareFrom;

    /// <summary>The end of that range.</summary>
    public int RareTo;

    /// <summary>The start of the range with no extra form.</summary>
    public int ExtraFrom;

    /// <summary>The end of that range.</summary>
    public int ExtraTo;
}

/// <summary>
/// The working memory of one match. Each thread keeps one and every match on the thread uses it, so a match
/// allocates only while the stack is still growing to the depth the pattern and the text need.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class MatchState
{
    /// <summary>The value of a group that has not taken part, and of the start of an iteration that has not started.</summary>
    public const int None = -1;

    private const int MaxRetainedStack = 1 << 16;
    private const int MaxRetainedMemo = 1 << 16;

    [ThreadStatic]
    private static MatchState? current;

    /// <summary>Gets or sets the backtracking stack.</summary>
    public Entry[] Stack { get; set; } = new Entry[64];

    /// <summary>Gets or sets the start and end of each tracked group, or <see cref="None"/>.</summary>
    public int[] Caps { get; set; } = [];

    /// <summary>Gets or sets the position at which each tracked group was entered.</summary>
    public int[] Tmp { get; set; } = [];

    /// <summary>Gets or sets, for each loop, the number of iterations started and the position at which the last one started.</summary>
    public int[] Counts { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the match has a limit of time.</summary>
    public bool Timed { get; set; }

    /// <summary>Gets or sets the limit, in the ticks of <see cref="Stopwatch"/>.</summary>
    public long LimitTicks { get; set; }

    /// <summary>Gets or sets the deadline, in the ticks of <see cref="Stopwatch"/>, or 0 before the first check.</summary>
    public long Deadline { get; set; }

    /// <summary>Gets or sets the number of steps left before the next check.</summary>
    public long Steps { get; set; }

    /// <summary>Gets or sets why the match was abandoned.</summary>
    public MatchStop Stop { get; set; }

    /// <summary>
    /// Gets or sets the memory of the places where a way has failed: one bit for each memo point of the program,
    /// each position of the text and each state of the loops around the point (see <see cref="MemoPoint"/>). A bit
    /// is set when every way from the place has failed, and a way that comes to a place whose bit is set fails at
    /// once. So no place is gone through twice, and a pattern of repetitions inside repetitions takes time
    /// proportional to its size times the text and not time that doubles with each character.
    /// </summary>
    /// <remarks>
    /// The memory costs its size to clear, and a mark on the stack for each place gone through, so a match starts
    /// without it. It takes it at the check by which it has made as many steps as the memory has words and
    /// <see cref="Matcher.MemoStepsPerPosition"/> more for each position of the text, which a match that goes
    /// through its text once or a few times never makes. A place whose way failed before then is not remembered,
    /// which loses time and nothing else.
    /// </remarks>
    public ulong[] Memo { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the memory is in use.</summary>
    public bool MemoOn { get; set; }

    /// <summary>Gets or sets a value indicating whether the memory never will be in use (the program has no memo points, or the memory would be larger than <see cref="Matcher.MaxMemoWords"/>).</summary>
    public bool MemoOff { get; set; }

    /// <summary>Gets or sets the number of positions of the text.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the number of checks made so far.</summary>
    public int Checks { get; set; }

    /// <summary>Gets or sets the number of memo points of the program.</summary>
    public int Points { get; set; }

    /// <summary>Gets or sets a value indicating whether a match on this thread is using the state.</summary>
    public bool InUse { get; set; }

    /// <summary>Gets or sets a value indicating whether every match takes its memory of failed places before its first step. The tests set it, to run every pattern they have with the memory in use.</summary>
    public static bool MemoFromTheStart { get; set; }

    /// <summary>Gets or sets the start of the range of the text known to hold no rare line terminator (U+2028, U+2029), for runs of <c>.</c>.</summary>
    public int RareFrom { get; set; }

    /// <summary>Gets or sets the end of that range.</summary>
    public int RareTo { get; set; }

    /// <summary>Gets or sets the start of the range of the text known to hold no form the program's search leaves out (the long s, the Kelvin sign).</summary>
    public int ExtraFrom { get; set; }

    /// <summary>Gets or sets the end of that range.</summary>
    public int ExtraTo { get; set; }

    /// <summary>Takes the ranges an enumeration carries between its matches.</summary>
    /// <param name="cursor">The ranges.</param>
    public void LoadCursor(in ScanCursor cursor)
    {
        this.RareFrom = cursor.RareFrom;
        this.RareTo = cursor.RareTo;
        this.ExtraFrom = cursor.ExtraFrom;
        this.ExtraTo = cursor.ExtraTo;
    }

    /// <summary>Gives the ranges back to the enumeration.</summary>
    /// <param name="cursor">The ranges.</param>
    public void SaveCursor(ref ScanCursor cursor)
    {
        cursor.RareFrom = this.RareFrom;
        cursor.RareTo = this.RareTo;
        cursor.ExtraFrom = this.ExtraFrom;
        cursor.ExtraTo = this.ExtraTo;
    }

    /// <summary>
    /// The state of the calling thread for a run of matches, or a new one when the thread is already inside a
    /// match. It is not marked as in use: between the matches of the run anything may use it, and each match
    /// prepares it afresh; nothing runs inside a match that could take it.
    /// </summary>
    /// <returns>The state.</returns>
    public static MatchState Current()
    {
        MatchState? st = current;
        if (st is null)
        {
            st = new MatchState();
            current = st;
        }

        return st.InUse ? new MatchState() : st;
    }

    /// <summary>The state of the calling thread, or a new one when the thread is already inside a match.</summary>
    /// <returns>The state.</returns>
    public static MatchState Acquire()
    {
        MatchState? st = current;
        if (st is null)
        {
            st = new MatchState();
            current = st;
        }

        if (st.InUse)
        {
            // The thread is already inside a match, which nothing in the library does. A match is then given a
            // memory of its own.
            return new MatchState();
        }

        // One match on its own knows nothing of the text: what an enumeration on this thread learned is dropped.
        st.RareFrom = 0;
        st.RareTo = 0;
        st.ExtraFrom = 0;
        st.ExtraTo = 0;
        st.InUse = true;
        return st;
    }

    /// <summary>Gives the state back, dropping memory beyond what is retained between matches.</summary>
    public void Release()
    {
        if (this.Stack.Length > MaxRetainedStack)
        {
            this.Stack = new Entry[64];
        }

        if (this.Memo.Length > MaxRetainedMemo)
        {
            this.Memo = [];
        }

        this.InUse = false;
    }

    /// <summary>Takes the memory of failed places into use when the match has made enough steps to pay for it.</summary>
    public void Remember()
    {
        long words = ((((long)this.Points * this.Width) << Compiler.MaxMemoLoops) + 63) >> 6;
        if (words > Matcher.MaxMemoWords)
        {
            this.MemoOff = true;
            return;
        }

        if ((long)this.Checks * Matcher.CheckEvery < words + (Matcher.MemoStepsPerPosition * (long)this.Width) && !MemoFromTheStart)
        {
            return;
        }

        if (this.Memo.Length < words)
        {
            this.Memo = new ulong[words];
        }
        else
        {
            Array.Clear(this.Memo, 0, (int)words);
        }

        this.MemoOn = true;
    }

    /// <summary>Called every <see cref="Matcher.CheckEvery"/> steps. It reports whether the match may go on.</summary>
    /// <param name="depth">The depth of the stack.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Check(int depth)
    {
        if (!this.MemoOn && !this.MemoOff)
        {
            this.Checks++;
            this.Remember();
        }

        if (!this.Timed)
        {
            return true;
        }

        if (depth > Matcher.MaxStack)
        {
            this.Stop = MatchStop.Memory;
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        if (this.Deadline == 0)
        {
            this.Deadline = now + this.LimitTicks;
        }
        else if (now > this.Deadline)
        {
            this.Stop = MatchStop.Time;
            return false;
        }

        return true;
    }

    /// <summary>Applies one undo entry.</summary>
    /// <param name="e">The entry.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Undo(in Entry e)
    {
        int k = e.Pc;
        switch (e.Kind)
        {
            case Entry.Tmp:
                this.Tmp[k] = e.Pos;
                break;
            case Entry.Cap:
                this.Caps[2 * k] = e.Pos;
                this.Caps[(2 * k) + 1] = e.A;
                break;
            case Entry.Counter:
                this.Counts[2 * k] = e.Pos;
                this.Counts[(2 * k) + 1] = e.A;
                break;
            default:
                break;
        }
    }
}

/// <summary>The backtracking matcher.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class Matcher
{
    /// <summary>
    /// The number of steps the matcher takes between two looks at the clock and at the depth of its stack. A step is
    /// one instruction, and eight characters of a single character loop or of a backreference. A step takes a few
    /// nanoseconds, so the clock is read every ten to twenty microseconds, a match that ends before its first check
    /// never reads it, and a match goes past its limit by that much at most.
    /// </summary>
    public const long CheckEvery = 1 << 12;

    /// <summary>
    /// The number of entries the backtracking stack of a match with a limit may hold (16 bytes each). A match that
    /// needs more is abandoned. A pattern that keeps a choice for every character of its text needs one entry or a
    /// few for each, so this is room for a text of some hundred thousand characters, and a pattern that pushes
    /// without consuming, such as <c>(?:a?){2147483647}</c>, reaches it in a few milliseconds. It is looked at with
    /// the clock, so the stack can go past it by <see cref="CheckEvery"/> entries.
    /// </summary>
    public const int MaxStack = 1 << 20;

    /// <summary>The largest memory of failed places, in words of 64 bits: 8 MB. A match that would need more goes without it.</summary>
    public const int MaxMemoWords = 1 << 20;

    /// <summary>The number of steps for each position of the text that a match makes before it takes the memory into use.</summary>
    public const int MemoStepsPerPosition = 64;

    /// <summary>
    /// The depth of the backtracking stack beyond which a memo point is not marked. A mark is an entry of the stack,
    /// and a pattern that keeps no choice for a character still marks a place for each, so on a long text the marks
    /// alone would reach <see cref="MaxStack"/>. A place that is not marked is not remembered, which loses time and
    /// nothing else, and the places below this depth are remembered when the stack unwinds, so a later start has
    /// the room to mark the next ones.
    /// </summary>
    public const int MaxMarkDepth = MaxStack / 2;

    /// <summary>
    /// Reports whether the program matches anywhere in the text (or only at <paramref name="start"/> when
    /// <paramref name="sticky"/>). With a limit the match is abandoned when it has run for longer than that, or
    /// when its stack has reached <see cref="MaxStack"/>, and <paramref name="stop"/> says which.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="p">The program.</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position to search from.</param>
    /// <param name="sticky">Whether a match may start only at <paramref name="start"/>.</param>
    /// <param name="limit">The limit of time, or a value of zero or less for none.</param>
    /// <param name="stop">Why the match was abandoned, or <see cref="MatchStop.None"/>.</param>
    /// <returns><see langword="true"/> when the program matches.</returns>
    public static bool IsMatch<TUnit, TEncoding>(Program p, ReadOnlySpan<TUnit> input, int start, bool sticky, TimeSpan limit, out MatchStop stop)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (p.Shape != SimpleShape.None)
        {
            stop = MatchStop.None;
            return Simple<TUnit, TEncoding>(p, null, input, start, sticky, out _, out _);
        }

        MatchState st = MatchState.Acquire();
        try
        {
            Prepare(st, p, input.Length, limit);
            bool ok = Search<TUnit, TEncoding>(p, st, input, start, sticky, out _, out _);
            stop = st.Stop;
            return ok && stop == MatchStop.None;
        }
        finally
        {
            st.Release();
        }
    }

    /// <summary>
    /// Finds the first match at or after <paramref name="start"/> (at <paramref name="start"/> alone when
    /// <paramref name="sticky"/>) and reports where it is and what each tracked group captured.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="p">The program, which must track every group (see <see cref="Compiler.Compile"/>).</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position to search from.</param>
    /// <param name="sticky">Whether a match may start only at <paramref name="start"/>.</param>
    /// <param name="limit">The limit of time, or a value of zero or less for none.</param>
    /// <param name="captures">
    /// Receives the start and end of the match in its first two slots, then the start and end of each tracked
    /// group in order, or -1 and -1 for a group that did not take part. Its length is at least
    /// <c>2 * (1 + p.Groups)</c>.
    /// </param>
    /// <param name="stop">Why the match was abandoned, or <see cref="MatchStop.None"/>.</param>
    /// <returns><see langword="true"/> when the program matches.</returns>
    public static bool Match<TUnit, TEncoding>(Program p, ReadOnlySpan<TUnit> input, int start, bool sticky, TimeSpan limit, Span<int> captures, out MatchStop stop)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (p.Shape != SimpleShape.None)
        {
            stop = MatchStop.None;
            bool found = Simple<TUnit, TEncoding>(p, null, input, start, sticky, out captures[0], out captures[1]);
            return found;
        }

        MatchState st = MatchState.Acquire();
        try
        {
            ScanCursor cursor = default;
            return MatchWith<TUnit, TEncoding>(st, ref cursor, p, input, start, sticky, limit, captures, out stop);
        }
        finally
        {
            st.Release();
        }
    }

    /// <summary>
    /// Reports whether the program matches at or after a position, with a state the caller holds for a run of
    /// matches (an enumeration), so that nothing is acquired and released for each one. The state is prepared
    /// here; the caller keeps nothing of it between matches.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="st">The state.</param>
    /// <param name="cursor">What the run of matches has learned about the text (see <see cref="ScanCursor"/>).</param>
    /// <param name="p">The program.</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <param name="sticky">Whether the match must start at the position.</param>
    /// <param name="limit">The limit of time, or zero for none.</param>
    /// <param name="captures">Receives the bounds of the match and of each group.</param>
    /// <param name="stop">The reason the match was abandoned, or <see cref="MatchStop.None"/>.</param>
    /// <returns>Whether the program matched.</returns>
    public static bool MatchWith<TUnit, TEncoding>(MatchState st, ref ScanCursor cursor, Program p, ReadOnlySpan<TUnit> input, int start, bool sticky, TimeSpan limit, Span<int> captures, out MatchStop stop)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        bool uses = p.UsesCursor;
        if (uses)
        {
            st.LoadCursor(in cursor);
        }

        if (p.Shape != SimpleShape.None)
        {
            stop = MatchStop.None;
            bool found = Simple<TUnit, TEncoding>(p, st, input, start, sticky, out captures[0], out captures[1]);
            if (uses)
            {
                st.SaveCursor(ref cursor);
            }

            return found;
        }

        Prepare(st, p, input.Length, limit);
        bool ok = Search<TUnit, TEncoding>(p, st, input, start, sticky, out int from, out int to);
        if (uses)
        {
            st.SaveCursor(ref cursor);
        }

        stop = st.Stop;
        if (!ok || stop != MatchStop.None)
        {
            return false;
        }

        captures[0] = from;
        captures[1] = to;
        int[] caps = st.Caps;
        for (int k = 0; k < 2 * p.Groups; k++)
        {
            captures[2 + k] = caps[k];
        }

        return true;
    }

    /// <summary>
    /// The search for a program of a simple shape (see <see cref="SimpleShape"/>): the match is where the search
    /// finds the literal or the member, and nothing of the matcher runs.
    /// </summary>
    public static bool Simple<TUnit, TEncoding>(Program p, MatchState? st, ReadOnlySpan<TUnit> input, int start, bool sticky, out int from, out int to)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        from = -1;
        to = -1;
        int len = input.Length;
        switch (p.Shape)
        {
            case SimpleShape.Literal:
            {
                int i = enc.IndexOfPrefix(input, start, p);
                if (i < 0 || (sticky && i != start))
                {
                    return false;
                }

                from = i;
                to = i + enc.PrefixLength(p);
                return true;
            }

            case SimpleShape.Set:
            {
                CharSet set = p.ShapeSet!;
                bool units = p.Units;
                int pos = start;
                while (pos < len)
                {
#if NET
                    if (!sticky)
                    {
                        int i = enc.IndexOfCandidate(input, pos, set, p.FirstIsSmall);
                        if (i < 0)
                        {
                            return false;
                        }

                        pos = enc.AlignStart(input, i, units);
                        if (pos >= len)
                        {
                            return false;
                        }
                    }
#endif
                    int c = enc.Unit(input, pos);
                    int w = 1;
                    if (c >= 0x80)
                    {
                        c = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);
                    }

                    if (set.Contains(c))
                    {
                        from = pos;
                        to = pos + w;
                        return true;
                    }

                    if (sticky)
                    {
                        return false;
                    }

                    pos += w;
                }

                return false;
            }

            case SimpleShape.Loop:
            {
                CharSet set = p.ShapeSet!;
                bool units = p.Units;
                int min = p.Insts[0].Min;
                int pos = start;
                if (min == 0)
                {
                    // The match is the run from the start, empty or not.
                    from = pos;
                    to = pos < len ? (set.IsLineRest ? EndOfLine<TUnit, TEncoding>(st, input, pos) : EndOfRun<TUnit, TEncoding>(set, input, pos, units)) : pos;
                    return true;
                }

                while (pos < len)
                {
#if NET
                    if (!sticky)
                    {
                        int i = enc.IndexOfCandidate(input, pos, set, p.FirstIsSmall);
                        if (i < 0)
                        {
                            return false;
                        }

                        pos = enc.AlignStart(input, i, units);
                        if (pos >= len)
                        {
                            return false;
                        }
                    }
#endif
                    int c = enc.Unit(input, pos);
                    int w = 1;
                    if (c >= 0x80)
                    {
                        c = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);
                    }

                    if (set.Contains(c))
                    {
                        from = pos;
                        to = set.IsLineRest ? EndOfLine<TUnit, TEncoding>(st, input, pos + w) : EndOfRun<TUnit, TEncoding>(set, input, pos + w, units);
                        return true;
                    }

                    if (sticky)
                    {
                        return false;
                    }

                    pos += w;
                }

                return false;
            }

            case SimpleShape.Sequence:
            case SimpleShape.LiteralAlternation:
            {
                CharSet first = p.First!;
                bool units = p.Units;
                int pos = start;
                while (pos < len)
                {
#if NET
                    if (!sticky)
                    {
                        AlternationSearch? alternation = p.Alternation;
                        int i = alternation is null ? enc.IndexOfAnyNeedle(input, pos, p) : enc.IndexOfAlternation(input, pos, alternation);
                        if (i == -2)
                        {
                            i = enc.IndexOfCandidate(input, pos, first, p.FirstIsSmall);
                        }
                        else if (alternation is not null && alternation.Forms is not null && enc.HasAlternativeExtras(alternation))
                        {
                            // A form the search leaves out matters where a start before the candidate could reach
                            // it: every start from which an alternative could reach it is tried first, in order.
                            int window = enc.AlternativeWindow(alternation);
                            int limit = i < 0 ? len : Math.Min(len, i + window);
                            int e = -1;
                            if (!ExtrasClear(st, pos, limit))
                            {
                                int scanFrom = ExtrasFrom(st, pos);
                                int scanTo = ExtrasTo(st, pos, limit, len);
                                e = ExtrasSeen(st, pos, scanTo, enc.IndexOfAlternativeExtra(input, scanFrom, scanTo, alternation), limit);
                            }

                            if (e >= 0 && (i < 0 || e - window < i))
                            {
                                int s = Math.Max(pos, e - window + 1);
                                while (s <= e)
                                {
                                    s = enc.AlignStart(input, s, units);
                                    if (s > e)
                                    {
                                        break;
                                    }

                                    int ends = AlternativeEnd<TUnit, TEncoding>(p, input, s);
                                    if (ends >= 0)
                                    {
                                        from = s;
                                        to = ends;
                                        return true;
                                    }

                                    s = NextPosition<TUnit, TEncoding>(input, s, units);
                                }

                                pos = enc.NextStart(input, e);
                                continue;
                            }
                        }

                        if (i < 0)
                        {
                            return false;
                        }

                        pos = enc.AlignStart(input, i, units);
                        if (pos >= len)
                        {
                            return false;
                        }
                    }
#endif
                    int end = p.Shape == SimpleShape.Sequence ? SequenceAt<TUnit, TEncoding>(p, input, pos, units) : AlternativeEnd<TUnit, TEncoding>(p, input, pos);
                    if (end >= 0)
                    {
                        from = pos;
                        to = end;
                        return true;
                    }

                    if (sticky)
                    {
                        return false;
                    }

                    pos = NextPosition<TUnit, TEncoding>(input, pos, units);
                }

                return false;
            }

#if NET
            case SimpleShape.CaseLiteral:
            {
                // The anchored search for the literal in its forms, and where a form the search leaves out exists
                // (the long s, the Kelvin sign, over UTF-8) a check of every start that could reach one, as the
                // general search does.
                int n = enc.CaseLength(p);
                int window = enc.CaseWindow(p);
                bool extras = enc.HasCaseExtras(p);
                bool units = p.Units;
                int pos = start;
                while (true)
                {
                    int i = enc.IndexOfCasePrefix(input, pos, p);
                    int e = -1;
                    if (extras)
                    {
                        int limit = i < 0 ? len : Math.Min(len, i + window);
                        if (!ExtrasClear(st, pos, limit))
                        {
                            int scanFrom = ExtrasFrom(st, pos);
                            int scanTo = ExtrasTo(st, pos, limit, len);
                            e = ExtrasSeen(st, pos, scanTo, enc.IndexOfCaseExtra(input, scanFrom, scanTo, p), limit);
                        }
                    }

                    if (i < 0 && e < 0)
                    {
                        return false;
                    }

                    if (e >= 0 && (i < 0 || e - window < i))
                    {
                        int s = Math.Max(pos, e - window + 1);
                        while (s <= e)
                        {
                            s = enc.AlignStart(input, s, units);
                            if (s > e)
                            {
                                break;
                            }

                            int end = SequenceAt<TUnit, TEncoding>(p, input, s, units);
                            if (end >= 0 && (!sticky || s == start))
                            {
                                from = s;
                                to = end;
                                return true;
                            }

                            if (sticky)
                            {
                                return false;
                            }

                            s = NextPosition<TUnit, TEncoding>(input, s, units);
                        }

                        pos = enc.NextStart(input, e);
                        continue;
                    }

                    if (sticky && i != start)
                    {
                        return false;
                    }

                    from = i;
                    to = i + n;
                    return true;
                }
            }
#endif

            default:
                break;
        }

        return false;
    }

    /// <summary>The end of the match of a <see cref="SimpleShape.Sequence"/> program at a position, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SequenceAt<TUnit, TEncoding>(Program p, ReadOnlySpan<TUnit> input, int pos, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        Inst[] insts = p.Insts;
        int len = input.Length;
        for (int k = 0; k < insts.Length - 1; k++)
        {
            if (pos >= len)
            {
                return -1;
            }

            int c = enc.Unit(input, pos);
            int w = 1;
            if (c >= 0x80)
            {
                c = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);
            }

            ref Inst i = ref insts[k];
            if (i.Set is not null ? !i.Set.Contains(c) : c != i.R)
            {
                return -1;
            }

            pos += w;
        }

        return pos;
    }

    /// <summary>
    /// The length in units of the first alternative that is at a position, each alternative the forms of its
    /// characters: a character of the text is decoded and must be one of the forms, so a form of another length
    /// (the long s for s over UTF-8) is matched here where the anchored search cannot see it.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="alternatives">The alternatives, each the forms of its characters.</param>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns>The length, or -1.</returns>
    public static int FormsAt<TUnit, TEncoding>(int[][][] alternatives, ReadOnlySpan<TUnit> input, int pos, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int len = input.Length;
        if (pos >= len)
        {
            return -1;
        }

        // The first character decides most alternatives at once.
        int first = enc.Unit(input, pos);
        int firstWidth = 1;
        if (first >= 0x80)
        {
            first = units ? enc.UnitAt(input, pos, out firstWidth) : enc.Decode(input, pos, out firstWidth);
        }

        for (int a = 0; a < alternatives.Length; a++)
        {
            int[][] forms = alternatives[a];
            if (!IsForm(forms[0], first))
            {
                continue;
            }

            int at = pos + firstWidth;
            bool ok = true;
            for (int k = 1; k < forms.Length; k++)
            {
                if (at >= len)
                {
                    ok = false;
                    break;
                }

                int c = enc.Unit(input, at);
                int w = 1;
                if (c >= 0x80)
                {
                    c = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);
                }

                if (!IsForm(forms[k], c))
                {
                    ok = false;
                    break;
                }

                at += w;
            }

            if (ok)
            {
                return at - pos;
            }
        }

        return -1;

        static bool IsForm(int[] character, int c)
        {
            for (int f = 0; f < character.Length; f++)
            {
                if (character[f] == c)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The end of the match of a <see cref="SimpleShape.LiteralAlternation"/> program at a position, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlternativeEnd<TUnit, TEncoding>(Program p, ReadOnlySpan<TUnit> input, int pos)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int n = enc.AlternativeAt(input, pos, p.Alternation!, p.Units);
        return n < 0 ? -1 : pos + n;
    }

    /// <summary>The position after the character at a position (a code unit with <paramref name="units"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPosition<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int pos, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        if (enc.Unit(input, pos) < 0x80)
        {
            return pos + 1;
        }

        int w;
        if (units)
        {
            enc.UnitAt(input, pos, out w);
        }
        else
        {
            enc.Decode(input, pos, out w);
        }

        return pos + w;
    }

    private static void Prepare(MatchState st, Program p, int length, TimeSpan limit)
    {
        if (st.Caps.Length < 2 * p.Groups)
        {
            st.Caps = new int[2 * p.Groups];
            st.Tmp = new int[p.Groups];
        }

        if (p.Groups != 0)
        {
            for (int k = 0; k < 2 * p.Groups; k++)
            {
                st.Caps[k] = MatchState.None;
            }

            Array.Clear(st.Tmp, 0, p.Groups);
        }

        if (p.Loops != 0)
        {
            if (st.Counts.Length < 2 * p.Loops)
            {
                st.Counts = new int[2 * p.Loops];
            }

            Array.Clear(st.Counts, 0, 2 * p.Loops);
        }
        st.Timed = limit > TimeSpan.Zero;
        st.LimitTicks = st.Timed ? (long)(limit.Ticks * (Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond)) : 0;
        st.Deadline = 0;
        st.Steps = CheckEvery;
        st.Stop = MatchStop.None;
        st.MemoOn = false;
        st.MemoOff = p.Points.Length == 0;
        st.Checks = 0;
        st.Width = length + 1;
        st.Points = p.Points.Length;
        if (!st.MemoOff && MatchState.MemoFromTheStart)
        {
            st.Remember();
        }
    }

    private static bool Search<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, int start, bool sticky, out int from, out int to)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int len = input.Length;
        from = -1;
        to = -1;
        if (len - start < p.Need)
        {
            return false;
        }

        if (sticky)
        {
            from = start;
            return Run<TUnit, TEncoding>(p, st, input, start, out to);
        }

        if (p.Anchored)
        {
            from = 0;
            return start == 0 && Run<TUnit, TEncoding>(p, st, input, 0, out to);
        }

        if (p.Utf16Prefix.Length != 0)
        {
            int at = start;
            while (true)
            {
                // The search for the literal takes time proportional to the text. `at` is at the start of a
                // character: a literal starts with one.
                int i = enc.IndexOfPrefix(input, at, p);
                if (i < 0)
                {
                    return false;
                }

                at = i;
                if (len - at < p.Need)
                {
                    return false;
                }

                from = at;
                if (Run<TUnit, TEncoding>(p, st, input, at, out to))
                {
                    return true;
                }

                if (st.Stop != MatchStop.None)
                {
                    return false;
                }

                at = enc.NextStart(input, at);
            }
        }

        CharSet? first = p.First;
        bool units = p.Units;
        int pos = start;
#if NET
        if (enc.HasCaseNeedle(p))
        {
            // Every match starts with the literal in one of its forms: the anchored search is vectorised. A form
            // the search leaves out (the long s, the Kelvin sign, over UTF-8) is looked for apart, and every start
            // from which the literal could reach it is tried, in order.
            int window = enc.CaseWindow(p);
            bool extras = enc.HasCaseExtras(p);

            while (true)
            {
                int i = enc.IndexOfCasePrefix(input, pos, p);

                // An extra form matters only where a start before the candidate could reach it, so it is looked
                // for up to the end of that candidate's literal, or to the end of the text when there is no
                // candidate.
                int e = -1;
                if (extras)
                {
                    int limit = i < 0 ? len : Math.Min(len, i + window);
                    if (!ExtrasClear(st, pos, limit))
                    {
                        int scanFrom = ExtrasFrom(st, pos);
                        int scanTo = ExtrasTo(st, pos, limit, len);
                        e = ExtrasSeen(st, pos, scanTo, enc.IndexOfCaseExtra(input, scanFrom, scanTo, p), limit);
                    }
                }

                if (i < 0 && e < 0)
                {
                    return false;
                }

                if (e >= 0 && (i < 0 || e - window < i))
                {
                    int s = Math.Max(pos, e - window + 1);
                    while (s <= e)
                    {
                        s = enc.AlignStart(input, s, units);
                        if (s > e)
                        {
                            break;
                        }

                        from = s;
                        if (len - s < p.Need)
                        {
                            return false;
                        }

                        if (Run<TUnit, TEncoding>(p, st, input, s, out to))
                        {
                            return true;
                        }

                        if (st.Stop != MatchStop.None)
                        {
                            return false;
                        }

                        s = enc.NextStart(input, s);
                    }

                    pos = enc.NextStart(input, e);
                    continue;
                }

                from = i;
                if (len - i < p.Need)
                {
                    return false;
                }

                if (Run<TUnit, TEncoding>(p, st, input, i, out to))
                {
                    return true;
                }

                if (st.Stop != MatchStop.None)
                {
                    return false;
                }

                pos = enc.NextStart(input, i);
            }
        }

        if (p.AfterLoopMax >= 0)
        {
            // Every match is at most AfterLoopMax (and at least LoopMin) characters of the loop's set, then the
            // literal or one of the alternatives: each occurrence of those allows the starts the loop could reach
            // it from, which are tried in order, the earliest occurrence's first. A form the alternatives' search
            // leaves out is looked for apart, and the starts that could reach it come where it does.
            AlternationSearch? alternation = p.AfterLoopAlternation;
            int most = p.AfterLoopMax;
            int least = p.LoopMin;
            bool extras = alternation is not null && enc.HasAlternativeExtras(alternation);
            int window = extras ? enc.AlternativeWindow(alternation!) : 0;
            int next = pos + least;
            int tried = pos - 1;
            while (true)
            {
                int i = alternation is null ? enc.IndexOfAfterLoop(input, next, p) : enc.IndexOfAlternation(input, next, alternation);
                int lo = i;
                int hi = i;
                if (extras)
                {
                    int limit = i < 0 ? len : Math.Min(len, i + window);
                    int e = enc.IndexOfAlternativeExtra(input, next, limit, alternation!);
                    if (e >= 0 && (i < 0 || e - window < i))
                    {
                        lo = Math.Max(next, e - window + 1);
                        hi = e;
                        i = e;
                    }
                }

                if (i < 0)
                {
                    return false;
                }

                // The starts from which the loop could reach an occurrence in [lo, hi].
                int s = Math.Max(Math.Max(pos, tried + 1), lo - most);
                int last = hi - least;
                while (s <= last)
                {
                    s = enc.AlignStart(input, s, p.Units);
                    if (s > last)
                    {
                        break;
                    }

                    from = s;
                    if (len - s < p.Need)
                    {
                        return false;
                    }

                    if (Run<TUnit, TEncoding>(p, st, input, s, out to))
                    {
                        return true;
                    }

                    if (st.Stop != MatchStop.None)
                    {
                        return false;
                    }

                    tried = s;
                    s = enc.NextStart(input, s);
                }

                next = enc.NextStart(input, lo);
            }
        }

        if (p.AfterLoopUtf16.Length != 0)
        {
            // Every match is a run of the loop's set then the literal: the literal is searched for, and the run
            // before it is walked back to the earliest start. A failed start fails for every later start of the
            // same run (the loop ends at the same place), so the next literal occurrence is searched for next.
            CharSet[] loopSets = p.AfterLoopSets;
            int[] loopMins = p.AfterLoopMins;
            int least = 0;
            foreach (int m in loopMins)
            {
                least += m;
            }

            while (true)
            {
                int i = enc.IndexOfAfterLoop(input, pos + least, p);
                if (i < 0)
                {
                    return false;
                }

                // The runs of the loops' sets before the occurrence, walked back last loop first; a loop that
                // needs a character and finds an empty run rules this occurrence out.
                int s = i;
                bool short_ = false;
                for (int k = loopSets.Length - 1; k >= 0; k--)
                {
                    CharSet loopSet = loopSets[k];
                    int runEnd = s;
                    while (s > pos)
                    {
                        int c = enc.Unit(input, s - 1);
                        if (c < 0x80)
                        {
                            if (!loopSet.HasAscii(c))
                            {
                                break;
                            }

                            s--;
                            continue;
                        }

                        int r = units ? enc.UnitBefore(input, s, out int w) : enc.DecodeBefore(input, s, out w);
                        if (!loopSet.Contains(r) || s - w < pos)
                        {
                            break;
                        }

                        s -= w;
                    }

                    if (loopMins[k] == 1 && s == runEnd)
                    {
                        short_ = true;
                        break;
                    }
                }

                if (short_)
                {
                    pos = enc.NextStart(input, i);
                    continue;
                }

                from = s;
                if (len - s < p.Need)
                {
                    return false;
                }

                if (Run<TUnit, TEncoding>(p, st, input, s, out to))
                {
                    return true;
                }

                if (st.Stop != MatchStop.None)
                {
                    return false;
                }

                pos = enc.NextStart(input, i);
            }
        }
#endif
        if (p.RequiredAlternation is not null)
        {
            // Every match holds one of the literals, so many to so many characters after its start: each
            // occurrence of one allows the starts that could reach it, which the scan below tries in order within
            // that window alone. The window is in units, taking the most units a character can be.
            AlternationSearch alternation = p.RequiredAlternation;
            int perCharacter = typeof(TUnit) == typeof(byte) ? 4 : 2;
            int lo = p.RequiredAlternationLo;
            int hi = p.RequiredAlternationHi * perCharacter;
            bool extras = enc.HasAlternativeExtras(alternation);
            int window = extras ? enc.AlternativeWindow(alternation) : 0;
            int next = pos + lo;
            int tried = pos - 1;
            while (true)
            {
                int i = enc.IndexOfAlternation(input, next, alternation);
                if (extras)
                {
                    int limit = i < 0 ? len : Math.Min(len, i + window);
                    int e = -1;
                    if (!ExtrasClear(st, next, limit))
                    {
                        int scanFrom = ExtrasFrom(st, next);
                        int scanTo = ExtrasTo(st, next, limit, len);
                        e = ExtrasSeen(st, next, scanTo, enc.IndexOfAlternativeExtra(input, scanFrom, scanTo, alternation), limit);
                    }

                    if (e >= 0 && (i < 0 || e - window < i))
                    {
                        i = Math.Max(next, e - window + 1);
                    }
                }

                if (i < 0)
                {
                    return false;
                }

                if (hi == 0)
                {
                    // Every alternative starts with its literal: the match starts at the occurrence or not at all.
                    if (p.Runner is not null)
                    {
                        // The generated code loops over the occurrences itself.
                        CandidateScan scan = new() { Kind = CandidateScan.Alternation, Next = next, Floor = pos, Last = -1 };
                        return ScanWith<TUnit, TEncoding>(p, st, input, ref scan, out from, out to);
                    }

                    if (len - i >= p.Need)
                    {
                        from = i;
                        if (Run<TUnit, TEncoding>(p, st, input, i, out to))
                        {
                            return true;
                        }

                        if (st.Stop != MatchStop.None)
                        {
                            return false;
                        }
                    }

                    next = enc.NextStart(input, i);
                    continue;
                }

                int wStart = Math.Max(Math.Max(pos, tried + 1), i - hi);
                int wEnd = i - lo;
                if (wStart <= wEnd)
                {
                    if (ScanStarts<TUnit, TEncoding>(p, st, input, wStart, wEnd, out from, out to))
                    {
                        return true;
                    }

                    if (st.Stop != MatchStop.None)
                    {
                        return false;
                    }

                    tried = wEnd;
                }

                next = enc.NextStart(input, i);
            }
        }

        if (p.RequiredCountMax >= 0)
        {
            // Every match holds the literal after so many to so many characters: each occurrence allows the
            // starts that many characters before it, found by walking back a character at a time, and tried in
            // order from the farthest.
            int lo = p.RequiredCount;
            int hi = p.RequiredCountMax;
            Span<int> backs = stackalloc int[hi + 1];
            int next = pos + lo;
            int tried = pos - 1;
            while (true)
            {
                int i = enc.IndexOfRequired(input, next, p);
                if (i < 0)
                {
                    return false;
                }

                int k = 0;
                int at = i;
                backs[0] = i;
                while (k < hi)
                {
                    int prev = enc.WalkBack(input, at, 1, pos, units);
                    if (prev < 0)
                    {
                        break;
                    }

                    at = prev;
                    backs[++k] = at;
                }

                for (int d = k; d >= lo; d--)
                {
                    int s = backs[d];
                    if (s <= tried)
                    {
                        continue;
                    }

                    from = s;
                    if (len - s < p.Need)
                    {
                        return false;
                    }

                    if (Run<TUnit, TEncoding>(p, st, input, s, out to))
                    {
                        return true;
                    }

                    if (st.Stop != MatchStop.None)
                    {
                        return false;
                    }

                    tried = s;
                }

                next = enc.NextStart(input, i);
            }
        }

        if (p.RequiredCount >= 0)
        {
            // Every match holds the literal after a fixed number of characters, so the literal's first occurrence
            // at or after pos plus that many allows one start, and a start before it can hold no match. Where the
            // characters are a fixed number of units of this encoding, the start is the occurrence less that;
            // otherwise it is found by walking back over the characters.
            int offset = enc.RequiredOffset(p);
            int count = p.RequiredCount;
            int next = pos + (offset >= 0 ? offset : count);
            if (p.Runner is not null)
            {
                CandidateScan scan = new() { Kind = CandidateScan.Fixed, Next = next, Floor = pos, Last = -1 };
                return ScanWith<TUnit, TEncoding>(p, st, input, ref scan, out from, out to);
            }

            while (true)
            {
                int i = enc.IndexOfRequired(input, next, p);
                if (i < 0)
                {
                    return false;
                }

                next = enc.NextStart(input, i);
                int at = offset >= 0 ? i - offset : enc.WalkBack(input, i, count, pos, units);
                if (at < 0)
                {
                    // Fewer characters between the earliest start and the occurrence than the literal needs: the
                    // next occurrence may do.
                    continue;
                }

                if (len - at < p.Need)
                {
                    return false;
                }

                from = at;
                if (Run<TUnit, TEncoding>(p, st, input, at, out to))
                {
                    return true;
                }

                if (st.Stop != MatchStop.None)
                {
                    return false;
                }

                pos = enc.NextStart(input, at);
            }
        }

        if (p.Runner is not null)
        {
            CandidateScan scan = new() { Kind = CandidateScan.First, Next = pos, Floor = len, Last = -1 };
            return ScanWith<TUnit, TEncoding>(p, st, input, ref scan, out from, out to);
        }

        return ScanStarts<TUnit, TEncoding>(p, st, input, pos, len, out from, out to);
    }

    /// <summary>The generated code's loop over the starts of a search.</summary>
    private static bool ScanWith<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, ref CandidateScan scan, out int from, out int to)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        GeneratedRunner runner = p.Runner!;
        bool ok = typeof(TUnit) == typeof(byte)
            ? runner.Scan(p, st, System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(input), ref scan, out from, out to)
            : runner.Scan(p, st, System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(input), ref scan, out from, out to);
        return ok && st.Stop == MatchStop.None;
    }

    /// <summary>
    /// The next start a search yields, or none: the occurrences of one of several literals that each begin a
    /// match; a fixed number of characters before the occurrences of a literal; or the units that can begin a
    /// member of the first set, with the run a failed start's leading loop ate passed over.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="scan">The search.</param>
    /// <param name="start">The start.</param>
    /// <returns>Whether there is one.</returns>
    public static bool NextStart<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, ref CandidateScan scan, out int start)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int len = input.Length;
        bool units = p.Units;
        start = -1;
        if (st.Stop != MatchStop.None)
        {
            return false;
        }

        switch (scan.Kind)
        {
            case CandidateScan.Alternation:
            {
                AlternationSearch alternation = p.RequiredAlternation!;
                bool extras = enc.HasAlternativeExtras(alternation);
                int window = extras ? enc.AlternativeWindow(alternation) : 0;
                while (true)
                {
                    int next = scan.Next;
                    int i = enc.IndexOfAlternation(input, next, alternation);
                    if (extras)
                    {
                        int limit = i < 0 ? len : Math.Min(len, i + window);
                        int e = -1;
                        if (!ExtrasClear(st, next, limit))
                        {
                            int scanFrom = ExtrasFrom(st, next);
                            int scanTo = ExtrasTo(st, next, limit, len);
                            e = ExtrasSeen(st, next, scanTo, enc.IndexOfAlternativeExtra(input, scanFrom, scanTo, alternation), limit);
                        }

                        if (e >= 0 && (i < 0 || e - window < i))
                        {
                            i = Math.Max(next, e - window + 1);
                        }
                    }

                    if (i < 0)
                    {
                        return false;
                    }

                    scan.Next = enc.NextStart(input, i);
                    if (len - i >= p.Need)
                    {
                        start = i;
                        return true;
                    }
                }
            }

            case CandidateScan.Fixed:
            {
                int offset = enc.RequiredOffset(p);
                int count = p.RequiredCount;
                while (true)
                {
                    int i = enc.IndexOfRequired(input, scan.Next, p);
                    if (i < 0)
                    {
                        return false;
                    }

                    scan.Next = enc.NextStart(input, i);
                    int at = offset >= 0 ? i - offset : enc.WalkBack(input, i, count, scan.Floor, units);
                    if (at < 0)
                    {
                        continue;
                    }

                    if (len - at < p.Need)
                    {
                        return false;
                    }

                    scan.Floor = enc.NextStart(input, at);
                    start = at;
                    return true;
                }
            }

            default:
            {
                CharSet? first = p.First;
                int until = scan.Floor;
                int pos = scan.Next;
                if (scan.Last >= 0)
                {
                    // After a failed start: the run its leading loop ate is passed over, else the next character.
                    int w = scan.LastWidth;
                    if (p.SkipRun && p.LoopSet!.Contains(scan.LastChar))
                    {
                        int end = EndOfRun<TUnit, TEncoding>(p.LoopSet!, input, scan.Last + w, units);
                        pos = end > scan.Last + w ? end : scan.Last + w;
                    }
                    else
                    {
                        pos = scan.Last + w;
                    }
                }

                while (pos <= until)
                {
                    if (len - pos < p.Need)
                    {
                        return false;
                    }

                    if (pos == len)
                    {
                        if (first is null)
                        {
                            scan.Last = pos;
                            scan.LastWidth = 1;
                            scan.LastChar = -1;
                            start = pos;
                            return true;
                        }

                        return false;
                    }

#if NET
                    if (first is not null)
                    {
                        int i = enc.IndexOfCandidate(input, pos, first, p.FirstIsSmall);
                        if (i < 0 || i > until)
                        {
                            return false;
                        }

                        pos = enc.AlignStart(input, i, units);
                        if (len - pos < p.Need || pos > until)
                        {
                            return false;
                        }

                        if (pos == len)
                        {
                            return false;
                        }

                        if (p.LeadingWordBoundary && pos > 0 && enc.Unit(input, pos - 1) < 0x80 && Utf8Encoding.IsWordUnit(enc.Unit(input, pos - 1)))
                        {
                            pos = EndOfRun<TUnit, TEncoding>(first, input, pos, units);
                            continue;
                        }
                    }
#endif
                    int c = enc.Unit(input, pos);
                    int w = 1;
                    if (c >= 0x80)
                    {
                        c = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);
                    }

                    if (first is null || first.Contains(c))
                    {
                        scan.Last = pos;
                        scan.LastChar = c;
                        scan.LastWidth = w;
                        start = pos;
                        return true;
                    }

                    pos += w;
                }

                return false;
            }
        }
    }

    /// <summary>Tries every start from a position to a last position, in order, with the first set's scan and the skipping of runs.</summary>
    private static bool ScanStarts<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, int pos, int until, out int from, out int to)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int len = input.Length;
        CharSet? first = p.First;
        bool units = p.Units;
        from = -1;
        to = -1;
        while (pos <= until)
        {
            if (len - pos < p.Need)
            {
                return false;
            }

            from = pos;
            if (pos == len)
            {
                return first is null && Run<TUnit, TEncoding>(p, st, input, pos, out to);
            }

#if NET
            if (first is not null)
            {
                // A start is a unit that can begin a member of the first set (looked at below). The scan for one
                // is vectorised.
                int i = enc.IndexOfCandidate(input, pos, first, p.FirstIsSmall);
                if (i < 0 || i > until)
                {
                    return false;
                }

                pos = enc.AlignStart(input, i, units);
                from = pos;
                if (len - pos < p.Need || pos > until)
                {
                    return false;
                }

                if (pos == len)
                {
                    return false;
                }

                if (p.LeadingWordBoundary && pos > 0 && enc.Unit(input, pos - 1) < 0x80 && Utf8Encoding.IsWordUnit(enc.Unit(input, pos - 1)))
                {
                    // Inside a word, where \b cannot hold: the rest of the word is passed over.
                    pos = EndOfRun<TUnit, TEncoding>(first, input, pos, units);
                    continue;
                }
            }
#endif
            int c = enc.Unit(input, pos);
            int w = 1;
            if (c >= 0x80)
            {
                c = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);
            }

            if ((first is null || first.Contains(c)) && Run<TUnit, TEncoding>(p, st, input, pos, out to))
            {
                return true;
            }

            if (st.Stop != MatchStop.None)
            {
                return false;
            }

            if (p.SkipRun && p.LoopSet!.Contains(c))
            {
                // The start failed after its leading loop ate the run from here: every start in the rest of the
                // run fails the same way. (A start the loop took nothing at begins no run.)
                int end = EndOfRun<TUnit, TEncoding>(p.LoopSet!, input, pos + w, units);
                pos = end > pos + w ? end : pos + w;
                continue;
            }

            pos += w;
        }

        return false;
    }

    /// <summary>
    /// The end of the run of members of a set that starts at a position: the first position at which the character
    /// is not in the set, or the end of the text. On .NET the ASCII part of the scan is vectorised; a character
    /// that is not ASCII is decoded and looked up unless the set holds every such character.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EndOfRun<TUnit, TEncoding>(CharSet set, ReadOnlySpan<TUnit> input, int at, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int len = input.Length;
#if NET
        bool all = set.HoldsAllNonAscii(units);
        while (at < len)
        {
            int i = all ? input.Slice(at).IndexOfAny(enc.NonMembers(set)) : input.Slice(at).IndexOfAnyExcept(enc.Members(set));
            if (i < 0)
            {
                return len;
            }

            at += i;
            int c = enc.Unit(input, at);
            if (c < 0x80)
            {
                return at;
            }

            int r = units ? enc.UnitAt(input, at, out int w) : enc.Decode(input, at, out w);
            if (!set.Contains(r))
            {
                return at;
            }

            at += w;
        }

        return at;
#else
        while (at < len)
        {
            int c = enc.Unit(input, at);
            if (c < 0x80)
            {
                if (!set.HasAscii(c))
                {
                    return at;
                }

                at++;
            }
            else
            {
                int r = units ? enc.UnitAt(input, at, out int w) : enc.Decode(input, at, out w);
                if (!set.Contains(r))
                {
                    return at;
                }

                at += w;
            }
        }

        return at;
#endif
    }

    /// <summary>
    /// The end of the run of ASCII members of a set that starts at a position and takes at most so many units: the
    /// first position within the bound at which the unit is not an ASCII member of the set, or the bound. On .NET
    /// the scan is vectorised; otherwise it is left to the caller, which goes on from the position itself.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="set">The set.</param>
    /// <param name="input">The text.</param>
    /// <param name="at">The position.</param>
    /// <param name="max">The most units.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns>The end of the run.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AsciiRun<TUnit, TEncoding>(CharSet set, ReadOnlySpan<TUnit> input, int at, int max, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
#if NET
        TEncoding enc = default;
        int len = input.Length;
        if (at >= len)
        {
            return at;
        }

        ReadOnlySpan<TUnit> window = input.Slice(at, Math.Min(max, len - at));
        int i = set.HoldsAllNonAscii(units) ? window.IndexOfAny(enc.NonMembers(set)) : window.IndexOfAnyExcept(enc.Members(set));
        if (i < 0)
        {
            i = window.Length;
        }

        // A unit beyond ASCII in the run is a character the caller decodes and counts.
        int beyond = typeof(TUnit) == typeof(byte)
            ? System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(window.Slice(0, i)).IndexOfAnyInRange((byte)0x80, (byte)0xFF)
            : System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(window.Slice(0, i)).IndexOfAnyInRange((char)0x80, '\uFFFF');
        return at + (beyond < 0 ? i : beyond);
#else
        return at;
#endif
    }

    /// <summary>
    /// The last position in a range at which the unit is an ASCII member of a set, or -1: where a loop gives back
    /// to, when what follows it can go on only from a member (vectorised on .NET).
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range.</param>
    /// <param name="set">The set, with no member beyond ASCII.</param>
    /// <returns>The position, or -1.</returns>
    public static int LastAllowedIn<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int from, int to, CharSet set)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
#if NET
        int i = input.Slice(from, to - from).LastIndexOfAny(enc.Members(set));
        return i < 0 ? -1 : from + i;
#else
        for (int i = to - 1; i >= from; i--)
        {
            int c = enc.Unit(input, i);
            if (c < 0x80 && set.HasAscii(c))
            {
                return i;
            }
        }

        return -1;
#endif
    }

    /// <summary>
    /// Whether the text a group captured is at a position, and how many units it takes there: compared unit by
    /// unit for a pattern with the u flag and no folding, else a character at a time, folding case when asked. A
    /// group that has not taken part matches the empty string.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position.</param>
    /// <param name="from">The start of the capture, or <see cref="MatchState.None"/>.</param>
    /// <param name="to">The end of the capture.</param>
    /// <param name="fold">Whether case is folded.</param>
    /// <param name="unicode">Whether the pattern has the u flag.</param>
    /// <param name="width">The units taken.</param>
    /// <returns>Whether it is.</returns>
    public static bool BackrefAt<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int pos, int from, int to, bool fold, bool unicode, out int width)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        width = 0;
        if (from == MatchState.None)
        {
            return true;
        }

        int len = input.Length;
        int n = to - from;
        if (unicode && !fold)
        {
            if (len - pos >= n && input.Slice(pos, n).SequenceEqual(input.Slice(from, n)))
            {
                width = n;
                return true;
            }

            return false;
        }

        bool units = !unicode;
        int at = pos;
        while (from < to)
        {
            int want = units ? enc.UnitAt(input, from, out int w) : enc.Decode(input, from, out w);
            from += w;
            if (at >= len)
            {
                return false;
            }

            int got = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);
            at += w;
            if (got != want && !(fold && CaseFolding.Canonicalize(got, unicode) == CaseFolding.Canonicalize(want, unicode)))
            {
                return false;
            }
        }

        width = at - pos;
        return true;
    }

    /// <summary>The units an enumeration's pass looks ahead for rare terminators or extra forms at a time.</summary>
    private const int CursorChunk = 1 << 16;

    /// <summary>Whether the state knows a range holds no extra form.</summary>
    private static bool ExtrasClear(MatchState? st, int pos, int limit) => st is not null && pos >= st.ExtraFrom && limit <= st.ExtraTo;

    /// <summary>Where a scan for extra forms starts: after what is known clear.</summary>
    private static int ExtrasFrom(MatchState? st, int pos) => st is not null && pos >= st.ExtraFrom && pos < st.ExtraTo ? st.ExtraTo : pos;

    /// <summary>Where a scan for extra forms ends: beyond what this match needs, so that later matches need none.</summary>
    private static int ExtrasTo(MatchState? st, int pos, int limit, int len) => st is null ? limit : Math.Max(limit, Math.Min(len, pos + CursorChunk));

    /// <summary>Records what a scan for extra forms found, and gives the position within the range asked for, or -1.</summary>
    private static int ExtrasSeen(MatchState? st, int pos, int scanTo, int e, int limit)
    {
        if (st is not null)
        {
            int clearTo = e < 0 ? scanTo : e;
            if (pos >= st.ExtraFrom && pos <= st.ExtraTo)
            {
                st.ExtraTo = Math.Max(st.ExtraTo, clearTo);
            }
            else
            {
                st.ExtraFrom = pos;
                st.ExtraTo = clearTo;
            }
        }

        return e >= limit ? -1 : e;
    }

    /// <summary>
    /// The end of the run of characters other than line terminators (<c>.</c> without the s flag) that starts at
    /// a position: the common terminators (LF, CR) are searched for by vector, and the rare ones (U+2028, U+2029)
    /// in passes of a chunk at a time whose result the state keeps, so that an enumeration scans the text for
    /// them once rather than at every run.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="st">The state, or null.</param>
    /// <param name="input">The text.</param>
    /// <param name="at">The position.</param>
    /// <returns>The end of the run.</returns>
    public static int EndOfLine<TUnit, TEncoding>(MatchState? st, ReadOnlySpan<TUnit> input, int at)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        int len = input.Length;
        if (at < len)
        {
            // A run that is empty (the position is at a terminator, as two of every three matches of .* are) is
            // answered without a vector call.
            int first = typeof(TUnit) == typeof(byte) ? MemoryMarshal.Cast<TUnit, byte>(input)[at] : MemoryMarshal.Cast<TUnit, char>(input)[at];
            if (first == 0x0A || first == 0x0D)
            {
                return at;
            }
        }

        while (at < len)
        {
            int to;
            if (st is not null && at >= st.RareFrom && at < st.RareTo)
            {
                to = st.RareTo;
            }
            else
            {
                int chunkEnd = Math.Min(len, at + CursorChunk);
                int r = IndexOfRareTerminator<TUnit>(input, at, chunkEnd);
                to = r < 0 ? chunkEnd : r;
                if (st is not null)
                {
                    if (at >= st.RareFrom && at <= st.RareTo)
                    {
                        st.RareTo = Math.Max(st.RareTo, to);
                    }
                    else
                    {
                        st.RareFrom = at;
                        st.RareTo = to;
                    }
                }

                if (r == at)
                {
                    return at;
                }
            }

            int i = typeof(TUnit) == typeof(byte)
                ? MemoryMarshal.Cast<TUnit, byte>(input.Slice(at, to - at)).IndexOfAny((byte)0x0A, (byte)0x0D)
                : MemoryMarshal.Cast<TUnit, char>(input.Slice(at, to - at)).IndexOfAny('\n', '\r');
            if (i >= 0)
            {
                return at + i;
            }

            at = to;
            if (at < len && IndexOfRareTerminator<TUnit>(input, at, Math.Min(len, at + 3)) == at)
            {
                return at;
            }
        }

        return len;
    }

    /// <summary>The first U+2028 or U+2029 in a range, or -1 (over UTF-8, the bytes E2 80 A8 and E2 80 A9).</summary>
    private static int IndexOfRareTerminator<TUnit>(ReadOnlySpan<TUnit> input, int from, int to)
        where TUnit : unmanaged, IEquatable<TUnit>
    {
        if (typeof(TUnit) == typeof(byte))
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.Cast<TUnit, byte>(input);
            int at = from;
            while (at < to)
            {
                int i = bytes.Slice(at, to - at).IndexOf((byte)0xE2);
                if (i < 0)
                {
                    return -1;
                }

                at += i;
                if (at + 2 < bytes.Length && bytes[at + 1] == 0x80 && (bytes[at + 2] == 0xA8 || bytes[at + 2] == 0xA9))
                {
                    return at;
                }

                at++;
            }

            return -1;
        }

        int k = MemoryMarshal.Cast<TUnit, char>(input.Slice(from, to - from)).IndexOfAny('\u2028', '\u2029');
        return k < 0 ? -1 : from + k;
    }

    /// <summary>The end of the run of one ASCII character that starts at a position.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="c">The character.</param>
    /// <param name="input">The text.</param>
    /// <param name="at">The position.</param>
    /// <returns>The end of the run.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EndOfRun<TUnit, TEncoding>(int c, ReadOnlySpan<TUnit> input, int at)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
#if NET
        int i = input.Slice(at).IndexOfAnyExcept(enc.AsciiUnit(c));
        return i < 0 ? input.Length : at + i;
#else
        while (at < input.Length && enc.Unit(input, at) == c)
        {
            at++;
        }

        return at;
#endif
    }

    /// <summary>The bit of the memory for the memo point <paramref name="m"/> at a position, with the state of the loops around the point: for each, whether it would refuse an iteration that ended here as empty.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MemoIndex(Program p, MatchState st, int m, int pos)
    {
        MemoPoint pt = p.Points[m - 1];
        int idx = (((m - 1) * st.Width) + pos) << Compiler.MaxMemoLoops;
        int[] counts = st.Counts;
        if (pt.Loop0 >= 0 && counts[2 * pt.Loop0] > pt.Min0 && counts[(2 * pt.Loop0) + 1] == pos)
        {
            idx |= 1;
        }

        if (pt.Loop1 >= 0 && counts[2 * pt.Loop1] > pt.Min1 && counts[(2 * pt.Loop1) + 1] == pos)
        {
            idx |= 2;
        }

        return idx;
    }

    /// <summary>Whether a bit of the memory of failed places is set.</summary>
    /// <param name="memo">The memory.</param>
    /// <param name="idx">The bit.</param>
    /// <returns>Whether it is set.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MemoBit(ulong[] memo, int idx) => ((memo[idx >> 6] >> (idx & 63)) & 1) != 0;

    /// <summary>Sets a bit of the memory of failed places.</summary>
    /// <param name="memo">The memory.</param>
    /// <param name="idx">The bit.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetMemoBit(ulong[] memo, int idx) => memo[idx >> 6] |= 1UL << (idx & 63);

    /// <summary>Pushes an entry on the stack, growing it when full.</summary>
    /// <param name="stack">The stack.</param>
    /// <param name="sp">Its depth.</param>
    /// <param name="kind">The kind of the entry.</param>
    /// <param name="pc">The instruction, group or loop.</param>
    /// <param name="pos">The position.</param>
    /// <param name="a">The other value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Push(ref Entry[] stack, ref int sp, byte kind, int pc, int pos, int a)
    {
        if (sp == stack.Length)
        {
            Array.Resize(ref stack, stack.Length * 2);
        }

        stack[sp++] = new Entry(kind, pc, pos, a);
    }

    /// <summary>The width of the character at the position <paramref name="at"/> when the instruction's character or class matches it, and 0 otherwise. The character is a code unit with <paramref name="units"/> set.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="i">The loop instruction.</param>
    /// <param name="input">The text.</param>
    /// <param name="at">The position.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns>The width, or 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int One<TUnit, TEncoding>(in Inst i, ReadOnlySpan<TUnit> input, int at, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (at >= input.Length)
        {
            return 0;
        }

        TEncoding enc = default;
        int r = enc.Unit(input, at);
        int w = 1;
        if (r >= 0x80)
        {
            r = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);
        }

        if (i.Set is not null ? !i.Set.Contains(r) : r != i.R)
        {
            return 0;
        }

        return w;
    }

    /// <summary>
    /// The width of the last character before a position, to give it back. The characters of a run were decoded
    /// going forward, and decoding the last of them going backward finds the same boundary.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns>The width.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GiveBack<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int pos, bool units)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        if (enc.Unit(input, pos - 1) < 0x80)
        {
            return 1;
        }

        int w;
        if (units)
        {
            enc.UnitBefore(input, pos, out w);
        }
        else
        {
            enc.DecodeBefore(input, pos, out w);
        }

        return w;
    }

    /// <summary>Whether the code point is a word character of a case-insensitive word boundary under the u flag: a word character, the long s or the Kelvin sign.</summary>
    /// <param name="r">The character.</param>
    /// <returns>Whether it is.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFoldWordRune(int r) => (r < 128 && Utf8Encoding.IsWordUnit(r)) || r == 0x17F || r == 0x212A;

    /// <summary>
    /// Reports whether the program matches at the position <paramref name="start"/>. It keeps its choices on an
    /// explicit stack, so the depth of the search is bounded by memory and not by the stack of the thread. When it
    /// fails, every group and counter is back to the value it had on entry. It counts its steps, and when a check
    /// says the match has reached a limit it returns false with the reason in the state.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns>Whether the program matched at the position.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Run<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, int start, out int end)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        GeneratedRunner? runner = p.Runner;
        if (runner is not null)
        {
            return typeof(TUnit) == typeof(byte)
                ? runner.Run(p, st, System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(input), start, out end)
                : runner.Run(p, st, System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(input), start, out end);
        }

        return Interpret<TUnit, TEncoding>(p, st, input, start, out end);
    }

    /// <summary>The interpreter: runs the program's instructions from a position.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns>Whether the program matched at the position.</returns>
    public static bool Interpret<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, int start, out int end)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        end = -1;
        TEncoding enc = default;
        Inst[] insts = p.Insts;
        Quick[] quick = p.Quick;
        Entry[] stack = st.Stack;
        int[] caps = st.Caps;
        int[] tmp = st.Tmp;
        int[] counts = st.Counts;
        int sp = 0;

        // The text is matched by UTF-16 code unit (see the encodings).
        bool units = p.Units;
        int len = input.Length;
        int pc = 0;
        int pos = start;
        long steps = st.Steps;

        // Whether the match has taken the memory of failed places into use.
        bool memo = st.MemoOn;
        ulong[] memoBits = st.Memo;
        while (true)
        {
            steps--;
            if (steps < 0)
            {
                st.Stack = stack;
                if (!st.Check(sp))
                {
                    st.Steps = 0;
                    return false;
                }

                steps = CheckEvery;
                memo = st.MemoOn;
                memoBits = st.Memo;
            }

            ref Inst i = ref insts[pc];
            switch (i.Op)
            {
                case Op.Char:
                {
                    if (pos < len)
                    {
                        int c = enc.Unit(input, pos);
                        if (c < 0x80)
                        {
                            if (c == i.R)
                            {
                                pos++;
                                pc++;
                                continue;
                            }
                        }
                        else
                        {
                            int r = units ? enc.UnitAt(input, pos, out int w) : enc.Decode(input, pos, out w);
                            if (r == i.R)
                            {
                                pos += w;
                                pc++;
                                continue;
                            }
                        }
                    }

                    break;
                }

                case Op.Set:
                {
                    if (pos < len)
                    {
                        CharSet set = i.Set!;
                        int c = enc.Unit(input, pos);
                        if (c < 0x80)
                        {
                            if (set.HasAscii(c))
                            {
                                pos++;
                                pc++;
                                continue;
                            }
                        }
                        else
                        {
                            int r = units ? enc.UnitAt(input, pos, out int w) : enc.Decode(input, pos, out w);
                            if (set.Contains(r))
                            {
                                pos += w;
                                pc++;
                                continue;
                            }
                        }
                    }

                    break;
                }

                case Op.Lit:
                {
                    if (enc.LiteralAt(input, pos, p, i.Lit, out int w))
                    {
                        pos += w;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.CharBack:
                case Op.SetBack:
                {
                    if (pos > 0)
                    {
                        int r = enc.Unit(input, pos - 1);
                        int w = 1;
                        if (r >= 0x80)
                        {
                            r = units ? enc.UnitBefore(input, pos, out w) : enc.DecodeBefore(input, pos, out w);
                        }

                        if (i.Op == Op.CharBack ? r == i.R : i.Set!.Contains(r))
                        {
                            pos -= w;
                            pc++;
                            continue;
                        }
                    }

                    break;
                }

                case Op.Fail:
                    break;
                case Op.Bol:
                    if (pos == 0)
                    {
                        pc++;
                        continue;
                    }

                    break;
                case Op.Eol:
                    if (pos == len)
                    {
                        pc++;
                        continue;
                    }

                    break;
                case Op.WordBoundary:
                case Op.NotWordBoundary:
                {
                    bool before = pos > 0 && Utf8Encoding.IsWordUnit(enc.Unit(input, pos - 1));
                    bool after = pos < len && Utf8Encoding.IsWordUnit(enc.Unit(input, pos));
                    if ((before != after) == (i.Op == Op.WordBoundary))
                    {
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.LineStart:
                    if (pos == 0 || enc.IsLineTerminatorBefore(input, pos))
                    {
                        pc++;
                        continue;
                    }

                    break;
                case Op.LineEnd:
                    if (pos == len || enc.IsLineTerminatorAt(input, pos))
                    {
                        pc++;
                        continue;
                    }

                    break;
                case Op.WordBoundaryFold:
                case Op.NotWordBoundaryFold:
                {
                    bool before = pos > 0 && IsFoldWordRune(enc.DecodeBefore(input, pos, out _));
                    bool after = pos < len && IsFoldWordRune(enc.Decode(input, pos, out _));
                    if ((before != after) == (i.Op == Op.WordBoundaryFold))
                    {
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.Jmp:
                    pc = i.X;
                    continue;
                case Op.Split:
                {
                    // A place from which every way has failed before fails at once. The mark is under whatever
                    // comes of this place, a choice or not, so that the place is remembered when it has all failed.
                    if (memo && i.M != 0)
                    {
                        int idx = MemoIndex(p, st, i.M, pos);
                        if (MemoBit(memoBits, idx))
                        {
                            break;
                        }

                        if (sp < MaxMarkDepth)
                        {
                            Push(ref stack, ref sp, Entry.Mark, 0, idx, 0);
                        }
                    }

                    // A way the next character rules out is not taken, and no choice is kept for it.
                    if (!quick[i.Y].Allows<TUnit, TEncoding>(input, pos))
                    {
                        if (!quick[i.X].Allows<TUnit, TEncoding>(input, pos))
                        {
                            break;
                        }

                        pc = i.X;
                        continue;
                    }

                    if (!quick[i.X].Allows<TUnit, TEncoding>(input, pos))
                    {
                        pc = i.Y;
                        continue;
                    }

                    Push(ref stack, ref sp, Entry.Choice, i.Y, pos, 0);
                    pc = i.X;
                    continue;
                }

                case Op.SaveTmp:
                {
                    int a = i.A;
                    Push(ref stack, ref sp, Entry.Tmp, a, tmp[a], 0);
                    tmp[a] = pos;
                    pc++;
                    continue;
                }

                case Op.Capture:
                {
                    int a = i.A;
                    Push(ref stack, ref sp, Entry.Cap, a, caps[2 * a], caps[(2 * a) + 1]);
                    if (i.Flag)
                    {
                        caps[2 * a] = pos;
                        caps[(2 * a) + 1] = tmp[a];
                    }
                    else
                    {
                        caps[2 * a] = tmp[a];
                        caps[(2 * a) + 1] = pos;
                    }

                    pc++;
                    continue;
                }

                case Op.Clear:
                {
                    steps -= i.B - i.A;
                    for (int g = i.A; g <= i.B; g++)
                    {
                        if (caps[2 * g] != MatchState.None)
                        {
                            Push(ref stack, ref sp, Entry.Cap, g, caps[2 * g], caps[(2 * g) + 1]);
                            caps[2 * g] = MatchState.None;
                            caps[(2 * g) + 1] = MatchState.None;
                        }
                    }

                    pc++;
                    continue;
                }

                case Op.Backref:
                {
                    // A group that has not taken part matches the empty string.
                    int a = i.A;
                    int from = caps[2 * a];
                    int to = caps[(2 * a) + 1];
                    if (from == MatchState.None)
                    {
                        pc++;
                        continue;
                    }

                    int n = to - from;
                    if (len - pos >= n && input.Slice(pos, n).SequenceEqual(input.Slice(from, n)))
                    {
                        steps -= n >> 3;
                        pos += n;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.BackrefBack:
                {
                    int a = i.A;
                    int from = caps[2 * a];
                    int to = caps[(2 * a) + 1];
                    if (from == MatchState.None)
                    {
                        pc++;
                        continue;
                    }

                    int n = to - from;
                    if (pos >= n && input.Slice(pos - n, n).SequenceEqual(input.Slice(from, n)))
                    {
                        steps -= n >> 3;
                        pos -= n;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.BackrefFold:
                case Op.BackrefUnits:
                {
                    // A character at a time: to fold case, or because a group matched by code unit can begin or end
                    // between the halves of a character, where its units do not say what it holds.
                    int a = i.A;
                    int from = caps[2 * a];
                    int to = caps[(2 * a) + 1];
                    if (from == MatchState.None)
                    {
                        pc++;
                        continue;
                    }

                    steps -= (to - from) >> 3;
                    int at = pos;
                    bool ok = true;
                    bool fold = i.Op == Op.BackrefFold;
                    while (from < to && ok)
                    {
                        int want = units ? enc.UnitAt(input, from, out int w) : enc.Decode(input, from, out w);
                        from += w;
                        if (at >= len)
                        {
                            ok = false;
                            break;
                        }

                        int got = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);
                        at += w;
                        ok = got == want || (fold && CaseFolding.Canonicalize(got, i.Flag) == CaseFolding.Canonicalize(want, i.Flag));
                    }

                    if (ok)
                    {
                        pos = at;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.BackrefFoldBack:
                case Op.BackrefUnitsBack:
                {
                    int a = i.A;
                    int from = caps[2 * a];
                    int to = caps[(2 * a) + 1];
                    if (from == MatchState.None)
                    {
                        pc++;
                        continue;
                    }

                    steps -= (to - from) >> 3;
                    int at = pos;
                    bool ok = true;
                    bool fold = i.Op == Op.BackrefFoldBack;
                    while (from < to && ok)
                    {
                        int want = units ? enc.UnitBefore(input, to, out int w) : enc.DecodeBefore(input, to, out w);
                        to -= w;
                        if (at == 0)
                        {
                            ok = false;
                            break;
                        }

                        int got = units ? enc.UnitBefore(input, at, out w) : enc.DecodeBefore(input, at, out w);
                        at -= w;
                        ok = got == want || (fold && CaseFolding.Canonicalize(got, i.Flag) == CaseFolding.Canonicalize(want, i.Flag));
                    }

                    if (ok)
                    {
                        pos = at;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.Look:
                {
                    // What follows a lookaround starts where the lookaround does, and so does the body of a
                    // lookahead.
                    if (quick[pc].Allows<TUnit, TEncoding>(input, pos))
                    {
                        Push(ref stack, ref sp, i.Flag ? Entry.LookNeg : Entry.Look, i.X, pos, 0);
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.LookOk:
                {
                    // The body matched. A lookaround is atomic, so the choices made inside it are dropped. The undo
                    // entries stay, because the groups it set remain set until something below this point is
                    // retried.
                    int f = sp - 1;
                    while (stack[f].Kind != Entry.Look)
                    {
                        f--;
                    }

                    pos = stack[f].Pos;
                    int keep = f;
                    for (int k = f + 1; k < sp; k++)
                    {
                        if (stack[k].Kind >= Entry.Tmp)
                        {
                            stack[keep++] = stack[k];
                        }
                    }

                    sp = keep;
                    pc++;
                    continue;
                }

                case Op.LookFail:
                {
                    // The body of a negative lookaround matched, so the lookaround fails and everything it did is
                    // undone.
                    while (sp > 0)
                    {
                        Entry e = stack[--sp];
                        if (e.Kind == Entry.LookNeg)
                        {
                            break;
                        }

                        st.Undo(in e);
                    }

                    break;
                }

                case Op.Rep:
                {
                    int at = pos;
                    int n = 0;
                    int floor = pos;
                    CharSet? set = i.Set;
                    int max = i.Max;
                    int min = i.Min;
                    if (max < 0 && min <= 1 && (set is not null || i.R < 0x80))
                    {
                        // No bound to count up to: the run is found by a scan. With a least count of one the first
                        // character is matched on its own, so the floor is after it.
                        if (min == 1)
                        {
                            int w = One<TUnit, TEncoding>(in i, input, at, units);
                            if (w == 0)
                            {
                                break;
                            }

                            at += w;
                            floor = at;
                        }

                        // A run of one character (a space between words) is not worth a vector call: the next
                        // unit is looked at first.
                        if (at < len && (set is not null ? (enc.Unit(input, at) < 0x80 ? set.HasAscii(enc.Unit(input, at)) : set.HasNonAscii) : enc.Unit(input, at) == i.R))
                        {
                            at = set is not null ? (set.IsLineRest ? EndOfLine<TUnit, TEncoding>(st, input, at) : EndOfRun<TUnit, TEncoding>(set, input, at, units)) : EndOfRun<TUnit, TEncoding>(i.R, input, at);
                        }

                        n = min;
                    }
                    else if (set is not null)
                    {
                        if (max >= 12)
                        {
                            // The ASCII part of the run, within the bound, is found by vector (two calls, which
                            // a loop of fewer than twelve characters beats); the loop goes on from where that stops.
                            at = AsciiRun<TUnit, TEncoding>(set, input, at, max, units);
                            n = at - pos;
                            if (n >= min)
                            {
                                floor = pos + min;
                            }
                        }

                        while (n != max && at < len)
                        {
                            int c = enc.Unit(input, at);
                            if (c < 0x80)
                            {
                                if (!set.HasAscii(c))
                                {
                                    break;
                                }

                                at++;
                            }
                            else
                            {
                                int r = units ? enc.UnitAt(input, at, out int w) : enc.Decode(input, at, out w);
                                if (!set.Contains(r))
                                {
                                    break;
                                }

                                at += w;
                            }

                            n++;
                            if (n == min)
                            {
                                floor = at;
                            }
                        }
                    }
                    else
                    {
                        int want = i.R;
                        while (n != max && at < len)
                        {
                            int r = enc.Unit(input, at);
                            int w = 1;
                            if (r >= 0x80)
                            {
                                r = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);
                            }

                            if (r != want)
                            {
                                break;
                            }

                            at += w;
                            n++;
                            if (n == min)
                            {
                                floor = at;
                            }
                        }
                    }

                    steps -= (at - pos) >> 3;
                    if (n >= min)
                    {
                        if (i.Atomic)
                        {
                            // Nothing that follows can take a character of the set, so no give-back can succeed.
                            pos = at;
                            pc++;
                            continue;
                        }

                        if (memo && i.M != 0)
                        {
                            int idx = MemoIndex(p, st, i.M, at);
                            if (MemoBit(memoBits, idx))
                            {
                                // What follows the loop has failed from here before. The loop gives a character
                                // back.
                                if (at > floor)
                                {
                                    Push(ref stack, ref sp, Entry.Rep, pc + 1, at, floor);
                                }

                                break;
                            }

                            if (at == floor && sp < MaxMarkDepth)
                            {
                                Push(ref stack, ref sp, Entry.Mark, 0, idx, 0);
                            }
                        }

                        if (at > floor)
                        {
                            Push(ref stack, ref sp, Entry.Rep, pc + 1, at, floor);
                        }

                        pos = at;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.RepLazy:
                {
                    int at = pos;
                    int n = 0;
                    while (n < i.Min)
                    {
                        int w = One<TUnit, TEncoding>(in i, input, at, units);
                        if (w == 0)
                        {
                            break;
                        }

                        at += w;
                        n++;
                    }

                    steps -= n >> 3;
                    if (n == i.Min)
                    {
                        if (memo && i.M != 0)
                        {
                            int idx = MemoIndex(p, st, i.M, at);
                            if (MemoBit(memoBits, idx))
                            {
                                // What follows the loop has failed from here before. The loop takes one more
                                // character.
                                if (n != i.Max)
                                {
                                    Push(ref stack, ref sp, Entry.RepLazy, pc, at, n);
                                }

                                break;
                            }

                            if (n == i.Max && sp < MaxMarkDepth)
                            {
                                Push(ref stack, ref sp, Entry.Mark, 0, idx, 0);
                            }
                        }

                        if (n != i.Max)
                        {
                            Push(ref stack, ref sp, Entry.RepLazy, pc, at, n);
                        }

                        pos = at;
                        pc++;
                        continue;
                    }

                    break;
                }

                case Op.LoopInit:
                {
                    int a = i.A;
                    Push(ref stack, ref sp, Entry.Counter, a, counts[2 * a], counts[(2 * a) + 1]);
                    counts[2 * a] = 0;
                    counts[(2 * a) + 1] = MatchState.None;
                    pc++;
                    continue;
                }

                case Op.Loop:
                {
                    int a = i.A;
                    long n = counts[2 * a];
                    long min = i.Min;
                    long need = i.Need;
                    long left = len - pos;

                    // Fewer characters are left than any way from here to a match consumes: what follows the loop,
                    // and the iterations it has still to make.
                    if (left < need || (n < min && i.Iter != 0 && left - need < (min - n) * i.Iter))
                    {
                        break;
                    }

                    // Once the minimum is met, ECMA-262 rejects an iteration that consumed nothing. That is what
                    // stops a loop over a body that can be empty.
                    if (n > min && pos == counts[(2 * a) + 1])
                    {
                        break;
                    }

                    if (n < min)
                    {
                        pc++;
                        continue;
                    }

                    if (i.Max >= 0 && n >= i.Max)
                    {
                        pc = i.X;
                        continue;
                    }

                    // The loop may be left or entered from here. A place from which both have failed before fails
                    // at once.
                    if (memo && i.M != 0)
                    {
                        int idx = MemoIndex(p, st, i.M, pos);
                        if (MemoBit(memoBits, idx))
                        {
                            break;
                        }

                        if (sp < MaxMarkDepth)
                        {
                            Push(ref stack, ref sp, Entry.Mark, 0, idx, 0);
                        }
                    }

                    if (!quick[i.X].Allows<TUnit, TEncoding>(input, pos))
                    {
                        // The next character rules out leaving the loop, so there is no choice.
                        pc++;
                    }
                    else if (!quick[pc + 1].Allows<TUnit, TEncoding>(input, pos))
                    {
                        pc = i.X;
                    }
                    else if (i.Flag)
                    {
                        Push(ref stack, ref sp, Entry.Choice, pc + 1, pos, 0);
                        pc = i.X;
                    }
                    else
                    {
                        Push(ref stack, ref sp, Entry.Choice, i.X, pos, 0);
                        pc++;
                    }

                    continue;
                }

                case Op.LoopIter:
                {
                    int a = i.A;
                    Push(ref stack, ref sp, Entry.Counter, a, counts[2 * a], counts[(2 * a) + 1]);
                    counts[2 * a]++;
                    counts[(2 * a) + 1] = pos;
                    pc++;
                    continue;
                }

                case Op.Match:
                    st.Steps = steps;
                    st.Stack = stack;
                    end = pos;
                    return true;
                default:
                    throw new InvalidOperationException("Unknown instruction " + i.Op);
            }

            // The instruction failed. Take up the most recent choice, undoing what was recorded since.
            if (!Backtrack<TUnit, TEncoding>(p, st, input, ref stack, ref sp, ref pc, ref pos, ref steps, memo, memoBits))
            {
                st.Steps = steps;
                st.Stack = stack;
                return false;
            }
        }
    }

    /// <summary>
    /// Takes up the most recent choice after an instruction failed, undoing what was recorded since: the shared
    /// backtracking of the interpreter and of generated code.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="stack">The stack of choices.</param>
    /// <param name="sp">Its depth.</param>
    /// <param name="pc">Receives the instruction to resume at.</param>
    /// <param name="pos">Receives the position to resume at.</param>
    /// <param name="steps">The steps left before the next check.</param>
    /// <param name="memo">Whether the memory of failed places is in use.</param>
    /// <param name="memoBits">The memory of failed places.</param>
    /// <returns><see langword="true"/> to resume at <paramref name="pc"/> and <paramref name="pos"/>; <see langword="false"/> when no choice is left and the match fails.</returns>
    public static bool Backtrack<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, ref Entry[] stack, ref int sp, ref int pc, ref int pos, ref long steps, bool memo, ulong[] memoBits)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
#if NET
        TEncoding enc = default;
#endif
        Inst[] insts = p.Insts;
        Quick[] quick = p.Quick;
        bool units = p.Units;
        while (true)
        {
            if (sp == 0)
            {
                return false;
            }

            int top = sp - 1;
            Entry e = stack[top];
            bool resumed = false;
            switch (e.Kind)
            {
                case Entry.Choice:
                    pc = e.Pc;
                    pos = e.Pos;
                    sp--;
                    resumed = true;
                    break;
                case Entry.Rep:
                {
                    // What followed the loop from the position it stood at has failed.
                    ref Inst rep = ref insts[e.Pc - 1];
                    bool marked = memo && rep.M != 0;
                    if (marked)
                    {
                        SetMemoBit(memoBits, MemoIndex(p, st, rep.M, e.Pos));
                    }

                    int at;
#if NET
                    if (!marked && e.Pos - e.A >= 16 && quick[e.Pc].AsciiOnly)
                    {
                        // What follows can go on only from a character below 128: the last such position in
                        // the run is found by a vectorised search, and the positions passed over are the ones
                        // the check below would pass over one by one. None of them is marked, since the loop
                        // has no memo point.
                        int j = enc.LastAllowed(input, e.A, e.Pos, ref quick[e.Pc]);
                        if (j < 0)
                        {
                            sp--;
                            break;
                        }

                        at = j;
                    }
                    else
                    {
                        at = e.Pos - GiveBack<TUnit, TEncoding>(input, e.Pos, units);
                    }
#else
                    at = e.Pos - GiveBack<TUnit, TEncoding>(input, e.Pos, units);
#endif
                    stack[top].Pos = at;
                    pc = e.Pc;
                    pos = at;
                    bool last = at <= e.A;

                    // A position from which what follows the loop cannot go on is passed over, and so is one
                    // from which it has failed before.
                    bool pass = !quick[pc].Allows<TUnit, TEncoding>(input, pos);
                    if (marked && !pass)
                    {
                        int idx = MemoIndex(p, st, rep.M, pos);
                        if (MemoBit(memoBits, idx))
                        {
                            pass = true;
                        }
                        else if (last)
                        {
                            // The loop has nothing more to give back. The entry stays to note this last way.
                            stack[top] = new Entry(Entry.Mark, 0, idx, 0);
                            resumed = true;
                            break;
                        }
                    }

                    if (last)
                    {
                        sp--;
                    }

                    if (pass)
                    {
                        steps--;
                        break;
                    }

                    resumed = true;
                    break;
                }

                case Entry.RepLazy:
                {
                    // What followed the loop from the position it stood at has failed.
                    ref Inst rep = ref insts[e.Pc];
                    bool marked = memo && rep.M != 0;
                    if (marked)
                    {
                        SetMemoBit(memoBits, MemoIndex(p, st, rep.M, e.Pos));
                    }

                    int w = One<TUnit, TEncoding>(in rep, input, e.Pos, units);
                    if (w == 0)
                    {
                        sp--;
                        break;
                    }

                    int at = e.Pos + w;
                    int taken = e.A + 1;
                    stack[top].Pos = at;
                    stack[top].A = taken;
                    pc = e.Pc + 1;
                    pos = at;
                    bool last = taken == rep.Max;
                    if (marked)
                    {
                        int idx = MemoIndex(p, st, rep.M, pos);
                        if (MemoBit(memoBits, idx))
                        {
                            if (last)
                            {
                                sp--;
                            }

                            steps--;
                            break;
                        }

                        if (last)
                        {
                            stack[top] = new Entry(Entry.Mark, 0, idx, 0);
                            resumed = true;
                            break;
                        }
                    }

                    if (last)
                    {
                        sp--;
                    }

                    resumed = true;
                    break;
                }

                case Entry.Mark:
                    // Every way from a memo point has failed.
                    SetMemoBit(memoBits, e.Pos);
                    sp--;
                    break;
                case Entry.Look:
                    // The body of a positive lookaround has no way left to match.
                    sp--;
                    break;
                case Entry.LookNeg:
                    // The body of a negative lookaround has no way to match, so the lookaround holds.
                    pc = e.Pc;
                    pos = e.Pos;
                    sp--;
                    resumed = true;
                    break;
                default:
                    st.Undo(in e);
                    sp--;
                    break;
            }

            if (resumed)
            {
                return true;
            }
        }
    }
}
