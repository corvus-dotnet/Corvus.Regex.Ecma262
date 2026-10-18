// <copyright file="Matcher.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of vm.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of vm.go).

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Why a match was abandoned. Neither is an answer.</summary>
internal enum MatchStop : byte
{
    /// <summary>The match was not abandoned.</summary>
    None,

    /// <summary>The match ran for longer than its limit.</summary>
    Time,

    /// <summary>The match needed more backtracking memory than its limit: its stack reached <see cref="Matcher.MaxStack"/> entries.</summary>
    Memory,
}

/// <summary>One entry of the backtracking stack. A choice entry says where to resume. An undo entry restores a value that was overwritten after the entries below it were pushed.</summary>
internal struct Entry
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
/// The working memory of one match. Each thread keeps one and every match on the thread uses it, so a match
/// allocates only while the stack is still growing to the depth the pattern and the text need.
/// </summary>
internal sealed class MatchState
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
internal static class Matcher
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
    private const int MaxMarkDepth = MaxStack / 2;

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
            return Simple<TUnit, TEncoding>(p, input, start, sticky, out _, out _);
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
            bool found = Simple<TUnit, TEncoding>(p, input, start, sticky, out captures[0], out captures[1]);
            return found;
        }

        MatchState st = MatchState.Acquire();
        try
        {
            Prepare(st, p, input.Length, limit);
            bool ok = Search<TUnit, TEncoding>(p, st, input, start, sticky, out int from, out int to);
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
        finally
        {
            st.Release();
        }
    }

    /// <summary>
    /// The search for a program of a simple shape (see <see cref="SimpleShape"/>): the match is where the search
    /// finds the literal or the member, and nothing of the matcher runs.
    /// </summary>
    public static bool Simple<TUnit, TEncoding>(Program p, ReadOnlySpan<TUnit> input, int start, bool sticky, out int from, out int to)
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
                    to = pos < len ? EndOfRun<TUnit, TEncoding>(set, input, pos, units) : pos;
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
                        to = EndOfRun<TUnit, TEncoding>(set, input, pos + w, units);
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
                        int i = p.Shape == SimpleShape.Sequence ? enc.IndexOfCaseSequence(input, pos, p) : -2;
                        if (i == -2)
                        {
                            i = enc.IndexOfCandidate(input, pos, first, p.FirstIsSmall);
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
                // The ordinal search ignoring case, and where a letter also folds to a character beyond ASCII (the
                // long s, the Kelvin sign) a check of every start that could reach one, as the general search does.
                int n = p.CasePrefixUtf16.Length;
                CharSet? extras = p.CaseExtras;
                bool units = p.Units;
                int pos = start;
                while (true)
                {
                    int i = enc.IndexOfCasePrefix(input, pos, p);
                    int e = -1;
                    if (extras is not null)
                    {
                        int limit = i < 0 ? len : Math.Min(len, i + n);
                        e = enc.IndexOfCandidate(input.Slice(0, limit), pos, extras, true);
                    }

                    if (i < 0 && e < 0)
                    {
                        return false;
                    }

                    if (e >= 0 && (i < 0 || e - n < i))
                    {
                        int s = Math.Max(pos, e - n + 1);
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

    /// <summary>The end of the match of a <see cref="SimpleShape.LiteralAlternation"/> program at a position, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlternativeEnd<TUnit, TEncoding>(Program p, ReadOnlySpan<TUnit> input, int pos)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int n = enc.AlternativeAt(input, pos, p);
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

        for (int k = 0; k < 2 * p.Groups; k++)
        {
            st.Caps[k] = MatchState.None;
        }

        Array.Clear(st.Tmp, 0, p.Groups);
        if (st.Counts.Length < 2 * p.Loops)
        {
            st.Counts = new int[2 * p.Loops];
        }

        Array.Clear(st.Counts, 0, 2 * p.Loops);
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
        if (p.CasePrefixUtf16.Length != 0)
        {
            // Every match starts with the literal ignoring case: the comparison is vectorised. A character beyond
            // ASCII that folds to a letter of the literal (the long s, the Kelvin sign) is looked for apart, and
            // every start from which the literal could reach it is tried, in order.
            int n = p.CasePrefixUtf16.Length;
            CharSet? extras = p.CaseExtras;

            while (true)
            {
                int i = enc.IndexOfCasePrefix(input, pos, p);

                // An extra character matters only where a start before the ordinal candidate could reach it, so
                // it is looked for up to the end of that candidate's prefix, or to the end of the text when there
                // is no candidate.
                int e = -1;
                if (extras is not null)
                {
                    int limit = i < 0 ? len : Math.Min(len, i + n);
                    e = enc.IndexOfCandidate(input.Slice(0, limit), pos, extras, true);
                }

                if (i < 0 && e < 0)
                {
                    return false;
                }

                if (e >= 0 && (i < 0 || e - n < i))
                {
                    int s = Math.Max(pos, e - n + 1);
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

        if (p.AfterLoopUtf16.Length != 0)
        {
            // Every match is a run of the loop's set then the literal: the literal is searched for, and the run
            // before it is walked back to the earliest start. A failed start fails for every later start of the
            // same run (the loop ends at the same place), so the next literal occurrence is searched for next.
            CharSet loopSet = p.LoopSet!;
            while (true)
            {
                int i = enc.IndexOfAfterLoop(input, pos + p.LoopMin, p);
                if (i < 0)
                {
                    return false;
                }

                int s = i;
                while (s > pos && enc.Unit(input, s - 1) < 0x80 && loopSet.HasAscii(enc.Unit(input, s - 1)))
                {
                    s--;
                }

                if (p.LoopMin == 1 && s == i)
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
        if (p.RequiredCount >= 0)
        {
            // Every match holds the literal after a fixed number of characters, so the literal's first occurrence
            // at or after pos plus that many allows one start, and a start before it can hold no match. Where the
            // characters are a fixed number of units of this encoding, the start is the occurrence less that;
            // otherwise it is found by walking back over the characters.
            int offset = enc.RequiredOffset(p);
            int count = p.RequiredCount;
            int next = pos + (offset >= 0 ? offset : count);
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

        while (true)
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
                if (i < 0)
                {
                    return false;
                }

                pos = enc.AlignStart(input, i, units);
                from = pos;
                if (len - pos < p.Need)
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
    }

    /// <summary>
    /// The end of the run of members of a set that starts at a position: the first position at which the character
    /// is not in the set, or the end of the text. On .NET the ASCII part of the scan is vectorised; a character
    /// that is not ASCII is decoded and looked up unless the set holds every such character.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EndOfRun<TUnit, TEncoding>(CharSet set, ReadOnlySpan<TUnit> input, int at, bool units)
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

    /// <summary>The end of the run of one ASCII character that starts at a position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EndOfRun<TUnit, TEncoding>(int c, ReadOnlySpan<TUnit> input, int at)
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
    private static int MemoIndex(Program p, MatchState st, int m, int pos)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MemoBit(ulong[] memo, int idx) => ((memo[idx >> 6] >> (idx & 63)) & 1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetMemoBit(ulong[] memo, int idx) => memo[idx >> 6] |= 1UL << (idx & 63);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Push(ref Entry[] stack, ref int sp, byte kind, int pc, int pos, int a)
    {
        if (sp == stack.Length)
        {
            Array.Resize(ref stack, stack.Length * 2);
        }

        stack[sp++] = new Entry(kind, pc, pos, a);
    }

    /// <summary>The width of the character at the position <paramref name="at"/> when the instruction's character or class matches it, and 0 otherwise. The character is a code unit with <paramref name="units"/> set.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int One<TUnit, TEncoding>(in Inst i, ReadOnlySpan<TUnit> input, int at, bool units)
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GiveBack<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int pos, bool units)
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFoldWordRune(int r) => (r < 128 && Utf8Encoding.IsWordUnit(r)) || r == 0x17F || r == 0x212A;

    /// <summary>
    /// Reports whether the program matches at the position <paramref name="start"/>. It keeps its choices on an
    /// explicit stack, so the depth of the search is bounded by memory and not by the stack of the thread. When it
    /// fails, every group and counter is back to the value it had on entry. It counts its steps, and when a check
    /// says the match has reached a limit it returns false with the reason in the state.
    /// </summary>
    private static bool Run<TUnit, TEncoding>(Program p, MatchState st, ReadOnlySpan<TUnit> input, int start, out int end)
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

                        at = set is not null ? EndOfRun<TUnit, TEncoding>(set, input, at, units) : EndOfRun<TUnit, TEncoding>(i.R, input, at);
                        n = min;
                    }
                    else if (set is not null)
                    {
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
            while (true)
            {
                if (sp == 0)
                {
                    st.Steps = steps;
                    st.Stack = stack;
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
                    break;
                }
            }
        }
    }
}
