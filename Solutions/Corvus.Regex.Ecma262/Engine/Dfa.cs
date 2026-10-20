// <copyright file="Dfa.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of dfa.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of dfa.go).

using System;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>What the automaton says of a text.</summary>
internal enum DfaResult : byte
{
    /// <summary>The pattern does not match the text.</summary>
    NoMatch,

    /// <summary>The pattern matches the text.</summary>
    Match,

    /// <summary>The text is not ASCII, which the automaton does not decide.</summary>
    Bail,
}

/// <summary>
/// A deterministic automaton over ASCII text, for the patterns that need no backtracking. The strings many
/// programs test are short and nearly always ASCII, and most patterns are small. For such a pattern every state set
/// the text can lead to is worked out once, and a match is then one table read per unit.
/// </summary>
/// <remarks>
/// The automaton reads units below 0x80 only. A text with any other unit is handed back to the backtracking
/// matcher from the start, so nothing here needs to know UTF-8 or the Unicode classes: a class is its ASCII
/// members. A word boundary is passed when the next unit is known, from what the state carries of the unit
/// before; a pattern with a boundary under the i and u flags together (where word characters fold), or whose
/// automaton has more states than a byte can number, has no automaton. The automaton is built on the first match, not when the pattern is compiled, so a program pays
/// nothing for the patterns it never matches.
/// </remarks>
internal sealed class Dfa
{
    // The states every automaton has. Dead: no match can follow. Accept: a match was found. Bail: the text is not
    // ASCII.
    private const byte DfaDead = 0;
    private const byte DfaAccept = 1;
    private const byte DfaBail = 2;
    private const int DfaFirst = 3;

    // An automaton has at most this many states (they are numbered in a byte), built from at most this many states
    // of the pattern, in at most this many steps (a step is one state of the pattern looked at while a set is
    // made). The steps bound what a pattern too large for an automaton costs before that is known, to about a
    // millisecond.
    private const int MaxDfaStates = 256;
    private const int MaxNfaStates = 512;
    private const long MaxDfaSteps = 400_000;

    private readonly byte[] classes = new byte[256];
    private byte[] trans = [];
    private int shift;
    private byte start;

    /// <summary>For each state: the pattern matches if the text ends there.</summary>
    private bool[] atEnd = [];

    /// <summary>The pattern matches the empty text.</summary>
    private bool empty;

    private Dfa()
    {
    }

    private enum NfaKind : byte
    {
        /// <summary>Reads one unit of its set and goes to <c>Out</c>.</summary>
        Set,

        /// <summary>Goes to <c>Out</c> and to <c>Alt</c>, reading nothing.</summary>
        Split,

        /// <summary>Goes to <c>Out</c> at the start of the text.</summary>
        Bol,

        /// <summary>Goes to <c>Out</c> at the end of the text.</summary>
        Eol,

        /// <summary>Goes to <c>Out</c> where exactly one of the units either side is a word character.</summary>
        WordBoundary,

        /// <summary>Goes to <c>Out</c> where both or neither of the units either side is a word character.</summary>
        NotWordBoundary,

        /// <summary>The match.</summary>
        Match,
    }

    /// <summary>Builds the automaton of a pattern for an unanchored search, or null when it has none.</summary>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The automaton, or null.</returns>
    public static Dfa? Build(EcmaRegexNode root)
    {
        NfaBuilder nb = new();
        int match = nb.Add(new NfaState(NfaKind.Match, 0));
        int start = nb.Compile(root, match);
        if (nb.Failed)
        {
            return null;
        }

        List<NfaState> nfa = nb.States;
        DfaBuilder b = new(nfa);
        Dfa d = new();

        // The classes of units: two units are in one class when every set of the pattern has both or neither.
        // Class 0 is the units that are not ASCII.
        int classes = 1;
        Dictionary<string, byte> signatures = [];
        char[] signature = new char[(nfa.Count / 8) + 1];
        for (int c = 0; c < 128; c++)
        {
            // Whether the unit is a word character is part of the class, since a boundary turns on it.
            int acc = Utf8Encoding.IsWordUnit(c) ? 1 : 0;
            int n = 1;
            int len = 0;
            foreach (NfaState s in nfa)
            {
                if (s.Kind != NfaKind.Set)
                {
                    continue;
                }

                acc = (acc << 1) | (s.Has(c) ? 1 : 0);
                n++;
                if (n == 8)
                {
                    signature[len++] = (char)acc;
                    acc = 0;
                    n = 0;
                }
            }

            signature[len++] = (char)acc;
            string key = new(signature, 0, len);
            if (!signatures.TryGetValue(key, out byte cls))
            {
                cls = (byte)classes++;
                signatures[key] = cls;
            }

            d.classes[c] = cls;
        }

        while (1 << d.shift < classes)
        {
            d.shift++;
        }

        // One unit of each class, to move a set by.
        int[] sample = new int[129];
        for (int c = 127; c >= 0; c--)
        {
            sample[d.classes[c]] = c;
        }

        // The search is unanchored: a match may start at any position, so after every unit the states the pattern
        // starts in (not at the start of the text) join the set.
        List<int> restart = [];
        b.Closure(restart, [start], false, false, null, null);
        List<int> atBoth = [];
        b.Closure(atBoth, [start], true, true, false, false);
        d.empty = b.HasMatch(atBoth);

        Dictionary<string, byte> ids = [];
        List<int[]> sets = [[], [], []];
        int stride = 1 << d.shift;
        List<byte> trans = new(DfaFirst * stride);
        List<bool> atEnd = [false, false, false];
        for (int k = 0; k < DfaFirst * stride; k++)
        {
            trans.Add(0);
        }

        for (int cls = 0; cls < stride; cls++)
        {
            trans[(DfaAccept * stride) + cls] = DfaAccept;
            trans[(DfaBail * stride) + cls] = DfaBail;
        }

        // A state is a set of the pattern's states, whether the unit before was a word character, which a
        // boundary still to pass turns on (the boundaries are passed when the next unit is known), and whether
        // it is at the start of the text, where a start anchor after a boundary still passes.
        List<bool> wordBefore = [false, false, false];
        List<bool> atStart = [false, false, false];
        List<int> first = [];
        b.Closure(first, [start], true, false, false, null);
        byte? s0 = Intern(b, ids, sets, trans, atEnd, wordBefore, atStart, stride, first, false, true);
        if (s0 is null)
        {
            return null;
        }

        d.start = s0.Value;
        List<int> resolved = [];
        List<int> moved = [];
        List<int> next = [];
        for (int id = DfaFirst; id < sets.Count; id++)
        {
            for (int cls = 0; cls < stride; cls++)
            {
                if (cls == 0 || cls >= classes)
                {
                    // Not ASCII (or no such class).
                    trans[(id * stride) + cls] = DfaBail;
                    continue;
                }

                int c = sample[cls];
                bool word = Utf8Encoding.IsWordUnit(c);
                resolved.Clear();
                b.Closure(resolved, sets[id], atStart[id], false, wordBefore[id], word);
                if (b.HasMatch(resolved))
                {
                    // A boundary before the unit let the match through: the match is at this position.
                    trans[(id * stride) + cls] = DfaAccept;
                    continue;
                }

                moved.Clear();
                foreach (int i in resolved)
                {
                    NfaState s = nfa[i];
                    if (s.Kind == NfaKind.Set && s.Has(c))
                    {
                        moved.Add(s.Out);
                    }
                }

                moved.AddRange(restart);
                next.Clear();
                b.Closure(next, moved, false, false, word, null);
                byte? to = Intern(b, ids, sets, trans, atEnd, wordBefore, atStart, stride, next, word, false);
                if (to is null || b.Steps < 0)
                {
                    return null;
                }

                trans[(id * stride) + cls] = to.Value;
            }
        }

        d.trans = [.. trans];
        d.atEnd = [.. atEnd];
        return d;
    }

    /// <summary>Reports whether the pattern matches the text, or that the text is not ASCII.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <returns>The result.</returns>
    public DfaResult IsMatch<TUnit, TEncoding>(ReadOnlySpan<TUnit> input)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (input.Length == 0)
        {
            return this.empty ? DfaResult.Match : DfaResult.NoMatch;
        }

        int s = this.start;
        if (s == DfaAccept)
        {
            return DfaResult.Match;
        }

        TEncoding enc = default;
        byte[] trans = this.trans;
        byte[] classes = this.classes;
        int shift = this.shift;
        for (int k = 0; k < input.Length; k++)
        {
            int c = enc.Unit(input, k);
            if (c >= 256)
            {
                return DfaResult.Bail;
            }

            s = trans[(s << shift) | classes[c]];
            if (s < DfaFirst)
            {
                return s == DfaBail ? DfaResult.Bail : (s == DfaAccept ? DfaResult.Match : DfaResult.NoMatch);
            }
        }

        return this.atEnd[s] ? DfaResult.Match : DfaResult.NoMatch;
    }

    /// <summary>Gives a set its state. Null when there are too many.</summary>
    private static byte? Intern(DfaBuilder b, Dictionary<string, byte> ids, List<int[]> sets, List<byte> trans, List<bool> atEnd, List<bool> wordBefore, List<bool> atStart, int stride, List<int> set, bool word, bool start)
    {
        if (b.HasMatch(set))
        {
            return DfaAccept;
        }

        if (set.Count == 0)
        {
            return DfaDead;
        }

        set.Sort();
        char[] keyChars = new char[set.Count + 1];
        keyChars[0] = (char)((word ? 'w' : 'n') + (start ? 2 : 0));
        for (int k = 0; k < set.Count; k++)
        {
            keyChars[k + 1] = (char)set[k];
        }

        string key = new(keyChars);
        if (ids.TryGetValue(key, out byte id))
        {
            return id;
        }

        if (sets.Count >= MaxDfaStates)
        {
            return null;
        }

        id = (byte)sets.Count;
        ids[key] = id;
        sets.Add([.. set]);
        wordBefore.Add(word);
        atStart.Add(start);
        for (int k = 0; k < stride; k++)
        {
            trans.Add(0);
        }

        // At the end of the text the end anchors are passed, and a boundary sees no word character after.
        List<int> closed = [];
        b.Closure(closed, set, start, true, word, false);
        atEnd.Add(b.HasMatch(closed));
        return id;
    }

    private struct NfaState
    {
        public NfaState(NfaKind kind, int output)
        {
            this.Kind = kind;
            this.Out = output;
        }

        public NfaKind Kind { get; set; }

        public ulong Set0 { get; set; }

        public ulong Set1 { get; set; }

        public int Out { get; set; }

        public int Alt { get; set; }

        public readonly bool Has(int c) => c < 64 ? ((this.Set0 >> c) & 1) != 0 : ((this.Set1 >> (c - 64)) & 1) != 0;
    }

    private sealed class NfaBuilder
    {
        public List<NfaState> States { get; } = [];

        /// <summary>Gets or sets a value indicating whether the pattern has something an automaton over units cannot decide, or is too large.</summary>
        public bool Failed { get; set; }

        public int Add(NfaState s)
        {
            if (this.States.Count >= MaxNfaStates)
            {
                this.Failed = true;
                return 0;
            }

            this.States.Add(s);
            return this.States.Count - 1;
        }

        /// <summary>Adds the states of a node that go on to <paramref name="next"/>, and returns the state they start at.</summary>
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
                    NfaState s = new(NfaKind.Set, next);
                    if (n.CodePoint >= 0 && n.CodePoint < 128)
                    {
                        if (n.CodePoint < 64)
                        {
                            s.Set0 = 1UL << n.CodePoint;
                        }
                        else
                        {
                            s.Set1 = 1UL << (n.CodePoint - 64);
                        }
                    }

                    return this.Add(s);
                }

                case EcmaRegexNodeKind.Set:
                {
                    CharSet set = new(n.Set);
                    return this.Add(new NfaState(NfaKind.Set, next) { Set0 = set.Ascii0, Set1 = set.Ascii1 });
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
                        start = this.Add(new NfaState(NfaKind.Split, output) { Alt = start });
                    }

                    return start;
                }

                case EcmaRegexNodeKind.Group:
                    return this.Compile(n.Subs[0], next);
                case EcmaRegexNodeKind.Repeat:
                {
                    EcmaRegexNode sub = n.Subs[0];
                    if (n.Max == EcmaRegexNode.Unbounded)
                    {
                        // The loop, then the copies that must match before it.
                        int loop = this.Add(new NfaState(NfaKind.Split, 0) { Alt = next });
                        if (this.Failed)
                        {
                            return 0;
                        }

                        int output = this.Compile(sub, loop);
                        NfaState s = this.States[loop];
                        s.Out = output;
                        this.States[loop] = s;
                        next = loop;
                    }
                    else
                    {
                        // The copies that may match, innermost last.
                        for (int k = n.Min; k < n.Max && !this.Failed; k++)
                        {
                            int output = this.Compile(sub, next);
                            next = this.Add(new NfaState(NfaKind.Split, output) { Alt = next });
                        }
                    }

                    for (int k = 0; k < n.Min && !this.Failed; k++)
                    {
                        next = this.Compile(sub, next);
                    }

                    return next;
                }

                case EcmaRegexNodeKind.Bol:
                    return this.Add(new NfaState(NfaKind.Bol, next));
                case EcmaRegexNodeKind.Eol:
                    return this.Add(new NfaState(NfaKind.Eol, next));
                case EcmaRegexNodeKind.WordBoundary when !n.Fold:
                    return this.Add(new NfaState(NfaKind.WordBoundary, next));
                case EcmaRegexNodeKind.NotWordBoundary when !n.Fold:
                    return this.Add(new NfaState(NfaKind.NotWordBoundary, next));
                default:
                    // A word boundary under the i and u flags together (which folds), or anything that needs the
                    // backtracking matcher.
                    this.Failed = true;
                    return 0;
            }
        }
    }

    /// <summary>Makes the automaton's states from sets of the pattern's states.</summary>
    private sealed class DfaBuilder(List<NfaState> nfa)
    {
        // Scratch for closure: the states visited, by generation, and the stack.
        private readonly uint[] mark = new uint[nfa.Count];
        private readonly Stack<int> stack = new();
        private uint generation;

        public List<NfaState> Nfa { get; } = nfa;

        /// <summary>Gets the steps left (see <see cref="MaxDfaSteps"/>).</summary>
        public long Steps { get; private set; } = MaxDfaSteps;

        /// <summary>
        /// Adds to <paramref name="set"/> every state that reading nothing reaches from the states in
        /// <paramref name="from"/>, keeping the states that read a unit, the end anchors and word boundaries still
        /// to pass, and the match. A start anchor is passed only <paramref name="atStart"/>, an end anchor only
        /// <paramref name="atEnd"/>, and a word boundary only when whether the units before and after are word
        /// characters is known (<paramref name="wordBefore"/> and <paramref name="wordAfter"/>), and it holds.
        /// </summary>
        public void Closure(List<int> set, IEnumerable<int> from, bool atStart, bool atEnd, bool? wordBefore, bool? wordAfter)
        {
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
                NfaState s = this.Nfa[i];
                switch (s.Kind)
                {
                    case NfaKind.Split:
                        this.stack.Push(s.Out);
                        this.stack.Push(s.Alt);
                        break;
                    case NfaKind.Bol:
                        if (atStart)
                        {
                            this.stack.Push(s.Out);
                        }

                        break;
                    case NfaKind.Eol:
                        if (atEnd)
                        {
                            this.stack.Push(s.Out);
                        }
                        else
                        {
                            set.Add(i);
                        }

                        break;
                    case NfaKind.WordBoundary:
                    case NfaKind.NotWordBoundary:
                        if (wordBefore is null || wordAfter is null)
                        {
                            set.Add(i);
                        }
                        else if ((wordBefore.Value != wordAfter.Value) == (s.Kind == NfaKind.WordBoundary))
                        {
                            this.stack.Push(s.Out);
                        }

                        break;
                    default:
                        set.Add(i);
                        break;
                }
            }
        }

        public bool HasMatch(List<int> set)
        {
            foreach (int i in set)
            {
                if (this.Nfa[i].Kind == NfaKind.Match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
