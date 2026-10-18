// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of compile.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of compile.go).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The instructions of the backtracking matcher.</summary>
internal enum Op : byte
{
    /// <summary><see cref="Char"/> and <see cref="Set"/> consume one code point going forward.</summary>
    Char,

    /// <summary>Consumes the code point before the position when it is the character, which is how the body of a lookbehind runs.</summary>
    CharBack,

    /// <summary>Consumes one code point of the set.</summary>
    Set,

    /// <summary>Consumes the code point before the position when it is in the set.</summary>
    SetBack,

    /// <summary>Consumes a run of literal text going forward.</summary>
    Lit,

    /// <summary>Fails.</summary>
    Fail,

    /// <summary>Holds at the start of the text.</summary>
    Bol,

    /// <summary>Holds at the end of the text.</summary>
    Eol,

    /// <summary>Holds at a word boundary.</summary>
    WordBoundary,

    /// <summary>Holds away from a word boundary.</summary>
    NotWordBoundary,

    /// <summary><c>^</c> with the m modifier.</summary>
    LineStart,

    /// <summary><c>$</c> with the m modifier.</summary>
    LineEnd,

    /// <summary>A word boundary inside a case-insensitive group of a pattern with the u flag.</summary>
    WordBoundaryFold,

    /// <summary>Away from a word boundary inside a case-insensitive group of a pattern with the u flag.</summary>
    NotWordBoundaryFold,

    /// <summary>Continues at <see cref="Inst.X"/>.</summary>
    Jmp,

    /// <summary>Continues at <see cref="Inst.X"/> and leaves <see cref="Inst.Y"/> as the alternative.</summary>
    Split,

    /// <summary>Notes where a group was entered.</summary>
    SaveTmp,

    /// <summary>Records the group once it has closed.</summary>
    Capture,

    /// <summary>Resets the groups <see cref="Inst.A"/> to <see cref="Inst.B"/>, as ECMA-262 does at the start of each iteration of a quantified body.</summary>
    Clear,

    /// <summary>Consumes the text a group captured.</summary>
    Backref,

    /// <summary>Consumes the text a group captured, going backward.</summary>
    BackrefBack,

    /// <summary>Consumes the text a group captured, with case folded.</summary>
    BackrefFold,

    /// <summary>Consumes the text a group captured, with case folded, going backward.</summary>
    BackrefFoldBack,

    /// <summary>Consumes the text a group captured, a code unit at a time, for a pattern matched by code unit.</summary>
    BackrefUnits,

    /// <summary>Consumes the text a group captured, a code unit at a time, going backward.</summary>
    BackrefUnitsBack,

    /// <summary>Opens a lookaround.</summary>
    Look,

    /// <summary>Ends the body of a positive lookaround.</summary>
    LookOk,

    /// <summary>Ends the body of a negative lookaround.</summary>
    LookFail,

    /// <summary>Repeats one character or class greedily without entering the general loop.</summary>
    Rep,

    /// <summary>Repeats one character or class lazily without entering the general loop.</summary>
    RepLazy,

    /// <summary>Starts a counted or possibly empty quantified body.</summary>
    LoopInit,

    /// <summary>Decides whether to enter or leave a counted loop.</summary>
    Loop,

    /// <summary>Counts an iteration of a loop.</summary>
    LoopIter,

    /// <summary>The match.</summary>
    Match,
}

/// <summary>One instruction of the backtracking matcher.</summary>
internal struct Inst
{
    /// <summary>Initializes a new instance of the <see cref="Inst"/> struct.</summary>
    /// <param name="op">The instruction.</param>
    public Inst(Op op)
    {
        this.Op = op;
        this.Lit = -1;
    }

    /// <summary>Gets or sets the instruction.</summary>
    public Op Op { get; set; }

    /// <summary>Gets or sets a value that marks a lazy <see cref="Op.Loop"/>, a backward <see cref="Op.Capture"/>, a negative <see cref="Op.Look"/>, and a <see cref="Op.BackrefFold"/> that folds case as the u flag grammar does.</summary>
    public bool Flag { get; set; }

    /// <summary>Gets or sets the code point of a <see cref="Op.Char"/>, a <see cref="Op.Rep"/> or a <see cref="Op.RepLazy"/>, and the first code point of the literal of a <see cref="Op.Lit"/>.</summary>
    public int R { get; set; }

    /// <summary>Gets or sets the set of a <see cref="Op.Set"/>, a <see cref="Op.Rep"/> or a <see cref="Op.RepLazy"/>.</summary>
    public CharSet? Set { get; set; }

    /// <summary>Gets or sets the number of the literal of a <see cref="Op.Lit"/>, or -1.</summary>
    public int Lit { get; set; }

    /// <summary>Gets or sets the number of code points of the literal of a <see cref="Op.Lit"/>.</summary>
    public int Len { get; set; }

    /// <summary>Gets or sets a jump target.</summary>
    public int X { get; set; }

    /// <summary>Gets or sets a jump target.</summary>
    public int Y { get; set; }

    /// <summary>Gets or sets a group, a loop or the first of a range of groups, depending on the instruction.</summary>
    public int A { get; set; }

    /// <summary>Gets or sets the last of a range of groups.</summary>
    public int B { get; set; }

    /// <summary>Gets or sets the least count of a repetition.</summary>
    public int Min { get; set; }

    /// <summary>Gets or sets the greatest count of a repetition, or -1 for no bound.</summary>
    public int Max { get; set; }

    /// <summary>
    /// Gets or sets the least number of characters of text that every way from this instruction to a match consumes
    /// going forward (see <see cref="Compiler.SetNeeds"/>). It is set for a <see cref="Op.Loop"/>, and counts none
    /// of the iterations the loop has still to make.
    /// </summary>
    public int Need { get; set; }

    /// <summary>Gets or sets the least number of characters one iteration of the body of a <see cref="Op.Loop"/> consumes going forward.</summary>
    public int Iter { get; set; }

    /// <summary>Gets or sets the number of the instruction's memo point, counted from 1, or 0 when it has none (see <see cref="MemoPoint"/>). It is set for a <see cref="Op.Split"/>, a <see cref="Op.Loop"/>, a <see cref="Op.Rep"/> and a <see cref="Op.RepLazy"/>.</summary>
    public int M { get; set; }
}

/// <summary>
/// A place where the matcher makes a choice and can remember that the choice has failed: a split, a loop that may
/// be entered or left, and what follows a single character loop that gives characters back or takes more. Whether a
/// way from such a place leads to a match depends on the place and the position alone when nothing else the matcher
/// keeps can change what follows. So a pattern with a backreference has no memo points (what a group holds changes
/// what follows), and neither has a place inside a loop that counts its iterations up to a bound. Inside a loop
/// with no upper bound and a least count of zero or one, one thing more decides what follows: whether the loop
/// would refuse the iteration as empty if it ended here. That is one bit for each such loop the place is in
/// (innermost last), and a place in more than <see cref="Compiler.MaxMemoLoops"/> of them has no memo point.
/// </summary>
internal struct MemoPoint
{
    /// <summary>Gets or sets the outer loop, or -1.</summary>
    public int Loop0 { get; set; }

    /// <summary>Gets or sets the inner loop, or -1.</summary>
    public int Loop1 { get; set; }

    /// <summary>Gets or sets the least count of the outer loop.</summary>
    public int Min0 { get; set; }

    /// <summary>Gets or sets the least count of the inner loop.</summary>
    public int Min1 { get; set; }
}

/// <summary>
/// What can come next on a way from an instruction to the end of its match, which is the end of the pattern or of
/// the lookaround the instruction is in. The matcher looks at it before it takes a way: when the next character of
/// the text is none that the way can start with, and the way cannot end without one, the way fails, so it is not
/// taken, and where it was one of two no choice is kept for it. V8 makes the same check ("quick check") at its
/// choices. It is what keeps a choice between ways that need different characters from being a choice.
/// </summary>
internal struct Quick : IEquatable<Quick>
{
    /// <summary>Gets or sets the characters 0 to 63 a way can consume next, one bit each.</summary>
    public ulong Ascii0 { get; set; }

    /// <summary>Gets or sets the characters 64 to 127 a way can consume next, one bit each.</summary>
    public ulong Ascii1 { get; set; }

    /// <summary>Gets or sets a value indicating whether a way can consume a character that is not ASCII.</summary>
    public bool Other { get; set; }

    /// <summary>Gets or sets a value indicating whether a way reaches the end of its match consuming nothing more while text is left.</summary>
    public bool Mid { get; set; }

    /// <summary>Gets or sets a value indicating whether a way reaches the end of its match at the end of the text.</summary>
    public bool End { get; set; }

    /// <summary>Gets the quick that allows everything.</summary>
    public static Quick All => new() { Ascii0 = ~0UL, Ascii1 = ~0UL, Other = true, Mid = true, End = true };

    /// <summary>Whether a way can go on from the position.</summary>
    /// <typeparam name="TUnit">The unit of the text.</typeparam>
    /// <typeparam name="TEncoding">The encoding of the text.</typeparam>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Allows<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, int pos)
        where TUnit : unmanaged, IEquatable<TUnit>
        where TEncoding : struct, ITextEncoding<TUnit>
    {
        if (pos >= input.Length)
        {
            return this.End;
        }

        if (this.Mid)
        {
            return true;
        }

        int c = default(TEncoding).Unit(input, pos);
        if (c < 0x80)
        {
            return c < 64 ? ((this.Ascii0 >> c) & 1) != 0 : ((this.Ascii1 >> (c - 64)) & 1) != 0;
        }

        return this.Other;
    }

    /// <summary>Adds a code point.</summary>
    /// <param name="r">The code point.</param>
    public void AddRune(int r)
    {
        if (r >= 0 && r < 0x80)
        {
            if (r < 64)
            {
                this.Ascii0 |= 1UL << r;
            }
            else
            {
                this.Ascii1 |= 1UL << (r - 64);
            }
        }
        else
        {
            this.Other = true;
        }
    }

    /// <summary>Adds a set.</summary>
    /// <param name="s">The set.</param>
    public void AddSet(CharSet s)
    {
        this.Ascii0 |= s.Ascii0;
        this.Ascii1 |= s.Ascii1;
        if (s.HasNonAscii)
        {
            this.Other = true;
        }
    }

    /// <summary>Adds everything another quick allows.</summary>
    /// <param name="o">The other.</param>
    public void Union(Quick o)
    {
        this.Ascii0 |= o.Ascii0;
        this.Ascii1 |= o.Ascii1;
        this.Other |= o.Other;
        this.Mid |= o.Mid;
        this.End |= o.End;
    }

    /// <summary>Keeps only what both this and another quick allow.</summary>
    /// <param name="b">The other.</param>
    public void Intersect(Quick b)
    {
        this.Ascii0 &= b.Ascii0;
        this.Ascii1 &= b.Ascii1;
        this.Other &= b.Other;
        this.Mid &= b.Mid;
        this.End &= b.End;
    }

    /// <inheritdoc/>
    public bool Equals(Quick other) => this.Ascii0 == other.Ascii0 && this.Ascii1 == other.Ascii1 && this.Other == other.Other && this.Mid == other.Mid && this.End == other.End;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Quick other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (this.Ascii0.GetHashCode() * 31) + this.Ascii1.GetHashCode();
}

/// <summary>A pattern compiled for the backtracking matcher.</summary>
internal sealed class Program
{
    /// <summary>Initializes a new instance of the <see cref="Program"/> class.</summary>
    /// <param name="insts">The instructions.</param>
    /// <param name="groups">The number of tracked groups.</param>
    /// <param name="loops">The number of counted loops.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <param name="anchored">Whether every match starts at the start of the text.</param>
    /// <param name="prefix">Literal text every match starts with, or empty.</param>
    /// <param name="need">The least number of characters a match consumes.</param>
    /// <param name="points">The memo points.</param>
    /// <param name="quick">The quick of each instruction.</param>
    /// <param name="first">Every code point a match can start with, or null.</param>
    /// <param name="utf8Literals">The literals as UTF-8.</param>
    /// <param name="utf16Literals">The literals as UTF-16.</param>
    public Program(Inst[] insts, int groups, int loops, bool units, bool anchored, string prefix, int need, MemoPoint[] points, Quick[] quick, CharSet? first, byte[][] utf8Literals, string[] utf16Literals)
    {
        this.Insts = insts;
        this.Groups = groups;
        this.Loops = loops;
        this.Units = units;
        this.Anchored = anchored;
        this.Utf16Prefix = prefix;
        this.Utf8Prefix = Encoding.UTF8.GetBytes(prefix);
        this.Need = need;
        this.Points = points;
        this.Quick = quick;
        this.First = first;
        this.Utf8Literals = utf8Literals;
        this.Utf16Literals = utf16Literals;
    }

    /// <summary>Gets the instructions.</summary>
    public Inst[] Insts { get; }

    /// <summary>Gets the number of capturing groups the program tracks: those some backreference names.</summary>
    public int Groups { get; }

    /// <summary>Gets the number of counted loops.</summary>
    public int Loops { get; }

    /// <summary>Gets a value indicating whether the pattern was read without the u flag, so the text is matched by UTF-16 code unit.</summary>
    public bool Units { get; }

    /// <summary>Gets a value indicating whether every match starts at the start of the text.</summary>
    public bool Anchored { get; }

    /// <summary>Gets the literal text every match starts with, or empty.</summary>
    public string Utf16Prefix { get; }

    /// <summary>Gets the literal text every match starts with, as UTF-8.</summary>
    public byte[] Utf8Prefix { get; }

    /// <summary>Gets the least number of characters a match consumes (see <see cref="Compiler.SetNeeds"/>).</summary>
    public int Need { get; }

    /// <summary>Gets the memo points of the program (see <see cref="MemoPoint"/>).</summary>
    public MemoPoint[] Points { get; }

    /// <summary>Gets, for each instruction, which characters can come next on a way from it to the end of its match (see <see cref="Compiler.SetQuick"/>).</summary>
    public Quick[] Quick { get; }

    /// <summary>Gets every code point a match can start with, or null when a match can be empty or when the set is not known.</summary>
    public CharSet? First { get; }

    /// <summary>Gets the literals as UTF-8.</summary>
    public byte[][] Utf8Literals { get; }

    /// <summary>Gets the literals as UTF-16.</summary>
    public string[] Utf16Literals { get; }
}

/// <summary>Compiles a syntax tree to a <see cref="Program"/>.</summary>
internal sealed class Compiler
{
    /// <summary>The number of loops whose state a memo point can tell apart.</summary>
    public const int MaxMemoLoops = 2;

    /// <summary>The need of an instruction from which no way leads to a match. Every need is capped at it.</summary>
    public const long NoMatch = 1L << 30;

    private readonly List<Inst> insts = [];

    /// <summary>Maps a capturing group's number to its slot, for the groups some backreference names. The others are not recorded at all.</summary>
    private readonly Dictionary<int, int> tracked = [];
    private readonly List<MemoPoint> points = [];
    private readonly List<LoopContext> open = [];
    private readonly List<byte[]> utf8Literals = [];
    private readonly List<string> utf16Literals = [];
    private readonly Dictionary<int[], CharSet> sets = new(ReferenceComparer.Instance);
    private readonly bool unicode;
    private int loops;

    /// <summary><see langword="true"/> says the pattern has no backreference, so its choices can have memo points.</summary>
    private bool memo;

    private Compiler(bool unicode)
    {
        this.unicode = unicode;
    }

    /// <summary>Compiles a tree. The tree is changed in place by <see cref="Simplify"/>.</summary>
    /// <param name="root">The root of the tree.</param>
    /// <param name="unicode">Whether the pattern was read with the u flag grammar.</param>
    /// <param name="groupCount">The number of capturing groups of the pattern.</param>
    /// <param name="trackAll">
    /// Whether every capturing group is tracked, with group <c>g</c> in slot <c>g - 1</c>, so that a match can
    /// report what each captured. Otherwise only the groups some backreference names are tracked, which is all a
    /// yes-or-no answer needs. The memory of failed places is sound either way: it prunes only ways that fail, and
    /// the first way that succeeds, with what its groups hold, is the same.
    /// </param>
    /// <returns>The program.</returns>
    public static Program Compile(EcmaRegexNode root, bool unicode, int groupCount, bool trackAll)
    {
        List<int> openGroups = [];
        root = Simplify(root, openGroups);
        Compiler c = new(unicode);
        List<int> referenced = [];
        CollectBackrefs(root, referenced);
        if (trackAll)
        {
            for (int g = 1; g <= groupCount; g++)
            {
                c.tracked[g] = g - 1;
            }
        }
        else
        {
            // Slots follow group order, so the groups inside any one subtree take a contiguous range of slots.
            referenced.Sort();
            int slot = 0;
            for (int k = 0; k < referenced.Count; k++)
            {
                if (k > 0 && referenced[k] == referenced[k - 1])
                {
                    continue;
                }

                c.tracked[referenced[k]] = slot++;
            }
        }

        c.memo = referenced.Count == 0;
        c.Emit(root, false);
        c.Add(new Inst(Op.Match));
        Inst[] insts = [.. c.insts];
        int need = SetNeeds(insts);
        Quick[] quick = SetQuick(insts);
        bool anchored = StartsAtBol(root);
        string prefix = string.Empty;
        CharSet? first = null;
        if (!anchored)
        {
            StringBuilder sb = new();
            LiteralPrefix(root, sb);
            prefix = sb.ToString();
            if (prefix.Length == 0)
            {
                List<int> pairs = [];
                bool? nullable = FirstChars(c, root, pairs);
                if (nullable == false)
                {
                    first = new CharSet(EcmaRegexUnicode.SetOf([.. pairs], pairs.Count));
                }
            }
        }

        return new Program(insts, c.tracked.Count, c.loops, !unicode, anchored, prefix, need, [.. c.points], quick, first, [.. c.utf8Literals], [.. c.utf16Literals]);
    }

    /// <summary>
    /// Returns the tree with two rewrites, both of which V8 makes. Neither changes what the pattern matches or what
    /// any group holds. Without them a pattern can take time exponential in its counts for no reason that ECMA-262
    /// gives.
    /// </summary>
    /// <remarks>
    /// A backreference to a group it is inside of is the empty string. The group is not set until it closes, and the
    /// repetition around a group clears it at the start of each iteration, so inside the group it is never set.
    /// A repetition whose body cannot consume a character, such as <c>(?:\b|(?=a)|){4,5}</c>, is the body once when
    /// its minimum is one or more, and nothing when its minimum is zero. Every iteration of such a body is empty.
    /// ECMA-262 refuses an empty iteration after the minimum, so the iterations are exactly the minimum. Each of
    /// them starts with the groups of the body cleared and at the same position, so what the last one can do does
    /// not depend on the ones before it, and the ways through the last one come in the same order whatever the
    /// others chose. Run as written, the matcher would try every combination of the ways through every iteration.
    /// </remarks>
    /// <param name="n">The node.</param>
    /// <param name="open">The capturing groups the node is inside of.</param>
    /// <returns>The node, rewritten.</returns>
    public static EcmaRegexNode Simplify(EcmaRegexNode n, List<int> open)
    {
        if (n.Kind == EcmaRegexNodeKind.Backref)
        {
            bool all = true;
            foreach (int g in n.Groups)
            {
                if (!open.Contains(g))
                {
                    all = false;
                    break;
                }
            }

            return all ? new EcmaRegexNode(EcmaRegexNodeKind.Empty) : n;
        }

        if (n.Subs.Length == 0)
        {
            return n;
        }

        bool opened = n.Kind == EcmaRegexNodeKind.Group && n.Group > 0;
        if (opened)
        {
            open.Add(n.Group);
        }

        for (int k = 0; k < n.Subs.Length; k++)
        {
            n.Subs[k] = Simplify(n.Subs[k], open);
        }

        if (opened)
        {
            open.RemoveAt(open.Count - 1);
        }

        if (n.Kind == EcmaRegexNodeKind.Repeat && n.Max != 0 && !Consumes(n.Subs[0]))
        {
            return n.Min == 0 ? new EcmaRegexNode(EcmaRegexNodeKind.Empty) : n.Subs[0];
        }

        return n;
    }

    /// <summary>
    /// Works out, for every instruction, the least number of characters of text that any way from it to a match
    /// consumes going forward, and writes it into the counted loops. It returns the need of the whole program. The
    /// matcher gives up at a counted loop when fewer characters than that are left, as V8 does at every choice. A
    /// loop whose iterations can be empty would otherwise try every combination of its ways at a position from
    /// which the rest of the pattern cannot match at all.
    /// </summary>
    /// <remarks>
    /// A counted loop that is entered needs its least count of iterations, each of them the least its body
    /// consumes (<see cref="Inst.Iter"/>), and then what follows it. The loop instruction itself is also reached
    /// after each iteration, so its own need counts only what follows, and the matcher adds the iterations it has
    /// still to make. A character is counted as one unit whatever its encoding, a literal run as its characters and
    /// a single character loop as its minimum, so the figure can only be too small. The body of a lookaround ends
    /// at its own last instruction and the instruction that opens one goes straight to what follows it, so a body
    /// is counted by itself. An instruction that matches right to left, which is the body of a lookbehind, counts
    /// nothing.
    /// </remarks>
    /// <param name="insts">The instructions.</param>
    /// <returns>The need of the whole program.</returns>
    public static int SetNeeds(Inst[] insts) => Needs(insts, out _);

    /// <summary><see cref="SetNeeds"/>, and also the number of passes over the instructions it made, the last of which changed nothing.</summary>
    /// <param name="insts">The instructions.</param>
    /// <param name="passes">The passes.</param>
    /// <returns>The need of the whole program.</returns>
    public static int Needs(Inst[] insts, out int passes)
    {
        int[] need = new int[insts.Length];
        for (int k = 0; k < need.Length; k++)
        {
            need[k] = (int)NoMatch;
        }

        passes = 0;

        // The needs only fall, and nearly every way goes forward, so going through the instructions from the last
        // to the first settles all but the loops in one pass.
        bool changed = true;
        while (changed)
        {
            changed = false;
            passes++;
            for (int pc = insts.Length - 1; pc >= 0; pc--)
            {
                Inst i = insts[pc];
                long n = NoMatch;
                int after = pc + 1;
                switch (i.Op)
                {
                    case Op.Match:
                    case Op.LookOk:
                    case Op.LookFail:
                        n = 0;
                        break;
                    case Op.Fail:
                        break;
                    case Op.Char:
                    case Op.Set:
                        n = Next(need, after, 1, n);
                        break;
                    case Op.Lit:
                        n = Next(need, after, i.Len, n);
                        break;
                    case Op.Rep:
                    case Op.RepLazy:
                        n = Next(need, after, i.Min, n);
                        break;
                    case Op.Jmp:
                    {
                        // The one way that goes back is the jump at the end of the body of a loop, to the
                        // instruction at its head, which this pass has not reached. The need of that instruction is
                        // the need of what follows the loop, since its other way, through the body, comes back to
                        // it and cannot need less. So the jump takes what follows the loop, which this pass has
                        // settled. Taking the need of the head would settle one loop of a nest for each pass: a
                        // thousand passes for loops a thousand deep.
                        Inst head = insts[i.X];
                        if (i.X < pc && head.Op == Op.Loop)
                        {
                            n = Next(need, head.X, 0, n);
                        }
                        else if (i.X < pc && head.Op == Op.Split)
                        {
                            n = Next(need, head.X, 0, n);
                            n = Next(need, head.Y, 0, n);
                        }
                        else
                        {
                            n = Next(need, i.X, 0, n);
                        }

                        break;
                    }

                    case Op.Look:
                        n = Next(need, i.X, 0, n);
                        break;
                    case Op.Split:
                        n = Next(need, i.X, 0, n);
                        n = Next(need, i.Y, 0, n);
                        break;
                    case Op.Loop:
                        n = Next(need, after, 0, n);
                        n = Next(need, i.X, 0, n);
                        break;
                    case Op.LoopInit:
                    {
                        Inst loop = insts[pc + 1];
                        n = Next(need, after, TimesNeed(loop.Min, loop.Iter), n);
                        break;
                    }

                    default:
                        n = Next(need, after, 0, n);
                        break;
                }

                int capped = (int)Math.Min(n, NoMatch);
                if (capped < need[pc])
                {
                    need[pc] = capped;
                    changed = true;
                }
            }
        }

        for (int pc = 0; pc < insts.Length; pc++)
        {
            if (insts[pc].Op == Op.Loop)
            {
                insts[pc].Need = need[pc];
            }
        }

        return need[0];
    }

    /// <summary>
    /// Works out the quick of every instruction. Every figure is a set that can only be too large: an assertion is
    /// taken to hold, a backreference and an instruction that matches right to left (the body of a lookbehind) to
    /// allow anything. A positive lookahead needs both its body and what follows it to go on from the same
    /// position, so it allows what both allow.
    /// </summary>
    /// <param name="insts">The instructions.</param>
    /// <returns>The quick of each instruction.</returns>
    public static Quick[] SetQuick(Inst[] insts) => Quicks(insts, out _);

    /// <summary><see cref="SetQuick"/>, and also the number of passes over the instructions it made, the last of which changed nothing.</summary>
    /// <param name="insts">The instructions.</param>
    /// <param name="passes">The passes.</param>
    /// <returns>The quick of each instruction.</returns>
    public static Quick[] Quicks(Inst[] insts, out int passes)
    {
        Quick[] q = new Quick[insts.Length];
        Quick all = Quick.All;
        passes = 0;

        // The sets only grow, and nearly every way goes forward, so going through the instructions from the last to
        // the first settles all but the loops in one pass.
        bool changed = true;
        while (changed)
        {
            changed = false;
            passes++;
            for (int pc = insts.Length - 1; pc >= 0; pc--)
            {
                Inst i = insts[pc];
                Quick n = default;
                switch (i.Op)
                {
                    case Op.Match:
                    case Op.LookOk:
                    case Op.LookFail:
                        n = all;
                        break;
                    case Op.Fail:
                        break;
                    case Op.Char:
                        n.AddRune(i.R);
                        break;
                    case Op.Set:
                        n.AddSet(i.Set!);
                        break;
                    case Op.Lit:
                        n.AddRune(i.R);
                        break;
                    case Op.Rep:
                    case Op.RepLazy:
                        if (i.Set is not null)
                        {
                            n.AddSet(i.Set);
                        }
                        else
                        {
                            n.AddRune(i.R);
                        }

                        if (i.Min == 0)
                        {
                            n.Union(q[pc + 1]);
                        }

                        break;
                    case Op.Jmp:
                    {
                        // The jump at the end of the body of a loop goes back to the head of the loop, which this
                        // pass has not reached. The head allows what follows the loop, which this pass has settled,
                        // and what the body starts with. So the jump takes what follows the loop too, and the first
                        // pass settles the head of every loop, the second every instruction, and the third finds
                        // nothing to change, however deep the loops nest.
                        n = q[i.X];
                        Inst head = insts[i.X];
                        if (i.X < pc && head.Op == Op.Loop)
                        {
                            n.Union(q[head.X]);
                        }
                        else if (i.X < pc && head.Op == Op.Split)
                        {
                            n.Union(q[head.X]);
                            n.Union(q[head.Y]);
                        }

                        break;
                    }

                    case Op.Split:
                        n = q[i.X];
                        n.Union(q[i.Y]);
                        break;
                    case Op.Loop:
                        n = q[pc + 1];
                        n.Union(q[i.X]);
                        break;
                    case Op.Eol:
                        n.End = q[pc + 1].End;
                        break;
                    case Op.Look:
                        n = q[i.X];
                        if (!i.Flag)
                        {
                            n.Intersect(q[pc + 1]);
                        }

                        break;
                    case Op.CharBack:
                    case Op.SetBack:
                    case Op.Backref:
                    case Op.BackrefBack:
                    case Op.BackrefFold:
                    case Op.BackrefFoldBack:
                    case Op.BackrefUnits:
                    case Op.BackrefUnitsBack:
                        n = all;
                        break;
                    default:
                        n = q[pc + 1];
                        break;
                }

                if (!n.Equals(q[pc]))
                {
                    q[pc] = n;
                    changed = true;
                }
            }
        }

        return q;
    }

    /// <summary>Whether a code point can be matched by comparing its encoded units. A surrogate has no encoding in UTF-8, and U+FFFD is also what a malformed byte of the text decodes to.</summary>
    /// <param name="r">The code point.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    public static bool IsLiteralRune(int r) => r >= 0 && r <= 0x10FFFF && !(r >= 0xD800 && r <= 0xDFFF) && r != Utf8Encoding.RuneError;

    private static long Next(int[] need, int to, long weight, long n)
    {
        // Both figures can be NoMatch, so the sum is made in 64 bits: in 32 it would wrap to a negative need.
        long v = need[to] + weight;
        return v < n ? v : n;
    }

    /// <summary><paramref name="count"/> times <paramref name="need"/>, capped at <see cref="NoMatch"/>.</summary>
    private static long TimesNeed(long count, long need)
    {
        if (count == 0 || need == 0)
        {
            return 0;
        }

        if (need >= NoMatch || count >= NoMatch / need)
        {
            return NoMatch;
        }

        return count * need;
    }

    /// <summary>Whether <paramref name="n"/> can match one character or more. It errs towards true.</summary>
    private static bool Consumes(EcmaRegexNode n)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Char:
            case EcmaRegexNodeKind.Set:
            case EcmaRegexNodeKind.Backref:
                return true;
            case EcmaRegexNodeKind.Cat:
            case EcmaRegexNodeKind.Alt:
            case EcmaRegexNodeKind.Group:
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    if (Consumes(sub))
                    {
                        return true;
                    }
                }

                return false;
            case EcmaRegexNodeKind.Repeat:
                return n.Max != 0 && Consumes(n.Subs[0]);
            default:
                return false;
        }
    }

    private static void CollectBackrefs(EcmaRegexNode n, List<int> output)
    {
        if (n.Kind == EcmaRegexNodeKind.Backref)
        {
            output.AddRange(n.Groups);
        }

        foreach (EcmaRegexNode sub in n.Subs)
        {
            CollectBackrefs(sub, output);
        }
    }

    /// <summary>The least number of characters a match of <paramref name="n"/> consumes, capped at <see cref="NoMatch"/>.</summary>
    private static long MinLength(EcmaRegexNode n)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Char:
                return 1;
            case EcmaRegexNodeKind.Set:
                return n.Set.Length == 0 ? NoMatch : 1;
            case EcmaRegexNodeKind.Cat:
            {
                long total = 0;
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    total += MinLength(sub);
                    if (total >= NoMatch)
                    {
                        return NoMatch;
                    }
                }

                return total;
            }

            case EcmaRegexNodeKind.Alt:
            {
                long least = NoMatch;
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    least = Math.Min(least, MinLength(sub));
                }

                return least;
            }

            case EcmaRegexNodeKind.Group:
                return MinLength(n.Subs[0]);
            case EcmaRegexNodeKind.Repeat:
                return TimesNeed(n.Min, MinLength(n.Subs[0]));
            default:
                return 0;
        }
    }

    /// <summary>Whether <paramref name="n"/> can match without consuming anything. It errs towards true.</summary>
    private static bool Nullable(EcmaRegexNode n)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Char:
            case EcmaRegexNodeKind.Set:
                return false;
            case EcmaRegexNodeKind.Cat:
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    if (!Nullable(sub))
                    {
                        return false;
                    }
                }

                return true;
            case EcmaRegexNodeKind.Alt:
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    if (Nullable(sub))
                    {
                        return true;
                    }
                }

                return false;
            case EcmaRegexNodeKind.Group:
                return Nullable(n.Subs[0]);
            case EcmaRegexNodeKind.Repeat:
                return n.Min == 0 || Nullable(n.Subs[0]);
            default:
                return true;
        }
    }

    /// <summary>Whether every way through <paramref name="n"/> begins with <c>^</c>.</summary>
    private static bool StartsAtBol(EcmaRegexNode n)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Bol:
                return true;
            case EcmaRegexNodeKind.Cat:
                return StartsAtBol(n.Subs[0]);
            case EcmaRegexNodeKind.Alt:
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    if (!StartsAtBol(sub))
                    {
                        return false;
                    }
                }

                return true;
            case EcmaRegexNodeKind.Group:
                return StartsAtBol(n.Subs[0]);
            case EcmaRegexNodeKind.Repeat:
                return n.Min >= 1 && StartsAtBol(n.Subs[0]);
            default:
                return false;
        }
    }

    /// <summary>Appends the literal text every match of <paramref name="n"/> starts with.</summary>
    private static void LiteralPrefix(EcmaRegexNode n, StringBuilder prefix)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Char:
                if (IsLiteralRune(n.CodePoint))
                {
                    AppendRune(prefix, n.CodePoint);
                }

                break;
            case EcmaRegexNodeKind.Cat:
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    if (sub.Kind != EcmaRegexNodeKind.Char || !IsLiteralRune(sub.CodePoint))
                    {
                        if (prefix.Length == 0 && sub.Kind == EcmaRegexNodeKind.Group)
                        {
                            LiteralPrefix(sub, prefix);
                        }

                        break;
                    }

                    AppendRune(prefix, sub.CodePoint);
                }

                break;
            case EcmaRegexNodeKind.Group:
                LiteralPrefix(n.Subs[0], prefix);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Appends, as inclusive pairs, the code points a match of <paramref name="n"/> can start with. It returns
    /// whether <paramref name="n"/> can match the empty string, and null when the set is not known. Assertions are
    /// treated as matching the empty string, which can only make the set larger than it need be.
    /// </summary>
    private static bool? FirstChars(Compiler c, EcmaRegexNode n, List<int> pairs)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Char:
                pairs.Add(n.CodePoint);
                pairs.Add(n.CodePoint);
                return false;
            case EcmaRegexNodeKind.Set:
                pairs.AddRange(n.Set);
                return false;
            case EcmaRegexNodeKind.Cat:
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    bool? empty = FirstChars(c, sub, pairs);
                    if (empty is null)
                    {
                        return null;
                    }

                    if (!empty.Value)
                    {
                        return false;
                    }
                }

                return true;
            case EcmaRegexNodeKind.Alt:
            {
                bool any = false;
                foreach (EcmaRegexNode sub in n.Subs)
                {
                    bool? empty = FirstChars(c, sub, pairs);
                    if (empty is null)
                    {
                        return null;
                    }

                    any |= empty.Value;
                }

                return any;
            }

            case EcmaRegexNodeKind.Group:
                return FirstChars(c, n.Subs[0], pairs);
            case EcmaRegexNodeKind.Repeat:
            {
                if (n.Max == 0)
                {
                    return true;
                }

                bool? empty = FirstChars(c, n.Subs[0], pairs);
                if (empty is null)
                {
                    return null;
                }

                return empty.Value || n.Min == 0;
            }

            case EcmaRegexNodeKind.Backref:
                return null;
            default:
                return true;
        }
    }

    private static void AppendRune(StringBuilder sb, int r)
    {
        if (r > 0xFFFF)
        {
            sb.Append(char.ConvertFromUtf32(r));
        }
        else
        {
            sb.Append((char)r);
        }
    }

    private CharSet Class(EcmaRegexNode n)
    {
        if (!this.sets.TryGetValue(n.Set, out CharSet? set))
        {
            set = new CharSet(n.Set);
            this.sets[n.Set] = set;
        }

        return set;
    }

    private int Add(Inst i)
    {
        this.insts.Add(i);
        return this.insts.Count - 1;
    }

    private int Pc() => this.insts.Count;

    /// <summary>The number of a new memo point for an instruction written here, or 0 when a choice here cannot be remembered.</summary>
    private int Point()
    {
        if (!this.memo || this.open.Count > MaxMemoLoops)
        {
            return 0;
        }

        MemoPoint p = new() { Loop0 = -1, Loop1 = -1 };
        for (int k = 0; k < this.open.Count; k++)
        {
            LoopContext l = this.open[k];
            if (!l.Simple)
            {
                return 0;
            }

            if (k == 0)
            {
                p.Loop0 = l.Loop;
                p.Min0 = l.Min;
            }
            else
            {
                p.Loop1 = l.Loop;
                p.Min1 = l.Min;
            }
        }

        this.points.Add(p);
        return this.points.Count;
    }

    /// <summary>The first and last slot of the tracked groups inside <paramref name="n"/>, or false when it has none.</summary>
    private bool GroupRange(EcmaRegexNode n, out int lo, out int hi)
    {
        lo = int.MaxValue;
        hi = int.MinValue;
        this.WalkGroups(n, ref lo, ref hi);
        return lo <= hi;
    }

    private void WalkGroups(EcmaRegexNode n, ref int lo, ref int hi)
    {
        if (n.Kind == EcmaRegexNodeKind.Group && n.Group > 0 && this.tracked.TryGetValue(n.Group, out int t))
        {
            lo = Math.Min(lo, t);
            hi = Math.Max(hi, t);
        }

        foreach (EcmaRegexNode sub in n.Subs)
        {
            this.WalkGroups(sub, ref lo, ref hi);
        }
    }

    private int Literal(List<int> runes)
    {
        StringBuilder sb = new();
        foreach (int r in runes)
        {
            AppendRune(sb, r);
        }

        string s = sb.ToString();
        this.utf16Literals.Add(s);
        this.utf8Literals.Add(Encoding.UTF8.GetBytes(s));
        return this.utf16Literals.Count - 1;
    }

    /// <summary>Writes the instructions for <paramref name="n"/>. With <paramref name="back"/> set the instructions match right to left, as the body of a lookbehind must.</summary>
    private void Emit(EcmaRegexNode n, bool back)
    {
        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Empty:
                break;
            case EcmaRegexNodeKind.Char:
                this.Add(new Inst(back ? Op.CharBack : Op.Char) { R = n.CodePoint });
                break;
            case EcmaRegexNodeKind.Set:
            {
                CharSet set = this.Class(n);
                if (set.IsEmpty)
                {
                    this.Add(new Inst(Op.Fail));
                }
                else
                {
                    this.Add(new Inst(back ? Op.SetBack : Op.Set) { Set = set });
                }

                break;
            }

            case EcmaRegexNodeKind.Cat:
            {
                if (back)
                {
                    for (int k = n.Subs.Length - 1; k >= 0; k--)
                    {
                        this.Emit(n.Subs[k], true);
                    }

                    return;
                }

                int i = 0;
                List<int> lit = [];
                while (i < n.Subs.Length)
                {
                    // A run of literal characters becomes one comparison of text.
                    int j = i;
                    lit.Clear();
                    while (j < n.Subs.Length && n.Subs[j].Kind == EcmaRegexNodeKind.Char && IsLiteralRune(n.Subs[j].CodePoint))
                    {
                        lit.Add(n.Subs[j].CodePoint);
                        j++;
                    }

                    if (j - i >= 2)
                    {
                        this.Add(new Inst(Op.Lit) { Lit = this.Literal(lit), Len = lit.Count, R = lit[0] });
                        i = j;
                        continue;
                    }

                    this.Emit(n.Subs[i], false);
                    i++;
                }

                break;
            }

            case EcmaRegexNodeKind.Alt:
            {
                List<int> jumps = [];
                for (int k = 0; k < n.Subs.Length; k++)
                {
                    if (k == n.Subs.Length - 1)
                    {
                        this.Emit(n.Subs[k], back);
                        break;
                    }

                    int m = this.Point();
                    int split = this.Add(new Inst(Op.Split) { M = m });
                    this.SetX(split, this.Pc());
                    this.Emit(n.Subs[k], back);
                    jumps.Add(this.Add(new Inst(Op.Jmp)));
                    this.SetY(split, this.Pc());
                }

                foreach (int j in jumps)
                {
                    this.SetX(j, this.Pc());
                }

                break;
            }

            case EcmaRegexNodeKind.Group:
            {
                if (n.Group == 0 || !this.tracked.TryGetValue(n.Group, out int t))
                {
                    this.Emit(n.Subs[0], back);
                    return;
                }

                this.Add(new Inst(Op.SaveTmp) { A = t });
                this.Emit(n.Subs[0], back);
                this.Add(new Inst(Op.Capture) { A = t, Flag = back });
                break;
            }

            case EcmaRegexNodeKind.Bol:
                this.Add(new Inst(Op.Bol));
                break;
            case EcmaRegexNodeKind.Eol:
                this.Add(new Inst(Op.Eol));
                break;
            case EcmaRegexNodeKind.LineStart:
                this.Add(new Inst(Op.LineStart));
                break;
            case EcmaRegexNodeKind.LineEnd:
                this.Add(new Inst(Op.LineEnd));
                break;
            case EcmaRegexNodeKind.WordBoundary:
                this.Add(new Inst(n.Fold ? Op.WordBoundaryFold : Op.WordBoundary));
                break;
            case EcmaRegexNodeKind.NotWordBoundary:
                this.Add(new Inst(n.Fold ? Op.NotWordBoundaryFold : Op.NotWordBoundary));
                break;
            case EcmaRegexNodeKind.Backref:
            {
                Op op = n.Fold
                    ? (back ? Op.BackrefFoldBack : Op.BackrefFold)
                    : this.unicode
                        ? (back ? Op.BackrefBack : Op.Backref)
                        : (back ? Op.BackrefUnitsBack : Op.BackrefUnits);

                // When several groups share the name, at most one has taken part, and the others match the empty
                // string. So matching each in turn matches the one that took part.
                foreach (int g in n.Groups)
                {
                    this.Add(new Inst(op) { A = this.tracked[g], Flag = this.unicode });
                }

                break;
            }

            case EcmaRegexNodeKind.Look:
            {
                int look = this.Add(new Inst(Op.Look) { Flag = n.Negative });

                // Whether the body matches depends on nothing outside it.
                LoopContext[] outer = [.. this.open];
                this.open.Clear();
                this.Emit(n.Subs[0], n.Behind);
                this.open.Clear();
                this.open.AddRange(outer);
                this.Add(new Inst(n.Negative ? Op.LookFail : Op.LookOk));
                this.SetX(look, this.Pc());
                break;
            }

            case EcmaRegexNodeKind.Repeat:
                this.EmitRepeat(n, back);
                break;
            default:
                throw new InvalidOperationException("Unknown node kind " + n.Kind);
        }
    }

    private void SetX(int at, int x)
    {
        Inst i = this.insts[at];
        i.X = x;
        this.insts[at] = i;
    }

    private void SetY(int at, int y)
    {
        Inst i = this.insts[at];
        i.Y = y;
        this.insts[at] = i;
    }

    private void EmitBody(EcmaRegexNode body, bool back, bool clear, int lo, int hi)
    {
        if (clear)
        {
            this.Add(new Inst(Op.Clear) { A = lo, B = hi });
        }

        this.Emit(body, back);
    }

    /// <summary>Writes a choice between entering the body and leaving the loop, to be completed by <see cref="SetSplit"/>.</summary>
    private int Split()
    {
        int m = this.Point();
        return this.Add(new Inst(Op.Split) { M = m });
    }

    /// <summary>Completes a split in the order the quantifier prefers.</summary>
    private void SetSplit(bool lazy, int at, int enter, int exit)
    {
        Inst i = this.insts[at];
        if (lazy)
        {
            i.X = exit;
            i.Y = enter;
        }
        else
        {
            i.X = enter;
            i.Y = exit;
        }

        this.insts[at] = i;
    }

    private void EmitRepeat(EcmaRegexNode n, bool back)
    {
        EcmaRegexNode body = n.Subs[0];
        if (n.Max == 0)
        {
            return;
        }

        int min = n.Min;
        int max = n.Max;
        if (!back && (body.Kind == EcmaRegexNodeKind.Char || (body.Kind == EcmaRegexNodeKind.Set && !this.Class(body).IsEmpty)))
        {
            int m = this.Point();
            this.Add(new Inst(n.Lazy ? Op.RepLazy : Op.Rep)
            {
                R = body.CodePoint,
                Set = body.Kind == EcmaRegexNodeKind.Set ? this.Class(body) : null,
                Min = min,
                Max = max,
                M = m,
            });
            return;
        }

        bool clear = this.GroupRange(body, out int lo, out int hi);

        // A body that always consumes something needs no check for empty iterations, so the unbounded forms and the
        // optional form are plain choices.
        if (!Nullable(body))
        {
            if (min == 0 && max == EcmaRegexNode.Unbounded)
            {
                int at = this.Split();
                int enter = this.Pc();
                this.EmitBody(body, back, clear, lo, hi);
                this.Add(new Inst(Op.Jmp) { X = at });
                int exit = this.Pc();
                this.SetSplit(n.Lazy, at, enter, exit);
                return;
            }

            if (min == 1 && max == EcmaRegexNode.Unbounded)
            {
                int enter = this.Pc();
                this.EmitBody(body, back, clear, lo, hi);
                int at = this.Split();
                int exit = this.Pc();
                this.SetSplit(n.Lazy, at, enter, exit);
                return;
            }

            if (min == 0 && max == 1)
            {
                int at = this.Split();
                int enter = this.Pc();
                this.EmitBody(body, back, clear, lo, hi);
                int exit = this.Pc();
                this.SetSplit(n.Lazy, at, enter, exit);
                return;
            }
        }

        int k = this.loops++;
        this.Add(new Inst(Op.LoopInit) { A = k });
        long iter = back ? 0 : MinLength(body);
        bool simple = max == EcmaRegexNode.Unbounded && min <= 1;

        // The loop is left or entered from here, and what follows either depends on the loops around this one.
        int point = simple ? this.Point() : 0;
        int loop = this.Add(new Inst(Op.Loop) { A = k, Min = min, Max = max, Flag = n.Lazy, Iter = (int)Math.Min(iter, int.MaxValue), M = point });
        this.Add(new Inst(Op.LoopIter) { A = k });
        this.open.Add(new LoopContext(k, min, simple));
        this.EmitBody(body, back, clear, lo, hi);
        this.open.RemoveAt(this.open.Count - 1);
        this.Add(new Inst(Op.Jmp) { X = loop });
        this.SetX(loop, this.Pc());
    }

    /// <summary>One counted loop that the instruction being written is inside of: its number, its least count, and whether its state is one bit to a memo point (no upper bound, and a least count of zero or one).</summary>
    private readonly struct LoopContext(int loop, int min, bool simple)
    {
        public int Loop { get; } = loop;

        public int Min { get; } = min;

        public bool Simple { get; } = simple;
    }

    private sealed class ReferenceComparer : IEqualityComparer<int[]>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(int[]? x, int[]? y) => ReferenceEquals(x, y);

        public int GetHashCode(int[] obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
