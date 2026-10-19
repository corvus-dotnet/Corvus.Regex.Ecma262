// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of compile.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of compile.go).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The instructions of the backtracking matcher.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public enum Op : byte
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
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct Inst
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

    /// <summary>
    /// Gets or sets a value indicating whether a <see cref="Op.Rep"/> keeps no choice to give characters back: what
    /// follows it cannot consume any character of its set, so a way that gives one back fails at once (see
    /// <see cref="Compiler.SetAtomic"/>).
    /// </summary>
    public bool Atomic { get; set; }
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
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct MemoPoint
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
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct Quick : IEquatable<Quick>
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

#if NET
    private System.Buffers.SearchValues<byte>? allowedBytes;
    private System.Buffers.SearchValues<char>? allowedChars;

    /// <summary>Gets a value indicating whether the way can only go on from a character below 128 (and so the allowed characters can be searched for).</summary>
    public bool AsciiOnly => !this.Other && !this.Mid;

    /// <summary>Gets the characters below 128 the way can go on from, for a vectorised search over UTF-8. Only when <see cref="AsciiOnly"/>.</summary>
    public System.Buffers.SearchValues<byte> AllowedBytes => this.allowedBytes ??= System.Buffers.SearchValues.Create(this.Allowed<byte>());

    /// <summary>Gets the characters below 128 the way can go on from, for a vectorised search over UTF-16. Only when <see cref="AsciiOnly"/>.</summary>
    public System.Buffers.SearchValues<char> AllowedChars => this.allowedChars ??= System.Buffers.SearchValues.Create(this.Allowed<char>());

    private TUnit[] Allowed<TUnit>()
        where TUnit : unmanaged
    {
        List<TUnit> units = [];
        for (int c = 0; c < 128; c++)
        {
            bool on = c < 64 ? ((this.Ascii0 >> c) & 1) != 0 : ((this.Ascii1 >> (c - 64)) & 1) != 0;
            if (on)
            {
                units.Add(typeof(TUnit) == typeof(byte) ? (TUnit)(object)(byte)c : (TUnit)(object)(char)c);
            }
        }

        return [.. units];
    }
#endif

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

/// <summary>The shapes of program the search answers without running the matcher.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public enum SimpleShape : byte
{
    /// <summary>None of the below.</summary>
    None,

    /// <summary>A literal and nothing else: every occurrence of the prefix is a match.</summary>
    Literal,

    /// <summary>One class and nothing else: every member is a match.</summary>
    Set,

    /// <summary>An ASCII literal matched ignoring case and nothing else.</summary>
    CaseLiteral,

    /// <summary>A sequence of characters and classes of one character each and nothing else: a match is a run of members, one from each, at a start the first set allows.</summary>
    Sequence,

    /// <summary>One greedy single-character loop with no upper bound and nothing else: a match is the run of members from the start.</summary>
    Loop,

    /// <summary>An alternation of literals and nothing else: a match is the first alternative that is at a start its first character allows.</summary>
    LiteralAlternation,
}

/// <summary>A pattern compiled for the backtracking matcher.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class Program
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
    /// <param name="required">A literal every match holds at a fixed distance from its start, or empty.</param>
    /// <param name="requiredOffset">That distance, or -1.</param>
    /// <param name="caseForms">For each character of a literal every match starts with, its forms (a letter's cases under the i flag), or empty (see <see cref="Compiler.CaseForms"/>).</param>
    /// <param name="requiredOffsetUtf8">The distance to the required literal over UTF-8, or -1 when the terms before it are not all ASCII.</param>
    /// <param name="loopSet">The set of a loop every match starts with, when a literal follows it or a failed start may skip its run, or null.</param>
    /// <param name="loopMin">The least count of that loop.</param>
    /// <param name="afterLoop">The literal after that loop, or empty.</param>
    /// <param name="skipRun">Whether a failed start may be moved to the end of the run of the loop's set.</param>
    /// <param name="requiredCount">The number of characters before the required literal, or -1.</param>
    /// <param name="afterLoopMax">The greatest count of the leading loop, when bounded, or -1.</param>
    /// <param name="afterLoopAlternation">The alternation of literals after the leading loop, or null.</param>
    /// <param name="requiredCountMax">The greatest number of characters before the required literal when it varies, or -1.</param>
    public Program(Inst[] insts, int groups, int loops, bool units, bool anchored, string prefix, int need, MemoPoint[] points, Quick[] quick, CharSet? first, byte[][] utf8Literals, string[] utf16Literals, string required, int requiredOffset, int[][] caseForms, int requiredOffsetUtf8, CharSet? loopSet, int loopMin, string afterLoop, bool skipRun, int requiredCount, int afterLoopMax = -1, AlternationSearch? afterLoopAlternation = null, int requiredCountMax = -1)
    {
        this.RequiredCountMax = requiredCountMax;
        this.AfterLoopMax = afterLoopMax;
        this.AfterLoopAlternation = afterLoopAlternation;
        this.RequiredOffsetUtf8 = requiredOffsetUtf8;
        this.RequiredCount = requiredCount;
        this.CaseCharacters = caseForms.Length;
        if (caseForms.Length != 0)
        {
            Needle<ushort> needle16 = Needles.Forms<ushort>(caseForms, false, out int[] extras16, out int window16);
            Needle<byte> needle8 = Needles.Forms<byte>(caseForms, true, out int[] extras8, out int window8);
            this.CaseNeedleUtf16 = needle16.CanSearch ? needle16 : null;
            this.CaseNeedleUtf8 = needle8.CanSearch ? needle8 : null;
            this.CaseExtrasUtf16 = extras16.Length == 0 ? null : Needles.Multi<ushort>(Array.ConvertAll(extras16, Rune), false);
            this.CaseExtrasUtf8 = extras8.Length == 0 ? null : Needles.Multi<byte>(Array.ConvertAll(extras8, Rune), true, leadOnly: true);
            this.CaseWindowUtf16 = window16;
            this.CaseWindowUtf8 = window8;
        }

        this.LoopSet = loopSet;
        this.LoopMin = loopMin;
        this.AfterLoopUtf16 = afterLoop;
        this.AfterLoopUtf8 = Encoding.UTF8.GetBytes(afterLoop);
        this.SkipRun = skipRun;
        this.FirstIsSmall = first is not null && first.IsSmall;
        this.Utf8PrefixNeedle = prefix.Length == 0 ? null : Needles.Utf8(prefix);
        this.RequiredUtf8Needle = required.Length == 0 ? null : Needles.Utf8(required);
        this.AfterLoopUtf8Needle = afterLoop.Length == 0 ? null : Needles.Utf8(afterLoop);
        this.Utf16PrefixNeedle = prefix.Length == 0 ? null : Needles.Utf16(prefix);
        this.RequiredUtf16Needle = required.Length == 0 ? null : Needles.Utf16(required);
        this.AfterLoopUtf16Needle = afterLoop.Length == 0 ? null : Needles.Utf16(afterLoop);
        this.RequiredUtf16 = required;
        this.RequiredUtf8 = Encoding.UTF8.GetBytes(required);
        this.RequiredOffset = requiredOffset;
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

    /// <summary>Gets or sets the generated code that runs this program in place of the interpreter, or null (see <see cref="GeneratedRunner"/>).</summary>
    public GeneratedRunner? Runner { get; set; }

    /// <summary>Gets the prefix as a UTF-8 search, or null.</summary>
    public Needle<byte>? Utf8PrefixNeedle { get; }

    /// <summary>Gets the prefix as a UTF-16 search, or null.</summary>
    public Needle<ushort>? Utf16PrefixNeedle { get; }

    /// <summary>Gets the required literal as a UTF-16 search, or null.</summary>
    public Needle<ushort>? RequiredUtf16Needle { get; }

    /// <summary>Gets the literal after the leading loop as a UTF-16 search, or null.</summary>
    public Needle<ushort>? AfterLoopUtf16Needle { get; }

    /// <summary>Gets the required literal as a UTF-8 search by its rarest byte, or null.</summary>
    public Needle<byte>? RequiredUtf8Needle { get; }

    /// <summary>Gets the literal after the leading loop as a UTF-8 search by its rarest byte, or null.</summary>
    public Needle<byte>? AfterLoopUtf8Needle { get; }

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

    /// <summary>Gets a literal every match holds at a fixed distance from its start, or empty (see <see cref="Compiler.RequiredLiteral"/>).</summary>
    public string RequiredUtf16 { get; }

    /// <summary>Gets the required literal as UTF-8.</summary>
    public byte[] RequiredUtf8 { get; }

    /// <summary>Gets the distance from the start of a match to its required literal, in units of either encoding, or -1 when there is none.</summary>
    public int RequiredOffset { get; }

    /// <summary>
    /// Gets the number of characters of the literal every match starts with whose characters each have one or
    /// more forms (a letter's cases under the i flag, or the members of a small class of single characters), or 0
    /// (see <see cref="Compiler.CaseForms"/>). The search for a start is then an anchored search for the literal
    /// in any of its forms, vectorised, instead of a match of the classes.
    /// </summary>
    public int CaseCharacters { get; }

    /// <summary>Gets the search for that literal over UTF-16, or null when there is none or it cannot be searched for by anchors.</summary>
    public Needle<ushort>? CaseNeedleUtf16 { get; }

    /// <summary>Gets the search for that literal over UTF-8, or null when there is none or it cannot be searched for by anchors.</summary>
    public Needle<byte>? CaseNeedleUtf8 { get; }

    /// <summary>Gets a search for the forms of the literal's characters that the UTF-16 search leaves out (none in practice: a character and its cases are each one code unit, or each two), or null.</summary>
    public MultiNeedle<ushort>? CaseExtrasUtf16 { get; }

    /// <summary>Gets a search for the forms of the literal's characters that the UTF-8 search leaves out (the long s and the Kelvin sign, whose UTF-8 is longer than s and k), which are looked for apart, or null.</summary>
    public MultiNeedle<byte>? CaseExtrasUtf8 { get; }

    /// <summary>Gets the most UTF-16 units the literal can take in any of its forms.</summary>
    public int CaseWindowUtf16 { get; }

    /// <summary>Gets the most UTF-8 units the literal can take in any of its forms.</summary>
    public int CaseWindowUtf8 { get; }

    /// <summary>Gets a value indicating whether <see cref="First"/> is small enough to be searched for member by member (see <see cref="CharSet.IsSmall"/>).</summary>
    public bool FirstIsSmall { get; }

    /// <summary>
    /// Gets the shape of a program the search answers without running the matcher: a literal alone, one class
    /// alone, or a case-insensitive ASCII literal alone (see <see cref="SimpleShape"/>). Only for a program that
    /// tracks no group.
    /// </summary>
    public SimpleShape Shape { get; private set; }

    /// <summary>Gets the class of a <see cref="SimpleShape.Set"/> or <see cref="SimpleShape.Loop"/> program.</summary>
    public CharSet? ShapeSet { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the program starts with <c>\b</c> and every character a match can start with
    /// is an ASCII word character: a candidate inside a word then fails, and the search passes over the rest of
    /// the word.
    /// </summary>
    public bool LeadingWordBoundary { get; internal set; }

    /// <summary>Decides <see cref="Shape"/> from the instructions.</summary>
    /// <param name="alternatives">The literals of an alternation that is the whole pattern, or empty.</param>
    /// <param name="caseAlternatives">The alternatives of an alternation that is the whole pattern as the forms of their characters (see <see cref="Compiler.CaseAlternatives"/>), or empty.</param>
    internal void SetShape(string[] alternatives, int[][][] caseAlternatives)
    {
        Inst[] insts = this.Insts;
        if (this.Groups != 0 || this.Anchored || insts.Length < 2 || insts[insts.Length - 1].Op != Op.Match)
        {
            return;
        }

        if (insts.Length == 2 && (insts[0].Op == Op.Lit || insts[0].Op == Op.Char) && this.Utf16Prefix.Length != 0)
        {
            this.Shape = SimpleShape.Literal;
            return;
        }

        if (insts.Length == 2 && insts[0].Op == Op.Set)
        {
            this.Shape = SimpleShape.Set;
            this.ShapeSet = insts[0].Set;
            return;
        }

        if (insts.Length == 2 && insts[0].Op == Op.Rep && insts[0].Max < 0 && insts[0].Min <= 1 && insts[0].Set is not null)
        {
            this.Shape = SimpleShape.Loop;
            this.ShapeSet = insts[0].Set;
            return;
        }

        if (alternatives.Length >= 2 && this.First is not null)
        {
            this.Shape = SimpleShape.LiteralAlternation;
            this.Alternation = AlternationSearch.Create(alternatives);
            return;
        }

        if (caseAlternatives.Length >= 2 && this.First is not null)
        {
            // An alternation of literals in their forms (under the i flag).
            AlternationSearch? search = AlternationSearch.CreateForms(caseAlternatives);
            if (search is not null)
            {
                this.Shape = SimpleShape.LiteralAlternation;
                this.Alternation = search;
            }

            return;
        }

        for (int k = 0; k < insts.Length - 1; k++)
        {
            if (insts[k].Op != Op.Set && insts[k].Op != Op.Char)
            {
                return;
            }
        }

#if NET
        if (this.CaseCharacters != 0 && this.CaseCharacters == insts.Length - 1 && this.CaseNeedleUtf16 is not null && this.CaseNeedleUtf8 is not null)
        {
            this.Shape = SimpleShape.CaseLiteral;
            return;
        }
#endif

        if (this.First is not null && this.RequiredUtf16.Length == 0)
        {
            // A required literal is the better guide to a start than the first class (x after [a-q][^u-z]{13}),
            // and the general search uses it.
            this.Shape = SimpleShape.Sequence;
            this.PairNeedles = MakePairNeedles(insts);
            this.PairNeedlesUtf8 = new Needle<byte>[this.PairNeedles.Length];
            this.PairNeedlesUtf16 = new Needle<ushort>[this.PairNeedles.Length];
            for (int k = 0; k < this.PairNeedles.Length; k++)
            {
                this.PairNeedlesUtf8[k] = Needles.Utf8(this.PairNeedles[k]);
                this.PairNeedlesUtf16[k] = Needles.Utf16(this.PairNeedles[k]);
            }

            if (this.PairNeedles.Length != 0)
            {
                this.PairMultiUtf8 = new MultiNeedle<byte>(this.PairNeedlesUtf8);
                this.PairMultiUtf16 = new MultiNeedle<ushort>(this.PairNeedlesUtf16);
            }
        }
    }

    /// <summary>
    /// Gets, for a <see cref="SimpleShape.Sequence"/> program whose first two classes are small, every pair of a
    /// member of the first and a member of the second as a UTF-16 needle, so that the search for a start is a
    /// vectorised search for two characters rather than one. Empty otherwise.
    /// </summary>
    public string[] PairNeedles { get; private set; } = [];

    /// <summary>Gets <see cref="PairNeedles"/> as UTF-8 searches.</summary>
    public Needle<byte>[] PairNeedlesUtf8 { get; private set; } = [];

    /// <summary>Gets <see cref="PairNeedles"/> as UTF-16 searches.</summary>
    public Needle<ushort>[] PairNeedlesUtf16 { get; private set; } = [];

    /// <summary>Gets the search for the alternatives of a <see cref="SimpleShape.LiteralAlternation"/> program, or null.</summary>
    public AlternationSearch? Alternation { get; private set; }

    /// <summary>Gets the search for the alternation of literals that follows the leading loop (see <see cref="Compiler.LiteralAfterLoop"/>), or null.</summary>
    public AlternationSearch? AfterLoopAlternation { get; }

    /// <summary>Gets the greatest count of the leading loop before the literal or alternation after it, or -1 when it has none.</summary>
    public int AfterLoopMax { get; }

    /// <summary>Gets the search for any of the pair needles at once over UTF-8, or null.</summary>
    public MultiNeedle<byte>? PairMultiUtf8 { get; private set; }

    /// <summary>Gets the search for any of the pair needles at once over UTF-16, or null.</summary>
    public MultiNeedle<ushort>? PairMultiUtf16 { get; private set; }

    private static string[] MakePairNeedles(Inst[] insts)
    {
        if (insts.Length < 3)
        {
            return [];
        }

        List<int> first = Members(insts[0], 4);
        List<int> second = Members(insts[1], 4);
        if (first.Count == 0 || second.Count == 0)
        {
            return [];
        }

        List<string> needles = [];
        foreach (int a in first)
        {
            foreach (int b in second)
            {
                StringBuilder sb = new();
                Compiler.AppendRune(sb, a);
                Compiler.AppendRune(sb, b);
                needles.Add(sb.ToString());
            }
        }

        return [.. needles];
    }

    /// <summary>The members of a character or class instruction, when there are at most <paramref name="most"/> and none is a surrogate; empty otherwise.</summary>
    private static List<int> Members(Inst i, int most)
    {
        List<int> members = [];
        if (i.Set is null)
        {
            if (Compiler.IsLiteralRune(i.R))
            {
                members.Add(i.R);
            }

            return members;
        }

        int[] ranges = i.Set.Ranges;
        for (int k = 0; k < ranges.Length; k += 2)
        {
            for (int c = ranges[k]; c <= ranges[k + 1]; c++)
            {
                if (!Compiler.IsLiteralRune(c) || members.Count >= most)
                {
                    return [];
                }

                members.Add(c);
            }
        }

        return members;
    }

    /// <summary>Gets the distance from the start of a match to its required literal over UTF-8, or -1 when the terms before it are not all ASCII (over UTF-16 <see cref="RequiredOffset"/> holds for any term of one code unit).</summary>
    public int RequiredOffsetUtf8 { get; }

    /// <summary>Gets the number of characters (code points, or code units without the u flag) before the required literal, or -1 when there is none. Where the encoding has no fixed offset, the start is that many characters before the literal.</summary>
    public int RequiredCount { get; }

    /// <summary>Gets the greatest number of characters before the required literal when it varies (<see cref="RequiredCount"/> is then the least), or -1 when it is fixed.</summary>
    public int RequiredCountMax { get; }

    /// <summary>Gets the set of the single-character loop every match starts with (see <see cref="Compiler.LiteralAfterLoop"/> and <see cref="SkipRun"/>), or null.</summary>
    public CharSet? LoopSet { get; }

    /// <summary>Gets the least count of that loop, 0 or 1.</summary>
    public int LoopMin { get; }

    /// <summary>Gets the literal after that loop, or empty.</summary>
    public string AfterLoopUtf16 { get; }

    /// <summary>Gets the literal after that loop as UTF-8.</summary>
    public byte[] AfterLoopUtf8 { get; }

    /// <summary>
    /// Gets a value indicating whether, when a match from a start fails, every start in the rest of the run of
    /// <see cref="LoopSet"/> from there fails too, so the search may go on from the end of the run. It holds when
    /// the pattern starts with an atomic single-character loop with no upper bound: the loop from any later start in
    /// the run ends at the same place, and what follows runs from there with the same result.
    /// </summary>
    public bool SkipRun { get; }

    private static string Rune(int codePoint)
    {
        StringBuilder sb = new();
        Compiler.AppendRune(sb, codePoint);
        return sb.ToString();
    }

    private static int[] Pairs(int[] points)
    {
        int[] pairs = new int[points.Length * 2];
        for (int k = 0; k < points.Length; k++)
        {
            pairs[2 * k] = points[k];
            pairs[(2 * k) + 1] = points[k];
        }

        return pairs;
    }

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
        SetAtomic(insts, quick);
        bool anchored = StartsAtBol(root);
        string prefix = string.Empty;
        CharSet? first = null;
        string required = string.Empty;
        int requiredOffset = -1;
        int requiredOffsetUtf8 = -1;
        int requiredCount = -1;
        int requiredCountMax = -1;
        int[][] caseForms = [];
        CharSet? loopSet = null;
        int loopMin = 0;
        string afterLoop = string.Empty;
        int afterLoopMax = -1;
        AlternationSearch? afterLoopAlternation = null;
        bool skipRun = false;
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

                required = RequiredLiteral(root, unicode, out requiredOffset, out requiredOffsetUtf8, out requiredCount, out requiredCountMax);
                if (required.Length == 1 && first is not null && first.Count <= 8)
                {
                    // One common character is a poorer guide than a small first set.
                    required = string.Empty;
                    requiredOffset = -1;
                    requiredOffsetUtf8 = -1;
                    requiredCount = -1;
                    requiredCountMax = -1;
                }

                if (required.Length == 0)
                {
                    caseForms = CaseForms(root);
                }

                if (required.Length == 0 && caseForms.Length == 0 && referenced.Count == 0)
                {
                    afterLoop = LiteralAfterLoop(root, c, out loopSet, out loopMin, out afterLoopMax, out afterLoopAlternation);
                }
            }
        }

        if (referenced.Count == 0 && afterLoop.Length == 0 && afterLoopAlternation is null)
        {
            // A leading single-character loop (after the entry of a group): a failed start may skip its run.
            // Whether what follows the loop succeeds depends only on the position (no backreference reads a group),
            // and every position a later start of the run could leave the loop at was tried from the first. An
            // assertion before the loop would make success depend on the start (\B holds inside a word and not at
            // its start), so none may come before it.
            int at = 0;
            while (at < insts.Length && insts[at].Op == Op.SaveTmp)
            {
                at++;
            }

            if (at < insts.Length && insts[at].Op == Op.Rep && insts[at].Max < 0 && insts[at].Set is not null)
            {
                // Any least count: a later start of the run can leave the loop only where the first could.
                skipRun = true;
                loopSet = insts[at].Set;
                loopMin = insts[at].Min;
            }
        }

        Program program = new(insts, c.tracked.Count, c.loops, !unicode, anchored, prefix, need, [.. c.points], quick, first, [.. c.utf8Literals], [.. c.utf16Literals], required, requiredOffset, caseForms, requiredOffsetUtf8, loopSet, loopMin, afterLoop, skipRun, requiredCount, afterLoopMax, afterLoopAlternation, requiredCountMax);
        program.SetShape(LiteralAlternatives(root), CaseAlternatives(root));
        program.LeadingWordBoundary = insts.Length > 1 && insts[0].Op == Op.WordBoundary && first is not null && !first.HasNonAscii && IsWordSubset(first);
        return program;
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

        if (n.Kind == EcmaRegexNodeKind.Cat && Array.Exists(n.Subs, sub => sub.Kind == EcmaRegexNodeKind.Cat))
        {
            // A sequence inside a sequence (a written-out repetition) is the same sequence: flat, so that what
            // reads the terms (the required literal, the first characters) sees them all.
            List<EcmaRegexNode> flat = [];
            foreach (EcmaRegexNode sub in n.Subs)
            {
                if (sub.Kind == EcmaRegexNodeKind.Cat)
                {
                    flat.AddRange(sub.Subs);
                }
                else
                {
                    flat.Add(sub);
                }
            }

            n.Subs = [.. flat];
        }

        if (n.Kind == EcmaRegexNodeKind.Repeat && n.Max != 0 && !Consumes(n.Subs[0]))
        {
            return n.Min == 0 ? new EcmaRegexNode(EcmaRegexNodeKind.Empty) : n.Subs[0];
        }

        if (n.Kind == EcmaRegexNodeKind.Repeat && n.Min == n.Max && n.Max >= 2 && n.Max <= 32 && n.Subs[0].Kind is EcmaRegexNodeKind.Set or EcmaRegexNodeKind.Char)
        {
            // An exact count of one character or class is that many of it in a row: a straight run of tests
            // without a loop's counting, which pays where a search tries the run at every occurrence of a literal
            // after it ([a-q][^u-z]{13}x).
            return Unroll(n);
        }

        if (n.Kind == EcmaRegexNodeKind.Repeat && n.Max > 0 && n.Max <= 4 && n.Min >= 1 && !Nullable(n.Subs[0]) && !HasCapture(n.Subs[0]) && Size(n.Subs[0]) <= 32
            && (n.Subs[0].Kind is EcmaRegexNodeKind.Group or EcmaRegexNodeKind.Alt or EcmaRegexNodeKind.Cat))
        {
            // A small counted repetition of a body that holds no group and cannot be empty is the body written out,
            // with the copies beyond the least count optional one inside the other: the same matches in the same
            // order, without a loop's counting. A body of one character stays a counted loop, which the matcher
            // runs without choices.
            return Unroll(n);
        }

        return n;
    }

    /// <summary>Writes out a counted repetition (see <see cref="Simplify"/>).</summary>
    private static EcmaRegexNode Unroll(EcmaRegexNode n)
    {
        EcmaRegexNode body = n.Subs[0];
        List<EcmaRegexNode> terms = [];
        for (int k = 0; k < n.Min; k++)
        {
            terms.Add(Clone(body));
        }

        if (n.Max > n.Min)
        {
            // X{2,4} is X X (X (X)?)?: the optional copies one inside the other, so that a copy is tried only when
            // the one before it matched.
            EcmaRegexNode? optional = null;
            for (int k = n.Max; k > n.Min; k--)
            {
                EcmaRegexNode copy = Clone(body);
                EcmaRegexNode inner = optional is null ? copy : new EcmaRegexNode(EcmaRegexNodeKind.Cat) { Subs = [copy, optional] };
                optional = new EcmaRegexNode(EcmaRegexNodeKind.Repeat) { Min = 0, Max = 1, Lazy = n.Lazy, Subs = [inner] };
            }

            terms.Add(optional!);
        }

        return terms.Count == 1 ? terms[0] : new EcmaRegexNode(EcmaRegexNodeKind.Cat) { Subs = [.. terms] };
    }

    private static EcmaRegexNode Clone(EcmaRegexNode n)
    {
        EcmaRegexNode copy = new(n.Kind)
        {
            CodePoint = n.CodePoint,
            Set = n.Set,
            Min = n.Min,
            Max = n.Max,
            Lazy = n.Lazy,
            Group = n.Group,
            Groups = n.Groups,
            Name = n.Name,
            Fold = n.Fold,
            Behind = n.Behind,
            Negative = n.Negative,
        };
        if (n.Subs.Length > 0)
        {
            EcmaRegexNode[] subs = new EcmaRegexNode[n.Subs.Length];
            for (int k = 0; k < subs.Length; k++)
            {
                subs[k] = Clone(n.Subs[k]);
            }

            copy.Subs = subs;
        }

        return copy;
    }

    private static bool HasCapture(EcmaRegexNode n)
    {
        if ((n.Kind == EcmaRegexNodeKind.Group && n.Group > 0) || n.Kind == EcmaRegexNodeKind.Backref || n.Kind == EcmaRegexNodeKind.Look)
        {
            return true;
        }

        foreach (EcmaRegexNode sub in n.Subs)
        {
            if (HasCapture(sub))
            {
                return true;
            }
        }

        return false;
    }

    private static int Size(EcmaRegexNode n)
    {
        int size = 1;
        foreach (EcmaRegexNode sub in n.Subs)
        {
            size += Size(sub);
        }

        return size;
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

    /// <summary>
    /// Marks each greedy single character loop that can keep no choice: when nothing that follows it (the quick of
    /// the next instruction) can consume a character of its set, a way that gives a character back stands before a
    /// character of the set, which what follows cannot take, so it fails at once. V8 and .NET make the same loop
    /// atomic. A loop inside a lookbehind is left alone, as is one whose follower can end consuming nothing.
    /// </summary>
    /// <param name="insts">The instructions.</param>
    /// <param name="quick">The quick of each instruction.</param>
    public static void SetAtomic(Inst[] insts, Quick[] quick)
    {
        for (int pc = 0; pc + 1 < insts.Length; pc++)
        {
            Inst i = insts[pc];
            if (i.Op != Op.Rep)
            {
                continue;
            }

            Quick follow = quick[pc + 1];
            if (follow.Mid)
            {
                continue;
            }

            bool disjoint;
            if (i.Set is not null)
            {
                disjoint = (follow.Ascii0 & i.Set.Ascii0) == 0 && (follow.Ascii1 & i.Set.Ascii1) == 0 && !(follow.Other && i.Set.HasNonAscii);
            }
            else
            {
                disjoint = i.R < 0x80 ? !(i.R < 64 ? ((follow.Ascii0 >> i.R) & 1) != 0 : ((follow.Ascii1 >> (i.R - 64)) & 1) != 0) : !follow.Other;
            }

            if (disjoint)
            {
                insts[pc].Atomic = true;
            }
        }
    }

    /// <summary>
    /// A literal of two characters or more that every match holds at a fixed distance from its start, with that
    /// distance, or empty and -1. The terms before it in the top-level sequence must each be one character or class
    /// of one code unit, or a counted repetition of one, so that the distance is a fixed number of units over
    /// UTF-16; over UTF-8 it is fixed only when those terms are ASCII. The search for a start then looks for the
    /// literal and tries the one start it allows.
    /// </summary>
    /// <param name="root">The root of the tree.</param>
    /// <param name="unicode">Whether the pattern is matched by code point.</param>
    /// <param name="offset">The distance over UTF-16, or -1.</param>
    /// <param name="offsetUtf8">The distance over UTF-8, or -1 when a term before the literal is not ASCII.</param>
    /// <param name="count">The number of characters before the literal (the least, when it varies), or -1.</param>
    /// <param name="countMax">The greatest number of characters before the literal when it varies (a bounded repetition of one character among the terms before it), or -1 when it is fixed.</param>
    /// <returns>The literal, or empty.</returns>
    public static string RequiredLiteral(EcmaRegexNode root, bool unicode, out int offset, out int offsetUtf8, out int count, out int countMax)
    {
        offset = -1;
        offsetUtf8 = -1;
        count = -1;
        countMax = -1;
        EcmaRegexNode n = root;
        while (n.Kind == EcmaRegexNodeKind.Group)
        {
            n = n.Subs[0];
        }

        if (n.Kind == EcmaRegexNodeKind.Alt)
        {
            // A literal every alternative holds at the same distance from its start is one every match holds.
            string shared = string.Empty;
            foreach (EcmaRegexNode alternative in n.Subs)
            {
                string literal = RequiredLiteral(alternative, unicode, out int o, out int o8, out int c, out int cm);
                if (literal.Length == 0 || (shared.Length != 0 && (literal != shared || o != offset || o8 != offsetUtf8 || c != count || cm != countMax)))
                {
                    offset = -1;
                    offsetUtf8 = -1;
                    count = -1;
                    countMax = -1;
                    return string.Empty;
                }

                shared = literal;
                offset = o;
                offsetUtf8 = o8;
                count = c;
                countMax = cm;
            }

            return shared;
        }

        if (n.Kind != EcmaRegexNodeKind.Cat)
        {
            return string.Empty;
        }

        int distance = 0;
        int spread = 0;
        bool ascii = true;
        bool fixedUnits = true;
        for (int k = 0; k < n.Subs.Length; k++)
        {
            EcmaRegexNode term = n.Subs[k];
            if (term.Kind == EcmaRegexNodeKind.Char && IsLiteralRune(term.CodePoint))
            {
                int j = k;
                StringBuilder sb = new();
                while (j < n.Subs.Length && n.Subs[j].Kind == EcmaRegexNodeKind.Char && IsLiteralRune(n.Subs[j].CodePoint))
                {
                    AppendRune(sb, n.Subs[j].CodePoint);
                    j++;
                }

                // One character is enough when something comes before it: the search for it is vectorised, and the
                // start it allows is tried once. At the very start one character would be a prefix.
                if (j - k >= 2 || distance > 0 || spread > 0)
                {
                    count = distance;
                    if (spread > 0)
                    {
                        // The distance varies: each occurrence allows the starts that many characters before it,
                        // which the search tries in order.
                        countMax = distance + spread;
                        return sb.ToString();
                    }

                    offset = fixedUnits ? distance : -1;
                    offsetUtf8 = ascii ? distance : -1;
                    return sb.ToString();
                }
            }

            if (term.Kind == EcmaRegexNodeKind.Repeat && term.Min != term.Max && term.Max != EcmaRegexNode.Unbounded && !term.Lazy
                && term.Max - term.Min + spread <= 64 && FixedCharacterCount(term.Subs[0], unicode, out _, out _) == 1)
            {
                // A bounded repetition of one character: so many to so many characters before the literal.
                distance += term.Min;
                spread += term.Max - term.Min;
                continue;
            }

            int width = FixedCharacterCount(term, unicode, out bool termAscii, out bool termFixedUnits);
            if (width < 0)
            {
                return string.Empty;
            }

            ascii &= termAscii;
            fixedUnits &= termFixedUnits;
            distance += width;
        }

        return string.Empty;
    }

    /// <summary>The literals of an alternation that is the whole pattern (each alternative a run of literal characters), or empty.</summary>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The literals.</returns>
    public static string[] LiteralAlternatives(EcmaRegexNode root)
    {
        EcmaRegexNode n = root;
        while (n.Kind == EcmaRegexNodeKind.Group && n.Group == 0)
        {
            n = n.Subs[0];
        }

        return n.Kind == EcmaRegexNodeKind.Alt ? LiteralAlternativesOf(n) : [];
    }

    /// <summary>The literals of an alternation node (each alternative a run of literal characters), or empty.</summary>
    /// <param name="n">The alternation.</param>
    /// <returns>The literals.</returns>
    public static string[] LiteralAlternativesOf(EcmaRegexNode n)
    {
        string[] literals = new string[n.Subs.Length];
        for (int k = 0; k < n.Subs.Length; k++)
        {
            EcmaRegexNode alt = n.Subs[k];
            StringBuilder sb = new();
            if (alt.Kind == EcmaRegexNodeKind.Char && IsLiteralRune(alt.CodePoint))
            {
                AppendRune(sb, alt.CodePoint);
            }
            else if (alt.Kind == EcmaRegexNodeKind.Cat)
            {
                foreach (EcmaRegexNode term in alt.Subs)
                {
                    if (term.Kind != EcmaRegexNodeKind.Char || !IsLiteralRune(term.CodePoint))
                    {
                        return [];
                    }

                    AppendRune(sb, term.CodePoint);
                }
            }
            else
            {
                return [];
            }

            if (sb.Length == 0)
            {
                return [];
            }

            literals[k] = sb.ToString();
        }

        return literals;
    }

    /// <summary>
    /// The alternatives of an alternation that is the whole pattern, when each is a run of characters or small
    /// classes of single characters (a letter under the i flag is the class of its cases) and some character has
    /// more than one form: for each alternative, for each character, its forms as code points, the pattern's own
    /// first. Empty otherwise (an alternation of plain literals is <see cref="LiteralAlternatives"/>).
    /// </summary>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The forms, or empty.</returns>
    public static int[][][] CaseAlternatives(EcmaRegexNode root)
    {
        EcmaRegexNode n = root;
        while (n.Kind == EcmaRegexNodeKind.Group && n.Group == 0)
        {
            n = n.Subs[0];
        }

        return n.Kind == EcmaRegexNodeKind.Alt ? CaseAlternativesOf(n) : [];
    }

    /// <summary>The alternatives of an alternation node as the forms of their characters (see <see cref="CaseAlternatives"/>), or empty.</summary>
    /// <param name="n">The alternation.</param>
    /// <returns>The forms, or empty.</returns>
    public static int[][][] CaseAlternativesOf(EcmaRegexNode n)
    {
        int[][][] alternatives = new int[n.Subs.Length][][];
        bool several = false;
        for (int k = 0; k < n.Subs.Length; k++)
        {
            EcmaRegexNode alt = n.Subs[k];
            EcmaRegexNode[] terms = alt.Kind == EcmaRegexNodeKind.Cat ? alt.Subs : [alt];
            int[][] forms = new int[terms.Length][];
            for (int t = 0; t < terms.Length; t++)
            {
                EcmaRegexNode term = terms[t];
                if (term.Kind == EcmaRegexNodeKind.Char && !IsSurrogate(term.CodePoint))
                {
                    forms[t] = [term.CodePoint];
                    continue;
                }

                if (term.Kind == EcmaRegexNodeKind.Set && term.Set.Length >= 4 && term.Set.Length <= 8)
                {
                    int[] members = new int[term.Set.Length / 2];
                    for (int m = 0; m < term.Set.Length; m += 2)
                    {
                        if (term.Set[m] != term.Set[m + 1] || IsSurrogate(term.Set[m]))
                        {
                            return [];
                        }

                        members[m / 2] = term.Set[m];
                    }

                    forms[t] = members;
                    several = true;
                    continue;
                }

                return [];
            }

            if (forms.Length == 0)
            {
                return [];
            }

            alternatives[k] = forms;
        }

        return several ? alternatives : [];

        static bool IsSurrogate(int c) => c >= 0xD800 && c <= 0xDFFF;
    }

    /// <summary>
    /// The literal (or, as <paramref name="alternation"/>, the alternation of literals) that follows a
    /// single-character loop every match starts with, when the pattern starts with such a loop (greedy, over an
    /// ASCII set) and a literal or an alternation of literals comes next. For a loop with no upper bound and a
    /// least count of 0 or 1, the search looks for the literal and walks back over the run of the set to the
    /// start it allows (as .NET's literal-after-loop search does); for a loop with an upper bound of 64 or less,
    /// each occurrence of the literal or of an alternative allows the starts from which the loop could reach it,
    /// which are tried in order (<c>.{0,2}(Tom|Sawyer|Huckleberry|Finn)</c>). Only for a pattern with no
    /// backreference.
    /// </summary>
    /// <param name="root">The root of the tree.</param>
    /// <param name="c">The compiler, for the sets.</param>
    /// <param name="set">The set of the loop, or null.</param>
    /// <param name="min">The least count of the loop.</param>
    /// <param name="max">The greatest count of the loop, or -1 for none.</param>
    /// <param name="alternation">The alternation after the loop, or null.</param>
    /// <returns>The literal, or empty.</returns>
    public static string LiteralAfterLoop(EcmaRegexNode root, Compiler c, out CharSet? set, out int min, out int max, out AlternationSearch? alternation)
    {
        set = null;
        min = 0;
        max = -1;
        alternation = null;
        EcmaRegexNode n = root;
        while (n.Kind == EcmaRegexNodeKind.Group)
        {
            n = n.Subs[0];
        }

        if (n.Kind != EcmaRegexNodeKind.Cat || n.Subs.Length < 2)
        {
            return string.Empty;
        }

        EcmaRegexNode loop = n.Subs[0];
        while (loop.Kind == EcmaRegexNodeKind.Group)
        {
            loop = loop.Subs[0];
        }

        if (loop.Kind != EcmaRegexNodeKind.Repeat || loop.Lazy)
        {
            return string.Empty;
        }

        bool bounded = loop.Max != EcmaRegexNode.Unbounded;
        if (bounded ? loop.Max > 64 || loop.Max < loop.Min : loop.Min > 1)
        {
            return string.Empty;
        }

        EcmaRegexNode body = loop.Subs[0];
        CharSet loopSet;
        if (body.Kind == EcmaRegexNodeKind.Set && body.Set.Length > 0 && body.Set[body.Set.Length - 1] < 0x80)
        {
            loopSet = c.Class(body);
        }
        else if (body.Kind == EcmaRegexNodeKind.Char && body.CodePoint < 0x80)
        {
            loopSet = new CharSet([body.CodePoint, body.CodePoint]);
        }
        else if (bounded && body.Kind == EcmaRegexNodeKind.Set && body.Set.Length > 0)
        {
            // A bounded loop over a set beyond ASCII (the dot): the starts are tried, not walked back over.
            loopSet = c.Class(body);
        }
        else
        {
            return string.Empty;
        }

        if (bounded)
        {
            // What follows: a literal, or an alternation of literals (exact, or in their forms).
            EcmaRegexNode next = n.Subs[1];
            while (next.Kind == EcmaRegexNodeKind.Group)
            {
                next = next.Subs[0];
            }

            if (next.Kind == EcmaRegexNodeKind.Alt)
            {
                string[] literals = LiteralAlternativesOf(next);
                alternation = literals.Length >= 2 ? AlternationSearch.Create(literals) : (CaseAlternativesOf(next) is { Length: >= 2 } forms ? AlternationSearch.CreateForms(forms) : null);
                if (alternation is null)
                {
                    return string.Empty;
                }

                set = loopSet;
                min = loop.Min;
                max = loop.Max;
                return string.Empty;
            }
        }

        StringBuilder sb = new();
        for (int j = 1; j < n.Subs.Length && n.Subs[j].Kind == EcmaRegexNodeKind.Char && IsLiteralRune(n.Subs[j].CodePoint); j++)
        {
            AppendRune(sb, n.Subs[j].CodePoint);
        }

        if (sb.Length == 0)
        {
            return string.Empty;
        }

        // The loop may take the literal's own characters ([a-zA-Z]+ing): the first occurrence of the literal
        // still lies in the first run of the set that holds one, and that run's start is the leftmost start, from
        // which the loop gives back to the literal.

        set = loopSet;
        min = loop.Min;
        max = bounded ? loop.Max : -1;
        return sb.ToString();
    }

    /// <summary>
    /// The literal every match starts with, when its characters each have one or more forms and it is two
    /// characters or more with some character of more than one form: a run of terms at the start of the
    /// top-level sequence that are each a character or a class of up to four single characters (which is how the
    /// parser reads a letter under the i flag: the letter in each of its cases), or empty. The search for a start
    /// is then one anchored search for the literal in any of its forms (see <see cref="Needle{T}"/>).
    /// </summary>
    /// <param name="root">The root of the tree.</param>
    /// <returns>For each character, its forms as code points, the pattern's own first; or empty.</returns>
    public static int[][] CaseForms(EcmaRegexNode root)
    {
        EcmaRegexNode n = root;
        while (n.Kind == EcmaRegexNodeKind.Group && n.Group == 0)
        {
            n = n.Subs[0];
        }

        EcmaRegexNode[] terms = n.Kind == EcmaRegexNodeKind.Cat ? n.Subs : [n];
        List<int[]> forms = [];
        bool several = false;
        foreach (EcmaRegexNode term in terms)
        {
            if (term.Kind == EcmaRegexNodeKind.Char && !IsSurrogate(term.CodePoint))
            {
                forms.Add([term.CodePoint]);
                continue;
            }

            if (term.Kind == EcmaRegexNodeKind.Set && term.Set.Length >= 4 && term.Set.Length <= 8)
            {
                int[] members = new int[term.Set.Length / 2];
                bool single = true;
                for (int k = 0; k < term.Set.Length; k += 2)
                {
                    single &= term.Set[k] == term.Set[k + 1] && !IsSurrogate(term.Set[k]);
                    members[k / 2] = term.Set[k];
                }

                if (!single)
                {
                    break;
                }

                forms.Add(members);
                several = true;
                continue;
            }

            break;
        }

        return forms.Count >= 2 && several ? [.. forms] : [];

        static bool IsSurrogate(int c) => c >= 0xD800 && c <= 0xDFFF;
    }

    /// <summary>
    /// The number of characters a term always consumes, when it is one character or class or a counted
    /// repetition of one, or -1. <paramref name="ascii"/> says every character of it is ASCII, so the count is a
    /// count of units over either encoding; <paramref name="fixedUnits"/> says every character of it is one UTF-16
    /// code unit, so the count is a count of units over UTF-16.
    /// </summary>
    private static int FixedCharacterCount(EcmaRegexNode term, bool unicode, out bool ascii, out bool fixedUnits)
    {
        ascii = false;
        fixedUnits = false;
        switch (term.Kind)
        {
            case EcmaRegexNodeKind.Bol:
            case EcmaRegexNodeKind.Eol:
            case EcmaRegexNodeKind.LineStart:
            case EcmaRegexNodeKind.LineEnd:
            case EcmaRegexNodeKind.WordBoundary:
            case EcmaRegexNodeKind.NotWordBoundary:
                ascii = true;
                fixedUnits = true;
                return 0;
            case EcmaRegexNodeKind.Char:
                ascii = term.CodePoint < 0x80;
                fixedUnits = term.CodePoint <= 0xFFFF;
                return 1;
            case EcmaRegexNodeKind.Set:
            {
                if (term.Set.Length == 0)
                {
                    return -1;
                }

                int last = term.Set[term.Set.Length - 1];
                ascii = last < 0x80;

                // By code point a character beyond the Basic Multilingual Plane is two units; by code unit every
                // member is one unit.
                fixedUnits = !unicode || last <= 0xFFFF;
                return 1;
            }

            case EcmaRegexNodeKind.Repeat:
            {
                if (term.Min != term.Max || term.Min > 64)
                {
                    return -1;
                }

                int w = FixedCharacterCount(term.Subs[0], unicode, out ascii, out fixedUnits);
                return w < 0 ? -1 : w * term.Min;
            }

            case EcmaRegexNodeKind.Group:
                return term.Group == 0 ? FixedCharacterCount(term.Subs[0], unicode, out ascii, out fixedUnits) : -1;
            default:
                return -1;
        }
    }

    /// <summary>Whether every member of a set is an ASCII word character.</summary>
    private static bool IsWordSubset(CharSet set)
    {
        for (int c = 0; c < 128; c++)
        {
            if (set.HasAscii(c) && !Utf8Encoding.IsWordUnit(c))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>Appends a code point as UTF-16.</summary>
    /// <param name="sb">The builder.</param>
    /// <param name="r">The code point.</param>
    internal static void AppendRune(StringBuilder sb, int r)
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

    /// <summary>The set of a Set node, shared across the program.</summary>
    /// <param name="n">The node.</param>
    /// <returns>The set.</returns>
    public CharSet Class(EcmaRegexNode n)
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
