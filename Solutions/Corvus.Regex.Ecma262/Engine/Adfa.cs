// <copyright file="Adfa.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of adfa.rs, and of the rest check of search.rs, of corvus-regex-ecma262-rs.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The automata the search runs over ASCII text, built from the tree of a pattern (after
/// <see cref="Compiler.Simplify"/>): an anchored, leftmost-first automaton that finds where the match that starts at a
/// position ends, for a pattern that needs no backtracking (what the <c>regex</c> crate's automata do for the match,
/// taken into this engine where ECMA-262's semantics allow it); the forward automaton, which says where the first
/// match ends; the reverse automaton of the terms before a search's literal; and the window of positions read one unit
/// at a time before a match can end.
/// </summary>
/// <remarks>
/// A state of the anchored automaton is an ordered list of the pattern's states (its threads, the most preferred
/// first, as the backtracking matcher would try them), with whether the unit before it was a word character and
/// whether it is at the start of the text. When the match state is among the threads, a match ends here, and the
/// threads after it, which the backtracking matcher would only try had this way failed, are dropped; the threads
/// before it may still find a longer match, which it would prefer. So the last match found before the threads run out
/// is the backtracking matcher's match, for a pattern with no lookaround, no backreference and no repetition whose
/// body can match the empty string (where ECMA-262's rule for an empty iteration would differ from a thread's). A unit
/// beyond ASCII hands the start back to the backtracking matcher. Every state is worked out when the automaton is
/// built, within bounds of states and steps, so that a pattern too large for an automaton costs little before that
/// is known.
/// </remarks>
internal static class Adfa
{
    /// <summary>What <see cref="AnchoredAutomaton.Find{TUnit, TEncoding}"/> returns where no match starts at the position.</summary>
    public const int NoMatch = -1;

    /// <summary>What the automata return where a unit beyond ASCII was reached: the backtracking matcher decides.</summary>
    public const int Bail = -2;

    /// <summary>The forward automaton found where a match ends (see <see cref="AnchoredAutomaton.Scan{TUnit, TEncoding}"/>).</summary>
    public const int ScanMatch = 0;

    /// <summary>The forward automaton reached a start state: no match starts before it.</summary>
    public const int ScanIdle = 1;

    /// <summary>The forward automaton reached a unit beyond ASCII.</summary>
    public const int ScanBail = 2;

    /// <summary>The forward automaton reached the end of the text with no match.</summary>
    public const int ScanNone = 3;

    /// <summary>The most states of an automaton.</summary>
    internal const int MaxStates = 2048;

    /// <summary>The most states of the pattern an automaton is built from.</summary>
    internal const int MaxNfa = 1024;

    /// <summary>The most steps of a build (a step is one state of the pattern looked at while a state is made).</summary>
    internal const long MaxSteps = 2_000_000;

    /// <summary>No thread is left: no match can end further on.</summary>
    internal const ushort Dead = 0;

    /// <summary>The flag of a transition across which a match ends before the unit.</summary>
    internal const ushort Matched = 0x8000;

    /// <summary>The same flag in the premultiplied table.</summary>
    internal const uint FastMatched = 0x8000_0000;

    /// <summary>
    /// Whether an automaton's tables read from an image hold together: every class is a column of the table, and every
    /// transition, less its flag, is the start of a row of one of its states.
    /// </summary>
    /// <param name="classes">The classes of the units.</param>
    /// <param name="shift">The log of the width of a row.</param>
    /// <param name="fast">The transitions.</param>
    /// <param name="states">The number of states.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    internal static bool TablesHold(byte[] classes, int shift, uint[] fast, int states)
    {
        if (shift < 0 || shift > 16 || states <= 0 || (long)states << shift != fast.Length)
        {
            return false;
        }

        foreach (byte c in classes)
        {
            if (c >= 1 << shift)
            {
                return false;
            }
        }

        uint rowMask = (1u << shift) - 1;
        foreach (uint t in fast)
        {
            uint row = t & ~FastMatched;
            if ((row & rowMask) != 0 || (row >> shift) >= (uint)states)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The most positions looked at for a window (see <see cref="Skip"/>).</summary>
    private const int SkipPositions = 16;

    /// <summary>The kinds of the pattern's states.</summary>
    internal enum Kind : byte
    {
        /// <summary>Reads one unit of its set and goes to <c>Out</c>.</summary>
        Set,

        /// <summary>Goes to <c>Out</c>, the preferred way, and to <c>Alt</c>, reading nothing.</summary>
        Split,

        /// <summary>Goes to <c>Out</c> at the start of the text.</summary>
        Bol,

        /// <summary>Goes to <c>Out</c> at the end of the text.</summary>
        Eol,

        /// <summary>Goes to <c>Out</c> where exactly one of the units either side is a word character.</summary>
        Word,

        /// <summary>Goes to <c>Out</c> where both or neither of the units either side is a word character.</summary>
        NotWord,

        /// <summary>The match.</summary>
        Match,
    }

    /// <summary>Builds the anchored automaton of a tree, or null when the pattern has none.</summary>
    /// <param name="root">The tree.</param>
    /// <returns>The automaton, or null.</returns>
    public static AnchoredAutomaton? Build(EcmaRegexNode root) => BuildWith(root, forward: false);

    /// <summary>
    /// Builds the forward automaton of a tree: the anchored automaton with a new start joined at every unit, behind
    /// every thread already alive (so last in preference, as the search tries later starts after earlier ones). Read
    /// over the text from a start, it says where the first match ends, and its start states say where no thread of an
    /// earlier start is alive: the leftmost match starts at or after the last of them, the forward search of the
    /// <c>regex</c> crate.
    /// </summary>
    /// <param name="root">The tree.</param>
    /// <returns>The automaton, or null.</returns>
    public static AnchoredAutomaton? BuildForward(EcmaRegexNode root) => BuildWith(root, forward: true);

    /// <summary>
    /// The window of a tree worth reading one unit of instead of every unit, or null: the units each position before a
    /// match can end can be, from the pattern's automaton without its determinisation, and the window whose units are
    /// rare enough that one unit read passes over three starts or more, reckoned by <see cref="Frequency"/>.
    /// </summary>
    /// <param name="root">The tree.</param>
    /// <returns>The window, or null.</returns>
    public static Skip? SkipWindow(EcmaRegexNode root)
    {
        Builder b = new();
        int match = b.Add(new State(Kind.Match, 0));
        int start = b.Compile(root, match);
        if (b.Failed)
        {
            return null;
        }

        List<State> nfa = b.States;
        List<(bool[] Table, bool Wide)> positions = [];
        List<int> threads = [start];
        while (positions.Count < SkipPositions)
        {
            List<int>? sets = SkipClosure(nfa, threads);
            if (sets is null)
            {
                break;
            }

            bool[] table = new bool[128];
            bool wide = false;
            foreach (int i in sets)
            {
                for (int c = 0; c < 128; c++)
                {
                    table[c] |= nfa[i].Has(c);
                }

                wide |= nfa[i].Wide;
            }

            positions.Add((table, wide));

            // A character beyond ASCII takes more than one unit of UTF-8: the positions after it are not known.
            if (wide)
            {
                break;
            }

            threads = [];
            foreach (int i in sets)
            {
                threads.Add(nfa[i].Out);
            }
        }

        double bestWorth = 0;
        int bestLo = -1;
        int bestHi = -1;
        for (int lo = 0; lo < positions.Count; lo++)
        {
            bool[] table = new bool[128];
            bool wide = false;
            for (int hi = lo; hi < positions.Count; hi++)
            {
                for (int c = 0; c < 128; c++)
                {
                    table[c] |= positions[hi].Table[c];
                }

                wide |= positions[hi].Wide;
                double p = wide ? 0.1 : 0.0;
                for (int c = 0; c < 128; c++)
                {
                    if (table[c])
                    {
                        p += Frequency(c);
                    }
                }

                double worth = (hi - lo + 1) * (1.0 - Math.Min(p, 1.0));
                if (worth >= 3.0 && (bestLo < 0 || worth > bestWorth))
                {
                    bestWorth = worth;
                    bestLo = lo;
                    bestHi = hi;
                }
            }
        }

        if (bestLo < 0)
        {
            return null;
        }

        bool[] joined = new bool[128];
        bool joinedWide = false;
        for (int k = bestLo; k <= bestHi; k++)
        {
            for (int c = 0; c < 128; c++)
            {
                joined[c] |= positions[k].Table[c];
            }

            joinedWide |= positions[k].Wide;
        }

        return new Skip(bestHi, bestHi - bestLo + 1, joined, joinedWide);
    }

    /// <summary>
    /// The reverse automaton of the terms before each of a search's literals (read backwards, accepting where any of
    /// them can start), or null when one holds an assertion or the automaton is too large.
    /// </summary>
    /// <param name="prefixes">For each literal, the terms before it.</param>
    /// <returns>The automaton, or null.</returns>
    public static ReverseAutomaton? Reverse(IReadOnlyList<EcmaRegexNode[]> prefixes)
    {
        Builder b = new();
        int match = b.Add(new State(Kind.Match, 0));
        List<int> starts = [];
        foreach (EcmaRegexNode[] prefix in prefixes)
        {
            int next = match;
            foreach (EcmaRegexNode t in prefix)
            {
                next = b.CompileBack(t, next);
            }

            starts.Add(next);
        }

        if (b.Failed || starts.Count == 0)
        {
            return null;
        }

        int start = starts[starts.Count - 1];
        for (int k = starts.Count - 2; k >= 0; k--)
        {
            start = b.Split(starts[k], start);
        }

        if (b.Failed)
        {
            return null;
        }

        List<State> nfa = b.States;
        Det d = new(nfa);
        byte[] classes = Classes(nfa, withWord: false, out int count);
        int shift = 0;
        while (1 << shift < count)
        {
            shift++;
        }

        int stride = 1 << shift;
        int[] sample = Sample(classes, count);

        // A set of states, sorted, is its key; every thread is kept (any of them reaching the match is a start).
        Dictionary<string, ushort> ids = [];
        List<List<int>> sets = [[]];
        List<ushort> trans = [];
        Fill(trans, stride);
        List<bool> accept = [false];
        List<int> first = d.ClosureAll([start]);
        ids[Key(first, false, false)] = 1;
        accept.Add(HasKind(nfa, first, Kind.Match));
        sets.Add(first);
        Fill(trans, stride);
        List<int> moved = [];
        for (int id = 1; id < sets.Count; id++)
        {
            for (int cl = 0; cl < count; cl++)
            {
                int c = sample[cl];
                moved.Clear();
                foreach (int i in sets[id])
                {
                    if (nfa[i].Kind == Kind.Set && nfa[i].Has(c))
                    {
                        moved.Add(nfa[i].Out);
                    }
                }

                List<int> next = d.ClosureAll(moved);
                ushort to;
                if (next.Count == 0)
                {
                    to = Dead;
                }
                else
                {
                    string key = Key(next, false, false);
                    if (!ids.TryGetValue(key, out to))
                    {
                        if (sets.Count >= MaxStates)
                        {
                            return null;
                        }

                        to = (ushort)sets.Count;
                        accept.Add(HasKind(nfa, next, Kind.Match));
                        ids[key] = to;
                        sets.Add(next);
                        Fill(trans, stride);
                    }
                }

                if (d.Steps < 0)
                {
                    return null;
                }

                trans[(id * stride) + cl] = to;
            }
        }

        uint[] fast = new uint[trans.Count];
        for (int k = 0; k < trans.Count; k++)
        {
            ushort t = trans[k];
            fast[k] = ((uint)t << shift) | (t != Dead && accept[t] ? FastMatched : 0);
        }

        return new ReverseAutomaton(classes, shift, fast, accept[1]);
    }

    /// <summary>The number of the lowest set bit of a word that is not zero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TrailingZeros(ulong x)
    {
#if NET10_0_OR_GREATER
        return System.Numerics.BitOperations.TrailingZeroCount(x);
#else
        int n = 0;
        while ((x & 1) == 0)
        {
            x >>= 1;
            n++;
        }

        return n;
#endif
    }

    /// <summary>Whether the unit before a position is an ASCII word character.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool WordBefore<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int at)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (at == 0)
        {
            return false;
        }

        TEncoding enc = default;
        int c = enc.Unit(input, at - 1);
        return c < 0x80 && Utf8Encoding.IsWordUnit(c);
    }

    /// <summary>How often a unit is found in text, roughly: what a window's worth is reckoned by.</summary>
    private static double Frequency(int c) => c switch
    {
        ' ' => 0.15,
        >= 'a' and <= 'z' => 0.06,
        '\n' or '\r' or '.' or ',' => 0.015,
        (>= 'A' and <= 'Z') or (>= '0' and <= '9') => 0.004,
        _ => 0.003,
    };

    /// <summary>The states of the pattern's sets each depth's threads are at, assertions taken as passed (they only rule threads out); null where a match can end, after which no position is known.</summary>
    private static List<int>? SkipClosure(List<State> nfa, List<int> from)
    {
        bool[] seen = new bool[nfa.Count];
        Stack<int> stack = new(from);
        List<int> sets = [];
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (seen[i])
            {
                continue;
            }

            seen[i] = true;
            switch (nfa[i].Kind)
            {
                case Kind.Match:
                    return null;
                case Kind.Set:
                    sets.Add(i);
                    break;
                case Kind.Split:
                    stack.Push(nfa[i].Out);
                    stack.Push(nfa[i].Alt);
                    break;
                default:
                    stack.Push(nfa[i].Out);
                    break;
            }
        }

        return sets;
    }

    private static AnchoredAutomaton? BuildWith(EcmaRegexNode root, bool forward)
    {
        Builder b = new();
        int match = b.Add(new State(Kind.Match, 0));
        int start = b.Compile(root, match);
        if (b.Failed)
        {
            return null;
        }

        List<State> nfa = b.States;
        Det d = new(nfa);

        // In the forward automaton a thread of the start joined at the current unit is kept apart from the threads of
        // earlier starts, as its state's number plus the number of states, so that a state whose threads are all of
        // the current start (a start state) is known: no earlier start can still match.
        int n = nfa.Count;

        // The classes of units: those every set has both or neither of, and that are both word characters or neither.
        byte[] classes = Classes(nfa, withWord: true, out int count);
        int shift = 0;
        while (1 << shift < count)
        {
            shift++;
        }

        int stride = 1 << shift;
        int[] sample = Sample(classes, count);

        // The word bit matters only to a pattern with a boundary and the start bit only to one with a start anchor
        // (each may be reached later, behind an end anchor or a boundary still to pass, so the whole pattern is asked).
        bool boundaries = false;
        bool anchors = false;
        foreach (State s in nfa)
        {
            boundaries |= s.Kind is Kind.Word or Kind.NotWord;
            anchors |= s.Kind == Kind.Bol;
        }

        Dictionary<string, ushort> ids = [];
        List<(List<int> Set, bool Word, bool AtStart)> sets = [([], false, false)];
        List<ushort> trans = [];
        Fill(trans, stride);
        List<bool> atEnd = [false];

        ushort[] starts = new ushort[4];
        for (int k = 0; k < 4; k++)
        {
            bool word = (k & 2) != 0;
            bool atStart = (k & 1) != 0;
            List<int> first = [];
            d.Closure(first, [start], atStart, false, false, false, false);
            if (forward)
            {
                for (int j = 0; j < first.Count; j++)
                {
                    first[j] += n;
                }
            }

            int id = Intern(first, word, atStart);
            if (id < 0)
            {
                return null;
            }

            starts[k] = (ushort)id;
        }

        List<int> resolved = [];
        List<int> moved = [];
        for (int id = 1; id < sets.Count; id++)
        {
            (List<int> keySet, bool wordBefore, bool atStartOf) = sets[id];
            List<int> set = Decode(keySet, n);
            for (int cl = 0; cl < count; cl++)
            {
                int c = sample[cl];
                bool wordAfter = Utf8Encoding.IsWordUnit(c);
                resolved.Clear();
                d.Closure(resolved, set, atStartOf, false, true, wordBefore, wordAfter);
                ushort flag = 0;
                int m = resolved.FindIndex(i => nfa[i].Kind == Kind.Match);
                if (m >= 0)
                {
                    // A match ends before this unit; only the threads it would have been tried after go on.
                    flag = Matched;
                    resolved.RemoveRange(m, resolved.Count - m);
                }

                moved.Clear();
                foreach (int i in resolved)
                {
                    State s = nfa[i];
                    if (s.Kind == Kind.Set && s.Has(c))
                    {
                        moved.Add(s.Out);
                    }
                }

                List<int> next = [];
                d.Closure(next, moved, false, false, false, false, false);
                if (forward && flag == 0)
                {
                    List<int> fresh = [];
                    d.ClosureOn(fresh, [start], false, false, false, false, false);
                    foreach (int i in fresh)
                    {
                        next.Add(i + n);
                    }
                }

                int to = Intern(next, wordAfter, false);
                if (to < 0 || d.Steps < 0)
                {
                    return null;
                }

                trans[(id * stride) + cl] = (ushort)(to | flag);
            }
        }

        uint[] fast = new uint[trans.Count];
        for (int k = 0; k < trans.Count; k++)
        {
            ushort t = trans[k];
            fast[k] = ((uint)(t & ~Matched) << shift) | ((t & Matched) != 0 ? FastMatched : 0);
        }

        bool context = starts[1] != starts[0] || starts[2] != starts[0] || starts[3] != starts[0];
        bool[] startLike = [];
        if (forward)
        {
            startLike = new bool[sets.Count];
            for (int k = 0; k < sets.Count; k++)
            {
                List<int> set = sets[k].Set;
                bool all = set.Count > 0;
                foreach (int i in set)
                {
                    all &= i >= n;
                }

                startLike[k] = all;
            }
        }

        return new AnchoredAutomaton(classes, shift, fast, [.. atEnd], starts, context, startLike);

        int Intern(List<int> set, bool word, bool atStart)
        {
            if (set.Count == 0)
            {
                return Dead;
            }

            word &= boundaries;
            atStart &= anchors;
            string key = Key(set, word, atStart);
            if (ids.TryGetValue(key, out ushort found))
            {
                return found;
            }

            if (sets.Count >= MaxStates)
            {
                return -1;
            }

            ushort id = (ushort)sets.Count;
            List<int> closed = [];
            d.Closure(closed, Decode(set, n), atStart, true, true, word, false);
            atEnd.Add(HasKind(nfa, closed, Kind.Match));
            Fill(trans, stride);
            ids[key] = id;
            sets.Add((set, word, atStart));
            return id;
        }
    }

    /// <summary>The classes of the ASCII units: those every set of the pattern has both or neither of (and, with <paramref name="withWord"/>, that are both word characters or neither).</summary>
    private static byte[] Classes(List<State> nfa, bool withWord, out int count)
    {
        byte[] classes = new byte[128];
        Dictionary<string, byte> signatures = [];
        StringBuilder sig = new();
        for (int c = 0; c < 128; c++)
        {
            sig.Clear();
            if (withWord)
            {
                sig.Append(Utf8Encoding.IsWordUnit(c) ? '1' : '0');
            }

            foreach (State s in nfa)
            {
                if (s.Kind == Kind.Set)
                {
                    sig.Append(s.Has(c) ? '1' : '0');
                }
            }

            string key = sig.ToString();
            if (!signatures.TryGetValue(key, out byte cls))
            {
                cls = (byte)signatures.Count;
                signatures[key] = cls;
            }

            classes[c] = cls;
        }

        count = signatures.Count;
        return classes;
    }

    /// <summary>One unit of each class, the least, to move a set by.</summary>
    private static int[] Sample(byte[] classes, int count)
    {
        int[] sample = new int[count];
        for (int c = 127; c >= 0; c--)
        {
            sample[classes[c]] = c;
        }

        return sample;
    }

    private static void Fill(List<ushort> trans, int stride)
    {
        for (int k = 0; k < stride; k++)
        {
            trans.Add(Dead);
        }
    }

    private static bool HasKind(List<State> nfa, List<int> set, Kind kind)
    {
        foreach (int i in set)
        {
            if (nfa[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The states of a forward automaton's key, with the threads of the current start as the states they are.</summary>
    private static List<int> Decode(List<int> set, int n)
    {
        List<int> decoded = new(set.Count);
        foreach (int i in set)
        {
            decoded.Add(i >= n ? i - n : i);
        }

        return decoded;
    }

    /// <summary>The key of a state: its threads in order, whether the unit before was a word character, and whether it is at the start of the text.</summary>
    private static string Key(List<int> set, bool word, bool atStart)
    {
        char[] chars = new char[set.Count + 1];
        chars[0] = (char)((word ? 2 : 0) | (atStart ? 1 : 0));
        for (int k = 0; k < set.Count; k++)
        {
            chars[k + 1] = (char)set[k];
        }

        return new string(chars);
    }

    /// <summary>A state of the pattern.</summary>
    internal struct State
    {
        public State(Kind kind, int output)
        {
            this.Kind = kind;
            this.Out = output;
        }

        public Kind Kind { get; set; }

        public ulong Set0 { get; set; }

        public ulong Set1 { get; set; }

        /// <summary>Gets or sets a value indicating whether the set holds a character beyond ASCII, which the automaton gives up at.</summary>
        public bool Wide { get; set; }

        public int Out { get; set; }

        public int Alt { get; set; }

        public readonly bool Has(int c) => c < 64 ? ((this.Set0 >> c) & 1) != 0 : ((this.Set1 >> (c - 64)) & 1) != 0;
    }

    /// <summary>Makes the pattern's states from its tree.</summary>
    internal sealed class Builder
    {
        public List<State> States { get; } = [];

        /// <summary>Gets a value indicating whether the pattern has something an automaton over units cannot decide, or is too large.</summary>
        public bool Failed { get; private set; }

        public int Add(State s)
        {
            if (this.States.Count >= MaxNfa)
            {
                this.Failed = true;
                return 0;
            }

            this.States.Add(s);
            return this.States.Count - 1;
        }

        /// <summary>A split whose preferred way is <paramref name="first"/>.</summary>
        public int Split(int first, int second) => this.Add(new State(Kind.Split, first) { Alt = second });

        /// <summary>The states of a node that go on to <paramref name="next"/>; the state they start at.</summary>
        public int Compile(EcmaRegexNode n, int next)
        {
            if (this.Failed)
            {
                return 0;
            }

            switch (n.Kind)
            {
                case EcmaRegexNodeKind.Empty:
                    return next;
                case EcmaRegexNodeKind.Char:
                {
                    State s = new(Kind.Set, next) { Wide = n.CodePoint >= 128 };
                    if (n.CodePoint >= 0 && n.CodePoint < 64)
                    {
                        s.Set0 = 1UL << n.CodePoint;
                    }
                    else if (n.CodePoint >= 64 && n.CodePoint < 128)
                    {
                        s.Set1 = 1UL << (n.CodePoint - 64);
                    }

                    return this.Add(s);
                }

                case EcmaRegexNodeKind.Set:
                {
                    CharSet set = new(n.Set);
                    return this.Add(new State(Kind.Set, next) { Set0 = set.Ascii0, Set1 = set.Ascii1, Wide = set.HasNonAscii });
                }

                case EcmaRegexNodeKind.Cat:
                    for (int k = n.Subs.Length - 1; k >= 0; k--)
                    {
                        next = this.Compile(n.Subs[k], next);
                    }

                    return next;
                case EcmaRegexNodeKind.Alt:
                {
                    int start = this.Compile(n.Subs[n.Subs.Length - 1], next);
                    for (int k = n.Subs.Length - 2; k >= 0; k--)
                    {
                        int output = this.Compile(n.Subs[k], next);
                        start = this.Split(output, start);
                    }

                    return start;
                }

                case EcmaRegexNodeKind.Group:
                    return this.Compile(n.Subs[0], next);
                case EcmaRegexNodeKind.Repeat:
                {
                    EcmaRegexNode sub = n.Subs[0];
                    if (n.Max != 0 && n.Max != 1 && Compiler.Nullable(sub))
                    {
                        // ECMA-262 refuses an empty iteration after the least count, which a thread does not know.
                        this.Failed = true;
                        return 0;
                    }

                    if (n.Max == EcmaRegexNode.Unbounded)
                    {
                        int loop = this.Split(0, 0);
                        if (this.Failed)
                        {
                            return 0;
                        }

                        int body = this.Compile(sub, loop);
                        State s = this.States[loop];
                        (s.Out, s.Alt) = n.Lazy ? (next, body) : (body, next);
                        this.States[loop] = s;
                        next = loop;
                    }
                    else
                    {
                        for (int k = n.Min; k < n.Max && !this.Failed; k++)
                        {
                            int body = this.Compile(sub, next);
                            next = n.Lazy ? this.Split(next, body) : this.Split(body, next);
                        }
                    }

                    for (int k = 0; k < n.Min && !this.Failed; k++)
                    {
                        next = this.Compile(sub, next);
                    }

                    return next;
                }

                case EcmaRegexNodeKind.Bol:
                    return this.Add(new State(Kind.Bol, next));
                case EcmaRegexNodeKind.Eol:
                    return this.Add(new State(Kind.Eol, next));
                case EcmaRegexNodeKind.WordBoundary when !n.Fold:
                    return this.Add(new State(Kind.Word, next));
                case EcmaRegexNodeKind.NotWordBoundary when !n.Fold:
                    return this.Add(new State(Kind.NotWord, next));
                default:
                    this.Failed = true;
                    return 0;
            }
        }

        /// <summary>The states of a node read backwards that go on to <paramref name="next"/>.</summary>
        public int CompileBack(EcmaRegexNode n, int next)
        {
            if (this.Failed)
            {
                return 0;
            }

            switch (n.Kind)
            {
                case EcmaRegexNodeKind.Cat:
                    foreach (EcmaRegexNode sub in n.Subs)
                    {
                        next = this.CompileBack(sub, next);
                    }

                    return next;
                case EcmaRegexNodeKind.Alt:
                {
                    int start = this.CompileBack(n.Subs[n.Subs.Length - 1], next);
                    for (int k = n.Subs.Length - 2; k >= 0; k--)
                    {
                        int output = this.CompileBack(n.Subs[k], next);
                        start = this.Split(output, start);
                    }

                    return start;
                }

                case EcmaRegexNodeKind.Group:
                    return this.CompileBack(n.Subs[0], next);
                case EcmaRegexNodeKind.Repeat:
                {
                    EcmaRegexNode sub = n.Subs[0];
                    if (n.Max == EcmaRegexNode.Unbounded)
                    {
                        int loop = this.Split(0, 0);
                        if (this.Failed)
                        {
                            return 0;
                        }

                        int body = this.CompileBack(sub, loop);
                        State s = this.States[loop];
                        (s.Out, s.Alt) = (body, next);
                        this.States[loop] = s;
                        next = loop;
                    }
                    else
                    {
                        for (int k = n.Min; k < n.Max && !this.Failed; k++)
                        {
                            int body = this.CompileBack(sub, next);
                            next = this.Split(body, next);
                        }
                    }

                    for (int k = 0; k < n.Min && !this.Failed; k++)
                    {
                        next = this.CompileBack(sub, next);
                    }

                    return next;
                }

                case EcmaRegexNodeKind.Empty:
                case EcmaRegexNodeKind.Char:
                case EcmaRegexNodeKind.Set:
                    return this.Compile(n, next);
                default:
                    this.Failed = true;
                    return 0;
            }
        }
    }

    /// <summary>The closures a build makes, with the steps it has left.</summary>
    private sealed class Det(List<State> nfa)
    {
        private readonly List<State> nfa = nfa;
        private readonly int[] mark = new int[nfa.Count];
        private readonly Stack<int> stack = new();
        private int generation;

        public long Steps { get; private set; } = MaxSteps;

        /// <summary>
        /// Appends to <paramref name="output"/>, in order of preference, the threads that reading nothing reaches from
        /// <paramref name="from"/>, keeping the states that read a unit, the anchors and boundaries still to pass, and
        /// the match, after which nothing more is kept. Boundaries are passed or dropped where <paramref name="words"/>
        /// says whether the units before and after are word characters, and kept to pass later where it does not.
        /// </summary>
        public void Closure(List<int> output, List<int> from, bool atStart, bool atEnd, bool words, bool before, bool after)
        {
            this.generation++;
            this.ClosureOn(output, from, atStart, atEnd, words, before, after);
        }

        /// <summary><see cref="Closure"/> after the closure just made, leaving out the states it reached.</summary>
        public void ClosureOn(List<int> output, List<int> from, bool atStart, bool atEnd, bool words, bool before, bool after)
        {
            this.stack.Clear();
            for (int k = from.Count - 1; k >= 0; k--)
            {
                this.stack.Push(from[k]);
            }

            while (this.stack.Count > 0)
            {
                int i = this.stack.Pop();
                this.Steps--;
                if (this.mark[i] == this.generation)
                {
                    continue;
                }

                this.mark[i] = this.generation;
                State s = this.nfa[i];
                switch (s.Kind)
                {
                    case Kind.Split:
                        this.stack.Push(s.Alt);
                        this.stack.Push(s.Out);
                        break;
                    case Kind.Bol:
                        if (atStart)
                        {
                            this.stack.Push(s.Out);
                        }

                        break;
                    case Kind.Eol:
                        if (atEnd)
                        {
                            this.stack.Push(s.Out);
                        }
                        else
                        {
                            output.Add(i);
                        }

                        break;
                    case Kind.Word:
                    case Kind.NotWord:
                        if (!words)
                        {
                            output.Add(i);
                        }
                        else if ((before != after) == (s.Kind == Kind.Word))
                        {
                            this.stack.Push(s.Out);
                        }

                        break;
                    case Kind.Match:
                        output.Add(i);
                        return;
                    default:
                        output.Add(i);
                        break;
                }
            }
        }

        /// <summary>Every state that reading nothing reaches from <paramref name="from"/>, splits passed and everything else kept, sorted: the closure of the reverse automaton.</summary>
        public List<int> ClosureAll(List<int> from)
        {
            List<int> output = [];
            this.generation++;
            this.stack.Clear();
            foreach (int f in from)
            {
                this.stack.Push(f);
            }

            while (this.stack.Count > 0)
            {
                int i = this.stack.Pop();
                this.Steps--;
                if (this.mark[i] == this.generation)
                {
                    continue;
                }

                this.mark[i] = this.generation;
                State s = this.nfa[i];
                if (s.Kind == Kind.Split)
                {
                    this.stack.Push(s.Alt);
                    this.stack.Push(s.Out);
                }
                else
                {
                    output.Add(i);
                }
            }

            output.Sort();
            return output;
        }
    }
}

/// <summary>The anchored automaton of a pattern, and the forward automaton (see <see cref="Adfa"/>).</summary>
internal sealed class AnchoredAutomaton
{
#if NET10_0_OR_GREATER
    /// <summary>The fewest units a state must loop on to be accelerated: a large set, such as a class of letters, words or anything but a few units, whose runs are long.</summary>
    private const int AccelMembers = 24;

    /// <summary>The units read in a state that loops before its run is searched for: a short run is read faster than a search is called. Measured over the Industry texts with the interpreter, 4 against 8 and 16: the URL workload 4.5 us against 5.1 and 5.5 (4.8 with no automaton), [a-zA-Z]+ing 170 against 163 and 159 (227 with none).</summary>
    private const int AccelAfter = 4;
#endif

    private readonly byte[] classes;
    private readonly int shift;

    /// <summary><c>fast[state * stride + class]</c>: the next state, premultiplied by the stride, with <see cref="Adfa.FastMatched"/> when a match ends before the unit.</summary>
    private readonly uint[] fast;

    /// <summary>For each state: a match ends at the end of the text.</summary>
    private readonly bool[] atEnd;

    /// <summary>The start state for each of: the unit before is a word character (bit 1), the start of the text (bit 0).</summary>
    private readonly ushort[] starts;

    /// <summary>Whether the start states differ (the pattern looks back at the unit before or at the start of the text).</summary>
    private readonly bool context;

    /// <summary>For the forward automaton, whether each state is a start state: no thread of an earlier start is alive in it.</summary>
    private readonly bool[] startLike;

#if NET10_0_OR_GREATER
    /// <summary>
    /// For each state that loops on a large set of units, those units, so that a long run of them is passed over by
    /// one vectorised search rather than a table read a unit (the accelerated states of the <c>regex</c> crate's
    /// automata); with whether a match ends before each of them. Null for the other states.
    /// </summary>
    private readonly System.Buffers.SearchValues<byte>?[] accel8;
    private readonly System.Buffers.SearchValues<char>?[] accel16;
    private readonly bool[] accelMatched;
#endif

    /// <summary>Writes the automaton's tables into an image (see <see cref="RegexImage"/>).</summary>
    /// <param name="w">The writer.</param>
    internal void WriteTo(RegexImage.Writer w)
    {
        w.Bytes(this.classes);
        w.Int(this.shift);
        w.UInts(this.fast);
        w.Bools(this.atEnd);
        w.UShorts(this.starts);
        w.Bool(this.context);
        w.Bools(this.startLike);
    }

    /// <summary>Reads an automaton's tables from an image, checking that every state they name is one of its states.</summary>
    /// <param name="r">The reader, failed when the tables do not hold together.</param>
    /// <param name="forward">Whether it is a forward automaton, the only kind with a table of the states that are like a start.</param>
    /// <returns>The automaton, or null.</returns>
    internal static AnchoredAutomaton? ReadFrom(ref RegexImage.Reader r, bool forward)
    {
        byte[] classes = r.Bytes();
        int shift = r.Int();
        uint[] fast = r.UInts();
        bool[] atEnd = r.Bools();
        ushort[] starts = r.UShorts();
        bool context = r.Bool();
        bool[] startLike = r.Bools();
        int states = atEnd.Length;
        if (!r.Ok || !Adfa.TablesHold(classes, shift, fast, states) || startLike.Length != (forward ? states : 0))
        {
            r.Fail();
            return null;
        }

        foreach (ushort s in starts)
        {
            if (s >= states)
            {
                r.Fail();
                return null;
            }
        }

        return new AnchoredAutomaton(classes, shift, fast, atEnd, starts, context, startLike);
    }

    internal AnchoredAutomaton(byte[] classes, int shift, uint[] fast, bool[] atEnd, ushort[] starts, bool context, bool[] startLike)
    {
        this.classes = classes;
        this.shift = shift;
        this.fast = fast;
        this.atEnd = atEnd;
        this.starts = starts;
        this.context = context;
        this.startLike = startLike;
#if NET10_0_OR_GREATER
        int states = atEnd.Length;
        this.accel8 = new System.Buffers.SearchValues<byte>?[states];
        this.accel16 = new System.Buffers.SearchValues<char>?[states];
        this.accelMatched = new bool[states];
        List<byte> members = [];
        for (int s = 1; s < states; s++)
        {
            // The units whose transition from the state is back to it, all with the same flag.
            members.Clear();
            uint row = (uint)s << shift;
            uint flag = 0;
            bool mixed = false;
            for (int c = 0; c < 128; c++)
            {
                uint t = fast[row + classes[c]];
                if ((t & ~Adfa.FastMatched) != row)
                {
                    continue;
                }

                if (members.Count == 0)
                {
                    flag = t & Adfa.FastMatched;
                }
                else if ((t & Adfa.FastMatched) != flag)
                {
                    mixed = true;
                    break;
                }

                members.Add((byte)c);
            }

            if (!mixed && members.Count >= AccelMembers)
            {
                byte[] bytes = [.. members];
                this.accel8[s] = System.Buffers.SearchValues.Create(bytes);
                this.accel16[s] = System.Buffers.SearchValues.Create(Array.ConvertAll(bytes, b => (char)b));
                this.accelMatched[s] = flag != 0;
            }
        }
#endif
    }

    /// <summary>Where the match that starts at a position ends, <see cref="Adfa.NoMatch"/>, or <see cref="Adfa.Bail"/> where a unit beyond ASCII was reached first.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <returns>The end, or what was found instead.</returns>
    public int Find<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int start)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        int s = this.Start<TUnit, TEncoding>(input, start);
        if (s == Adfa.Dead)
        {
            return Adfa.NoMatch;
        }

        TEncoding enc = default;
        uint[] trans = this.fast;
        byte[] cls = this.classes;
        int last = Adfa.NoMatch;
        int p = start;

        // The state is kept premultiplied by the stride, so a step is one load and an add.
        uint at = (uint)s << this.shift;
#if NET10_0_OR_GREATER
        int run = 0;
#endif
        while (p < input.Length)
        {
            int c = enc.Unit(input, p);
            if (c >= 0x80)
            {
                return Adfa.Bail;
            }

            uint t = trans[at + cls[c]];
            if ((t & Adfa.FastMatched) != 0)
            {
                last = p;
            }

            uint next = t & ~Adfa.FastMatched;
            if (next == 0)
            {
                return last;
            }

            p++;
#if NET10_0_OR_GREATER
            if (next != at)
            {
                run = 0;
            }
            else if (++run == AccelAfter)
            {
                // A run in a state that loops: the rest of it is found by one search, after which the units are read
                // again one at a time.
                run = 0;
                int state = (int)(at >> this.shift);
                if (this.accel8[state] is { } members8)
                {
                    int k = typeof(TUnit) == typeof(byte)
                        ? System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(input).Slice(p).IndexOfAnyExcept(members8)
                        : System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(input).Slice(p).IndexOfAnyExcept(this.accel16[state]!);
                    int q = k < 0 ? input.Length : p + k;
                    if (q > p && this.accelMatched[state])
                    {
                        last = q - 1;
                    }

                    p = q;
                }
            }
#endif
            at = next;
        }

        return this.atEnd[at >> this.shift] ? p : last;
    }

    /// <summary>
    /// For the forward automaton: the first place at or after <paramref name="from"/> where a match ends, read from a
    /// start state at <paramref name="from"/>. <see cref="Adfa.ScanIdle"/> where a start state is reached again first
    /// (no match starts before it), <see cref="Adfa.ScanBail"/> at a unit beyond ASCII, <see cref="Adfa.ScanNone"/> at
    /// the end of the text with no match.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="at">Receives the place.</param>
    /// <returns>What was found.</returns>
    public int Scan<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int from, out int at)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        uint[] trans = this.fast;
        byte[] cls = this.classes;
        bool[] idle = this.startLike;
        int shift = this.shift;
        uint row = (uint)this.Start<TUnit, TEncoding>(input, from) << shift;
        int p = from;
        while (p < input.Length)
        {
            int c = enc.Unit(input, p);
            if (c >= 0x80)
            {
                at = p;
                return Adfa.ScanBail;
            }

            uint t = trans[row + cls[c]];
            if ((t & Adfa.FastMatched) != 0)
            {
                at = p;
                return Adfa.ScanMatch;
            }

            row = t;
            p++;
            if (idle[row >> shift])
            {
                at = p;
                return Adfa.ScanIdle;
            }
        }

        at = p;
        return this.atEnd[row >> shift] ? Adfa.ScanMatch : Adfa.ScanNone;
    }

    /// <summary>Whether any match starts at a position, found at the first place one ends: 1, 0, or <see cref="Adfa.Bail"/> where a unit beyond ASCII is reached first.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <returns>1 when one does, 0 when none does, or <see cref="Adfa.Bail"/>.</returns>
    public int MatchesAt<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int start)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        int s = this.Start<TUnit, TEncoding>(input, start);
        if (s == Adfa.Dead)
        {
            return 0;
        }

        TEncoding enc = default;
        uint[] trans = this.fast;
        byte[] cls = this.classes;
        uint at = (uint)s << this.shift;
        for (int p = start; p < input.Length; p++)
        {
            int c = enc.Unit(input, p);
            if (c >= 0x80)
            {
                return Adfa.Bail;
            }

            uint t = trans[at + cls[c]];
            if ((t & Adfa.FastMatched) != 0)
            {
                return 1;
            }

            at = t;
            if (at == 0)
            {
                return 0;
            }
        }

        return this.atEnd[at >> this.shift] ? 1 : 0;
    }

    /// <summary>The start state at a position: where the pattern has no boundary or anchor that looks back, the one start state.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Start<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int start)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (!this.context)
        {
            return this.starts[0];
        }

        bool word = Adfa.WordBefore<TUnit, TEncoding>(input, start);
        return this.starts[(word ? 2 : 0) | (start == 0 ? 1 : 0)];
    }
}

/// <summary>
/// An automaton of the terms before the literals of a search, read backwards (accepting where any of them can start):
/// from an occurrence of a literal it finds, over ASCII text, every start from which some alternative's terms reach it.
/// Only terms with no assertion are taken.
/// </summary>
internal sealed class ReverseAutomaton
{
    private readonly byte[] classes;
    private readonly int shift;

    /// <summary>The transitions, each the next state's row (premultiplied by the stride, so 0 is the dead state) with <see cref="Adfa.FastMatched"/> where that state accepts.</summary>
    private readonly uint[] fast;

    /// <summary>Whether the first state accepts (an empty run of terms).</summary>
    private readonly bool acceptFirst;

    /// <summary>Writes the automaton's tables into an image (see <see cref="RegexImage"/>).</summary>
    /// <param name="w">The writer.</param>
    internal void WriteTo(RegexImage.Writer w)
    {
        w.Bytes(this.classes);
        w.Int(this.shift);
        w.UInts(this.fast);
        w.Bool(this.acceptFirst);
    }

    /// <summary>Reads an automaton's tables from an image, checking that every state they name is one of its states.</summary>
    /// <param name="r">The reader, failed when the tables do not hold together.</param>
    /// <returns>The automaton, or null.</returns>
    internal static ReverseAutomaton? ReadFrom(ref RegexImage.Reader r)
    {
        byte[] classes = r.Bytes();
        int shift = r.Int();
        uint[] fast = r.UInts();
        bool acceptFirst = r.Bool();
        if (!r.Ok || shift < 0 || shift > 16 || !Adfa.TablesHold(classes, shift, fast, fast.Length >> shift))
        {
            r.Fail();
            return null;
        }

        return new ReverseAutomaton(classes, shift, fast, acceptFirst);
    }

    internal ReverseAutomaton(byte[] classes, int shift, uint[] fast, bool acceptFirst)
    {
        this.classes = classes;
        this.shift = shift;
        this.fast = fast;
        this.acceptFirst = acceptFirst;
    }

    /// <summary>
    /// The starts before <paramref name="q"/> the terms allow, as a mask of their distances (bit <c>d</c> is the start
    /// <c>q - d</c>), looking no further back than <paramref name="most"/> units (at most 63) nor before
    /// <paramref name="floor"/>; false where a unit beyond ASCII is reached.
    /// </summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="q">The occurrence.</param>
    /// <param name="most">The most units looked back over.</param>
    /// <param name="floor">The earliest start.</param>
    /// <param name="mask">Receives the starts.</param>
    /// <returns>Whether the units read were ASCII.</returns>
    public bool Starts<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int q, int most, int floor, out ulong mask)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        uint[] trans = this.fast;
        byte[] cls = this.classes;
        mask = this.acceptFirst ? 1UL : 0UL;
        uint at = 1u << this.shift;
        int n = Math.Min(most, q - floor);
        for (int d = 1; d <= n; d++)
        {
            int c = enc.Unit(input, q - d);
            if (c >= 0x80)
            {
                return false;
            }

            uint t = trans[at + cls[c]];
            if (t == 0)
            {
                break;
            }

            if ((t & Adfa.FastMatched) != 0)
            {
                mask |= 1UL << d;
            }

            at = t & ~Adfa.FastMatched;
        }

        return true;
    }
}

/// <summary>
/// The positions of a pattern read before a match can end: V8's Boyer-Moore lookahead (<c>BoyerMooreLookahead</c> in
/// its regexp compiler). For a window of positions <c>lo..=hi</c> of every match, the units each can be are joined; a
/// unit at <c>hi</c> from a place that is none of them rules out the starts from that place to <c>hi - lo</c> units
/// on, so the search reads one unit in <c>hi - lo + 1</c> where the window's units are rare.
/// </summary>
internal sealed class Skip(int hi, int length, bool[] table, bool wide)
{
    private readonly bool[] table = table;
    private readonly bool wide = wide;

    /// <summary>Writes the window into an image (see <see cref="RegexImage"/>).</summary>
    /// <param name="w">The writer.</param>
    internal void WriteTo(RegexImage.Writer w)
    {
        w.Int(this.Hi);
        w.Int(this.Length);
        w.Bools(this.table);
        w.Bool(this.wide);
    }

    /// <summary>Reads a window from an image.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The window, or null.</returns>
    internal static Skip? ReadFrom(ref RegexImage.Reader r)
    {
        int hi = r.Int();
        int length = r.Int();
        bool[] table = r.Bools();
        bool wide = r.Bool();
        if (!r.Ok || hi < 0 || length < 0)
        {
            r.Fail();
            return null;
        }

        return new Skip(hi, length, table, wide);
    }

    /// <summary>Gets the window's last position, the one read.</summary>
    public int Hi { get; } = hi;

    /// <summary>Gets the starts one unit rules out, <c>hi - lo + 1</c>.</summary>
    public int Length { get; } = length;

    /// <summary>The first position at or after <paramref name="at"/> that the window does not rule out.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="at">The position.</param>
    /// <returns>The position.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Ahead<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int at)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        TEncoding enc = default;
        int hi = this.Hi;
        int length = this.Length;
        bool[] t = this.table;
        while (at + hi < input.Length)
        {
            int c = enc.Unit(input, at + hi);
            if (c < 128 ? t[c] : this.wide)
            {
                break;
            }

            at += length;
        }

        return at;
    }
}

/// <summary>
/// The check of the rest of a pattern at an occurrence of its literal, the anchored automaton of the literal and the
/// terms after it, with counts of how often it rules an occurrence out: after <see cref="Trials"/> checks it is dropped
/// for good when fewer than one in eight of them ruled the occurrence out. The counts change only how fast a search
/// is, never what it finds, so a race between threads over them costs nothing.
/// </summary>
internal sealed class RestCheck(AnchoredAutomaton dfa)
{
    /// <summary>The checks made before it is decided whether the check pays.</summary>
    private const int Trials = 64;

    private readonly AnchoredAutomaton dfa = dfa;
    private int checks;
    private int rejects;

    /// <summary>Writes the check's automaton into an image (see <see cref="RegexImage"/>).</summary>
    /// <param name="w">The writer.</param>
    internal void WriteTo(RegexImage.Writer w) => this.dfa.WriteTo(w);

    /// <summary>Whether nothing from the occurrence at <paramref name="q"/> on can match.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="q">The occurrence.</param>
    /// <returns>Whether the occurrence is ruled out.</returns>
    public bool RulesOut<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int q)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        int made = this.checks;
        if (made >= Trials && this.rejects * 8 < Trials)
        {
            return false;
        }

        bool output = this.dfa.MatchesAt<TUnit, TEncoding>(input, q) == 0;
        if (made < Trials)
        {
            this.checks = made + 1;
            if (output)
            {
                this.rejects++;
            }
        }

        return output;
    }
}

/// <summary>The automata of a program's search (see <see cref="Program.Automata"/>), each null where the program has none.</summary>
internal sealed class SearchAutomata
{
    private SearchAutomata(AnchoredAutomaton? anchored, AnchoredAutomaton? forward, Skip? skip, ReverseAutomaton? reverse, RestCheck? rest)
    {
        this.Anchored = anchored;
        this.Forward = forward;
        this.Skip = skip;
        this.Reverse = reverse;
        this.Rest = rest;
    }

    /// <summary>Gets or sets a value indicating whether the programs whose automata are built on this thread have none, so that the tests can compare the searches the automata speed with the plain ones.</summary>
    [ThreadStatic]
    internal static bool Off;

    /// <summary>Writes the automata into an image (see <see cref="RegexImage"/>), each preceded by whether it is there.</summary>
    /// <param name="w">The writer.</param>
    internal void WriteTo(RegexImage.Writer w)
    {
        w.Bool(this.Anchored is not null);
        this.Anchored?.WriteTo(w);
        w.Bool(this.Forward is not null);
        this.Forward?.WriteTo(w);
        w.Bool(this.Skip is not null);
        this.Skip?.WriteTo(w);
        w.Bool(this.Reverse is not null);
        this.Reverse?.WriteTo(w);
        w.Bool(this.Rest is not null);
        this.Rest?.WriteTo(w);
    }

    /// <summary>Reads the automata from an image.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The automata, or null when the reader failed.</returns>
    internal static SearchAutomata? ReadFrom(ref RegexImage.Reader r)
    {
        AnchoredAutomaton? anchored = r.Bool() ? AnchoredAutomaton.ReadFrom(ref r, forward: false) : null;
        AnchoredAutomaton? forward = r.Bool() ? AnchoredAutomaton.ReadFrom(ref r, forward: true) : null;
        Skip? skip = r.Bool() ? Skip.ReadFrom(ref r) : null;
        ReverseAutomaton? reverse = r.Bool() ? ReverseAutomaton.ReadFrom(ref r) : null;
        RestCheck? rest = r.Bool() && AnchoredAutomaton.ReadFrom(ref r, forward: false) is { } check ? new RestCheck(check) : null;
        return r.Ok ? new SearchAutomata(anchored, forward, skip, reverse, rest) : null;
    }

    /// <summary>Gets the anchored automaton, which finds where the match from a start ends, for a program that tracks no group.</summary>
    public AnchoredAutomaton? Anchored { get; }

    /// <summary>Gets the forward automaton, for a search that tries every start its first set allows.</summary>
    public AnchoredAutomaton? Forward { get; }

    /// <summary>Gets the window of positions read one unit at a time before that search looks for the first set.</summary>
    public Skip? Skip { get; }

    /// <summary>Gets the reverse automaton of the terms before the literals of a windowed search.</summary>
    public ReverseAutomaton? Reverse { get; }

    /// <summary>Gets the check of the literals and the terms after them at an occurrence of one.</summary>
    public RestCheck? Rest { get; }

    /// <summary>Builds the automata of a program.</summary>
    /// <param name="p">The program.</param>
    /// <returns>The automata.</returns>
    public static SearchAutomata Build(Program p)
    {
        EcmaRegexNode? root = p.AutomatonRoot;
        if (root is null || Off)
        {
            return new(null, null, null, null, null);
        }

        AnchoredAutomaton? anchored = p.Groups == 0 ? Adfa.Build(root) : null;
        AnchoredAutomaton? forward = p.ForwardSearch ? Adfa.BuildForward(root) : null;
        Skip? skip = p.ForwardSearch ? Adfa.SkipWindow(root) : null;
        ReverseAutomaton? reverse = p.WindowPrefixes is { } prefixes ? Adfa.Reverse(prefixes) : null;
        RestCheck? rest = p.WindowRest is { } r && Adfa.Build(r) is { } check ? new RestCheck(check) : null;
        return new(anchored, forward, skip, reverse, rest);
    }
}
