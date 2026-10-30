// <copyright file="RegexImage.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// A compiled regular expression as data: everything the constructor and the first matches work out from a pattern
/// (the two programs, the automaton over ASCII and the search automata), written by the source generator into the code
/// it generates and read by the library in place of parsing and compiling (see docs/plan/compiled-regex-as-data.md).
/// </summary>
/// <remarks>
/// <para>
/// An image records the fingerprint of the engine that wrote it (<see cref="EngineFingerprint"/>, a hash of the
/// parser's, the compiler's and the engine's sources) and the settings it was written under. A reader whose engine or
/// settings differ ignores it and parses the pattern, so an image only ever saves work: a regular expression read from
/// one answers as one compiled from its pattern.
/// </para>
/// <para>
/// What an image holds is values: the instructions, the sets as their ranges, the literals, the tables of the automata.
/// The objects that depend on the machine or the runtime (the searchers over the sets and the literals, the packed
/// tables of the vectorised searches) are made from those values by the constructors that make them from a pattern.
/// </para>
/// </remarks>
internal static class RegexImage
{
    /// <summary>The version of the layout of an image.</summary>
    internal const byte FormatVersion = 1;

    // "CRI": a Corvus regular expression image.
    private const byte Magic0 = (byte)'C';
    private const byte Magic1 = (byte)'R';
    private const byte Magic2 = (byte)'I';

    private const int NoAlternation = 0;
    private const int LiteralAlternation = 1;
    private const int FormsAlternation = 2;

    /// <summary>
    /// Reads a pattern and compiles its first program (the one that finds a match's bounds), as the constructor does.
    /// The reader and the compiler go a level deeper for each level of groups (and with the v flag, of classes), so a
    /// pattern that nests deeply is read and compiled on a stack of its own (see <see cref="DeepStack"/>).
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <returns>The parser, the tree and the program.</returns>
    internal static (EcmaRegexParser Parser, EcmaRegexNode Root, Program Program) Compile(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        bool unicode = (flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;
        return DeepStack.Run(
            pattern,
            (flags & RegexFlags.UnicodeSets) != 0,
            () =>
            {
                EcmaRegexParser parser = new(
                    pattern.AsSpan(),
                    unicode,
                    ignoreCase: (flags & RegexFlags.IgnoreCase) != 0,
                    multiline: (flags & RegexFlags.Multiline) != 0,
                    dotAll: (flags & RegexFlags.DotAll) != 0,
                    unicodeSets: (flags & RegexFlags.UnicodeSets) != 0,
                    edition: edition,
                    unicodeVersion: unicodeVersion);
                EcmaRegexNode root = parser.Parse();
                return (parser, root, Compiler.Compile(root, unicode, parser.GroupCount, trackAll: false));
            });
    }

    /// <summary>
    /// Compiles a pattern and builds everything its regular expression would build on first use, then writes it all
    /// as an image.
    /// </summary>
    /// <param name="pattern">The pattern, which must be valid in the grammar of its flags and edition.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <returns>The image.</returns>
    internal static byte[] Write(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        bool unicode = (flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;
        bool sets = (flags & RegexFlags.UnicodeSets) != 0;
        (EcmaRegexParser parser, EcmaRegexNode root, Program bounds) = Compile(pattern, flags, edition, unicodeVersion);
        bool linear = !parser.HasBackreferences;

        // The order the regular expression builds them in on first use: each compiles from the tree the first program
        // left simplified, and the automaton reads it.
        Dfa? dfa = linear ? DeepStack.Run(pattern, sets, () => Dfa.Build(root)) : null;
        Program captures = DeepStack.Run(pattern, sets, () => Compiler.Compile(root, unicode, parser.GroupCount, trackAll: true));
        SearchAutomata boundsAutomata = bounds.Automata();
        SearchAutomata captureAutomata = captures.Automata();

        Writer payload = new();
        payload.Bool(linear);
        payload.Int(parser.GroupCount);
        payload.Int(parser.GroupNames.Count);
        foreach (KeyValuePair<string, int[]> name in parser.GroupNames)
        {
            payload.String(name.Key);
            payload.Ints(name.Value);
        }

        WriteProgram(payload, bounds, boundsAutomata);
        WriteProgram(payload, captures, captureAutomata);
        payload.Bool(dfa is not null);
        dfa?.WriteTo(payload);
        byte[] body = payload.ToArray();

        Writer image = new();
        image.Byte(Magic0);
        image.Byte(Magic1);
        image.Byte(Magic2);
        image.Byte(FormatVersion);
        image.String(EngineFingerprint.Value);
        image.Int((int)flags);
        image.Int((int)edition);
        image.Int((int)UnicodeTables.Resolve(unicodeVersion));
        image.Int(body.Length);
        image.ULong(Checksum(body));
        image.Raw(body);
        return image.ToArray();
    }

    /// <summary>
    /// Reads an image written for the settings given by this engine. False, with nothing read, for an image of another
    /// engine, layout or settings, or one that is damaged.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="flags">The flags the regular expression is made with.</param>
    /// <param name="edition">The edition it is made with.</param>
    /// <param name="resolvedUnicodeVersion">The version of Unicode it is made with, resolved.</param>
    /// <param name="parts">What the image holds.</param>
    /// <returns><see langword="true"/> when the image was read.</returns>
    internal static bool TryRead(ReadOnlySpan<byte> image, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion resolvedUnicodeVersion, out Parts parts)
    {
        parts = default;
        Reader header = new(image);
        if (header.Byte() != Magic0 || header.Byte() != Magic1 || header.Byte() != Magic2 || header.Byte() != FormatVersion
            || header.String() != EngineFingerprint.Value
            || header.Int() != (int)flags || header.Int() != (int)edition || header.Int() != (int)resolvedUnicodeVersion)
        {
            return false;
        }

        int length = header.Int();
        ulong checksum = header.ULong();
        ReadOnlySpan<byte> body = header.Rest();
        if (!header.Ok || length != body.Length || Checksum(body) != checksum)
        {
            return false;
        }

        Reader r = new(body);
        bool linear = r.Bool();
        int groupCount = r.Int();
        int names = r.Count();
        List<KeyValuePair<string, int[]>> groupNames = new(r.Ok ? names : 0);
        for (int k = 0; k < names && r.Ok; k++)
        {
            groupNames.Add(new KeyValuePair<string, int[]>(r.String(), r.Ints()));
        }

        Program? bounds = ReadProgram(ref r);
        Program? captures = ReadProgram(ref r);
        Dfa? dfa = r.Bool() ? Dfa.ReadFrom(ref r) : null;
        if (!r.Ok || !r.AtEnd || bounds is null || captures is null || groupCount < 0)
        {
            return false;
        }

        parts = new Parts(bounds, captures, dfa, linear, groupCount, groupNames);
        return true;
    }

    /// <summary>FNV-1a over 64 bits: a check against damage, not against tampering.</summary>
    private static ulong Checksum(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in data)
        {
            h ^= b;
            h *= 1099511628211UL;
        }

        return h;
    }

    private static void WriteProgram(Writer w, Program p, SearchAutomata automata)
    {
        // The sets, each once, numbered from 1 (0 is none) in the order they are met.
        Dictionary<CharSet, int> index = new(SameSet.Instance);
        List<CharSet> sets = [];
        int Index(CharSet? set)
        {
            if (set is null)
            {
                return 0;
            }

            if (!index.TryGetValue(set, out int k))
            {
                sets.Add(set);
                k = sets.Count;
                index.Add(set, k);
            }

            return k;
        }

        foreach (Inst i in p.Insts)
        {
            Index(i.Set);
        }

        Index(p.First);
        Index(p.LoopSet);
        foreach (CharSet set in p.AfterLoopSets)
        {
            Index(set);
        }

        w.Int(sets.Count);
        foreach (CharSet set in sets)
        {
            w.Ints(set.Ranges);
        }

        w.Int(p.Insts.Length);
        foreach (Inst i in p.Insts)
        {
            w.Int((int)i.Op);
            w.Bool(i.Flag);
            w.Int(i.R);
            w.Int(Index(i.Set));
            w.Int(i.Lit);
            w.Int(i.Len);
            w.Int(i.X);
            w.Int(i.Y);
            w.Int(i.A);
            w.Int(i.B);
            w.Int(i.Min);
            w.Int(i.Max);
            w.Int(i.Need);
            w.Int(i.Iter);
            w.Int(i.M);
            w.Bool(i.Atomic);
        }

        w.Int(p.Groups);
        w.Int(p.Loops);
        w.Bool(p.Units);
        w.Bool(p.Anchored);
        w.String(p.Utf16Prefix);
        w.Int(p.Need);
        w.Int(p.Points.Length);
        foreach (MemoPoint m in p.Points)
        {
            w.Int(m.Loop0);
            w.Int(m.Loop1);
            w.Int(m.Min0);
            w.Int(m.Min1);
        }

        w.Int(p.Quick.Length);
        foreach (Quick q in p.Quick)
        {
            w.ULong(q.Ascii0);
            w.ULong(q.Ascii1);
            w.Bool(q.Other);
            w.Bool(q.Mid);
            w.Bool(q.End);
        }

        w.Int(Index(p.First));
        w.Int(p.Utf8Literals.Length);
        foreach (byte[] literal in p.Utf8Literals)
        {
            w.Bytes(literal);
        }

        w.Strings(p.Utf16Literals);
        w.String(p.RequiredUtf16);
        w.Int(p.RequiredOffset);
        w.IntArrays(p.CaseForms);
        w.Int(p.RequiredOffsetUtf8);
        w.Int(Index(p.LoopSet));
        w.Int(p.LoopMin);
        w.String(p.AfterLoopUtf16);
        w.Bool(p.SkipRun);
        w.Int(p.RequiredCount);
        w.Int(p.AfterLoopMax);
        WriteAlternation(w, p.AfterLoopAlternation);
        w.Int(p.RequiredCountMax);
        w.Int(p.AfterLoopSets.Length);
        foreach (CharSet set in p.AfterLoopSets)
        {
            w.Int(Index(set));
        }

        w.Ints(p.AfterLoopMins);
        WriteAlternation(w, p.RequiredAlternation);
        w.Int(p.RequiredAlternationLo);
        w.Int(p.RequiredAlternationHi);

        // What the compiler sets once the program is made.
        w.Strings(p.ShapeAlternatives);
        w.Int(p.ShapeCaseAlternatives.Length);
        foreach (int[][] alternative in p.ShapeCaseAlternatives)
        {
            w.IntArrays(alternative);
        }

        w.Bool(p.LeadingWordBoundary);
        w.Bool(p.AfterLoopEveryStart);
        w.Bool(p.UsesCursor);
        w.Bool(p.Unicode);
        w.Bool(p.ForwardSearch);
        w.Bool(p.HasWindow);
        automata.WriteTo(w);
    }

    private static Program? ReadProgram(ref Reader r)
    {
        int setCount = r.Count();
        CharSet[] sets = new CharSet[r.Ok ? setCount : 0];
        for (int k = 0; k < sets.Length && r.Ok; k++)
        {
            int[] ranges = r.Ints();
            if (!IsNormalForm(ranges))
            {
                r.Fail();
                break;
            }

            sets[k] = new CharSet(ranges);
        }

        CharSet? Set(ref Reader rd)
        {
            int k = rd.Int();
            if (k == 0)
            {
                return null;
            }

            if ((uint)(k - 1) >= (uint)sets.Length)
            {
                rd.Fail();
                return null;
            }

            return sets[k - 1];
        }

        int instCount = r.Count();
        Inst[] insts = new Inst[r.Ok ? instCount : 0];
        for (int k = 0; k < insts.Length && r.Ok; k++)
        {
            int op = r.Int();
            if (op < 0 || op > (int)Op.Match)
            {
                r.Fail();
                break;
            }

            insts[k] = new Inst((Op)op)
            {
                Flag = r.Bool(),
                R = r.Int(),
                Set = Set(ref r),
                Lit = r.Int(),
                Len = r.Int(),
                X = r.Int(),
                Y = r.Int(),
                A = r.Int(),
                B = r.Int(),
                Min = r.Int(),
                Max = r.Int(),
                Need = r.Int(),
                Iter = r.Int(),
                M = r.Int(),
                Atomic = r.Bool(),
            };
        }

        int groups = r.Int();
        int loops = r.Int();
        bool units = r.Bool();
        bool anchored = r.Bool();
        string prefix = r.String();
        int need = r.Int();
        int pointCount = r.Count();
        MemoPoint[] points = new MemoPoint[r.Ok ? pointCount : 0];
        for (int k = 0; k < points.Length && r.Ok; k++)
        {
            points[k] = new MemoPoint { Loop0 = r.Int(), Loop1 = r.Int(), Min0 = r.Int(), Min1 = r.Int() };
        }

        int quickCount = r.Count();
        Quick[] quick = new Quick[r.Ok ? quickCount : 0];
        for (int k = 0; k < quick.Length && r.Ok; k++)
        {
            quick[k] = new Quick { Ascii0 = r.ULong(), Ascii1 = r.ULong(), Other = r.Bool(), Mid = r.Bool(), End = r.Bool() };
        }

        CharSet? first = Set(ref r);
        int literalCount = r.Count();
        byte[][] utf8Literals = new byte[r.Ok ? literalCount : 0][];
        for (int k = 0; k < utf8Literals.Length && r.Ok; k++)
        {
            utf8Literals[k] = r.Bytes();
        }

        string[] utf16Literals = r.Strings();
        string required = r.String();
        int requiredOffset = r.Int();
        int[][] caseForms = r.IntArrays();
        int requiredOffsetUtf8 = r.Int();
        CharSet? loopSet = Set(ref r);
        int loopMin = r.Int();
        string afterLoop = r.String();
        bool skipRun = r.Bool();
        int requiredCount = r.Int();
        int afterLoopMax = r.Int();
        AlternationSearch? afterLoopAlternation = ReadAlternation(ref r);
        int requiredCountMax = r.Int();
        int afterLoopSetCount = r.Count();
        CharSet[] afterLoopSets = new CharSet[r.Ok ? afterLoopSetCount : 0];
        for (int k = 0; k < afterLoopSets.Length && r.Ok; k++)
        {
            afterLoopSets[k] = Set(ref r) ?? Failed<CharSet>(ref r);
        }

        int[] afterLoopMins = r.Ints();
        AlternationSearch? requiredAlternation = ReadAlternation(ref r);
        int requiredAlternationLo = r.Int();
        int requiredAlternationHi = r.Int();
        string[] shapeAlternatives = r.Strings();
        int shapeCaseCount = r.Count();
        int[][][] shapeCaseAlternatives = new int[r.Ok ? shapeCaseCount : 0][][];
        for (int k = 0; k < shapeCaseAlternatives.Length && r.Ok; k++)
        {
            shapeCaseAlternatives[k] = r.IntArrays();
        }

        bool leadingWordBoundary = r.Bool();
        bool afterLoopEveryStart = r.Bool();
        bool usesCursor = r.Bool();
        bool unicode = r.Bool();
        bool forwardSearch = r.Bool();
        bool hasWindow = r.Bool();
        SearchAutomata? automata = SearchAutomata.ReadFrom(ref r);
        if (!r.Ok || automata is null || !Consistent(insts, quick, points, groups, utf8Literals, utf16Literals))
        {
            return null;
        }

        Program p = new(insts, groups, loops, units, anchored, prefix, need, points, quick, first, utf8Literals, utf16Literals, required, requiredOffset, caseForms, requiredOffsetUtf8, loopSet, loopMin, afterLoop, skipRun, requiredCount, afterLoopMax, afterLoopAlternation, requiredCountMax, afterLoopSets, afterLoopMins, requiredAlternation, requiredAlternationLo, requiredAlternationHi);
        p.SetShape(shapeAlternatives, shapeCaseAlternatives);
        p.LeadingWordBoundary = leadingWordBoundary;
        p.AfterLoopEveryStart = afterLoopEveryStart;
        p.UsesCursor = usesCursor;
        p.Unicode = unicode;
        p.ForwardSearch = forwardSearch;
        p.HasWindow = hasWindow;
        p.SetAutomata(automata);
        return p;
    }

    /// <summary>
    /// Whether the references between the parts of a program read from an image stay inside it: every jump to an
    /// instruction, every literal, memo point and quick table an instruction names. The checksum already guards
    /// against damage; this keeps a matcher from reading outside its arrays even so.
    /// </summary>
    private static bool Consistent(Inst[] insts, Quick[] quick, MemoPoint[] points, int groups, byte[][] utf8Literals, string[] utf16Literals)
    {
        if (insts.Length == 0 || insts[insts.Length - 1].Op != Op.Match || quick.Length != insts.Length || groups < 0
            || utf8Literals.Length != utf16Literals.Length)
        {
            return false;
        }

        foreach (Inst i in insts)
        {
            if (i.X < -1 || i.X > insts.Length || i.Y < -1 || i.Y > insts.Length
                || i.Lit < -1 || i.Lit >= utf16Literals.Length || i.M < 0 || i.M > points.Length)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether ranges are in the normal form a set keeps: pairs of code points, ascending and apart.</summary>
    private static bool IsNormalForm(int[] ranges)
    {
        if ((ranges.Length & 1) != 0)
        {
            return false;
        }

        int last = -2;
        for (int k = 0; k < ranges.Length; k += 2)
        {
            int lo = ranges[k];
            int hi = ranges[k + 1];
            if (lo <= last + 1 || hi < lo || hi > 0x10FFFF)
            {
                return false;
            }

            last = hi;
        }

        return true;
    }

    private static T Failed<T>(ref Reader r)
        where T : class
    {
        r.Fail();
        return null!;
    }

    private static void WriteAlternation(Writer w, AlternationSearch? search)
    {
        if (search is null)
        {
            w.Int(NoAlternation);
        }
        else if (search.Forms is int[][][] forms)
        {
            w.Int(FormsAlternation);
            w.Int(forms.Length);
            foreach (int[][] form in forms)
            {
                w.IntArrays(form);
            }
        }
        else
        {
            w.Int(LiteralAlternation);
            w.Strings(search.Literals);
        }
    }

    private static AlternationSearch? ReadAlternation(ref Reader r)
    {
        switch (r.Int())
        {
            case NoAlternation:
                return null;
            case LiteralAlternation:
                string[] literals = r.Strings();
                return r.Ok && literals.Length > 0 ? AlternationSearch.Create(literals) : Failed<AlternationSearch>(ref r);
            case FormsAlternation:
                int count = r.Count();
                int[][][] forms = new int[r.Ok ? count : 0][][];
                for (int k = 0; k < forms.Length && r.Ok; k++)
                {
                    forms[k] = r.IntArrays();
                }

                return r.Ok ? AlternationSearch.CreateForms(forms) ?? Failed<AlternationSearch>(ref r) : null;
            default:
                return Failed<AlternationSearch>(ref r);
        }
    }

    /// <summary>Sets compared by reference: each set an image holds once, as the program holds it.</summary>
    private sealed class SameSet : IEqualityComparer<CharSet>
    {
        public static readonly SameSet Instance = new();

        public bool Equals(CharSet? x, CharSet? y) => ReferenceEquals(x, y);

        public int GetHashCode(CharSet obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>What an image holds.</summary>
    /// <param name="bounds">The program that finds a match's bounds.</param>
    /// <param name="captures">The program that tracks every group.</param>
    /// <param name="dfa">The automaton over ASCII, or null when the pattern has none.</param>
    /// <param name="linear">Whether the pattern has no backreference.</param>
    /// <param name="groupCount">The number of groups.</param>
    /// <param name="groupNames">The names of the groups.</param>
    internal readonly struct Parts(Program bounds, Program captures, Dfa? dfa, bool linear, int groupCount, List<KeyValuePair<string, int[]>> groupNames)
    {
        public Program Bounds { get; } = bounds;

        public Program Captures { get; } = captures;

        public Dfa? Dfa { get; } = dfa;

        public bool Linear { get; } = linear;

        public int GroupCount { get; } = groupCount;

        public List<KeyValuePair<string, int[]>> GroupNames { get; } = groupNames;
    }

    /// <summary>Writes the values of an image: integers as variable-length zigzag numbers, strings as UTF-16 code units (a pattern's literal may hold a lone surrogate).</summary>
    internal sealed class Writer
    {
        private byte[] buffer = new byte[256];
        private int length;

        public void Byte(byte value)
        {
            if (this.length == this.buffer.Length)
            {
                Array.Resize(ref this.buffer, this.buffer.Length * 2);
            }

            this.buffer[this.length++] = value;
        }

        public void Bool(bool value) => this.Byte(value ? (byte)1 : (byte)0);

        public void Int(int value) => this.ULong((ulong)(uint)((value << 1) ^ (value >> 31)));

        public void ULong(ulong value)
        {
            while (value >= 0x80)
            {
                this.Byte((byte)(value | 0x80));
                value >>= 7;
            }

            this.Byte((byte)value);
        }

        public void UInt(uint value) => this.ULong(value);

        public void Raw(ReadOnlySpan<byte> bytes)
        {
            foreach (byte b in bytes)
            {
                this.Byte(b);
            }
        }

        public void Bytes(byte[] bytes)
        {
            this.Int(bytes.Length);
            this.Raw(bytes);
        }

        public void Bools(bool[] values)
        {
            this.Int(values.Length);
            foreach (bool b in values)
            {
                this.Bool(b);
            }
        }

        public void Ints(int[] values)
        {
            this.Int(values.Length);
            foreach (int v in values)
            {
                this.Int(v);
            }
        }

        public void UInts(uint[] values)
        {
            this.Int(values.Length);
            foreach (uint v in values)
            {
                this.UInt(v);
            }
        }

        public void UShorts(ushort[] values)
        {
            this.Int(values.Length);
            foreach (ushort v in values)
            {
                this.UInt(v);
            }
        }

        public void IntArrays(int[][] values)
        {
            this.Int(values.Length);
            foreach (int[] v in values)
            {
                this.Ints(v);
            }
        }

        public void String(string value)
        {
            this.Int(value.Length);
            foreach (char c in value)
            {
                this.UInt(c);
            }
        }

        public void Strings(string[] values)
        {
            this.Int(values.Length);
            foreach (string s in values)
            {
                this.String(s);
            }
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[this.length];
            Array.Copy(this.buffer, result, this.length);
            return result;
        }
    }

    /// <summary>
    /// Reads the values of an image. A read past the end, or of a value out of range, marks the reader failed and
    /// gives a default; the caller checks <see cref="Ok"/> once it is done, so no read throws.
    /// </summary>
    internal ref struct Reader(ReadOnlySpan<byte> data)
    {
        /// <summary>The most elements any one array of an image may claim, a bound on what a damaged count allocates.</summary>
        private const int MostElements = 1 << 24;

        private readonly ReadOnlySpan<byte> data = data;
        private int at;
        private bool failed;

        public readonly bool Ok => !this.failed;

        public readonly bool AtEnd => this.at == this.data.Length;

        public void Fail() => this.failed = true;

        public ReadOnlySpan<byte> Rest()
        {
            ReadOnlySpan<byte> rest = this.failed ? default : this.data.Slice(this.at);
            this.at = this.data.Length;
            return rest;
        }

        public byte Byte()
        {
            if (this.failed || this.at >= this.data.Length)
            {
                this.failed = true;
                return 0;
            }

            return this.data[this.at++];
        }

        public bool Bool()
        {
            byte b = this.Byte();
            if (b > 1)
            {
                this.failed = true;
            }

            return b == 1;
        }

        public ulong ULong()
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                byte b = this.Byte();
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }

            this.failed = true;
            return 0;
        }

        public uint UInt()
        {
            ulong value = this.ULong();
            if (value > uint.MaxValue)
            {
                this.failed = true;
                return 0;
            }

            return (uint)value;
        }

        public int Int()
        {
            uint z = this.UInt();
            return (int)(z >> 1) ^ -(int)(z & 1);
        }

        /// <summary>A count of elements: not negative, and no more than the bytes left could hold.</summary>
        public int Count()
        {
            int n = this.Int();
            if (n < 0 || n > MostElements || n > this.data.Length - this.at)
            {
                this.failed = true;
                return 0;
            }

            return n;
        }

        public byte[] Bytes()
        {
            int n = this.Count();
            if (this.failed)
            {
                return [];
            }

            byte[] result = this.data.Slice(this.at, n).ToArray();
            this.at += n;
            return result;
        }

        public bool[] Bools()
        {
            bool[] result = new bool[this.Count()];
            for (int k = 0; k < result.Length && !this.failed; k++)
            {
                result[k] = this.Bool();
            }

            return result;
        }

        public int[] Ints()
        {
            int[] result = new int[this.Count()];
            for (int k = 0; k < result.Length && !this.failed; k++)
            {
                result[k] = this.Int();
            }

            return result;
        }

        public uint[] UInts()
        {
            uint[] result = new uint[this.Count()];
            for (int k = 0; k < result.Length && !this.failed; k++)
            {
                result[k] = this.UInt();
            }

            return result;
        }

        public ushort[] UShorts()
        {
            ushort[] result = new ushort[this.Count()];
            for (int k = 0; k < result.Length && !this.failed; k++)
            {
                uint v = this.UInt();
                if (v > ushort.MaxValue)
                {
                    this.failed = true;
                }

                result[k] = (ushort)v;
            }

            return result;
        }

        public int[][] IntArrays()
        {
            int[][] result = new int[this.Count()][];
            for (int k = 0; k < result.Length; k++)
            {
                result[k] = this.failed ? [] : this.Ints();
            }

            return result;
        }

        public string String()
        {
            int n = this.Count();
            if (this.failed)
            {
                return string.Empty;
            }

            StringBuilder sb = new(n);
            for (int k = 0; k < n && !this.failed; k++)
            {
                uint c = this.UInt();
                if (c > char.MaxValue)
                {
                    this.failed = true;
                }

                sb.Append((char)c);
            }

            return sb.ToString();
        }

        public string[] Strings()
        {
            string[] result = new string[this.Count()];
            for (int k = 0; k < result.Length; k++)
            {
                result[k] = this.failed ? string.Empty : this.String();
            }

            return result;
        }
    }
}
