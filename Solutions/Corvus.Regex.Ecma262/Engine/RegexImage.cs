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
/// An image records the version of its layout, the revision of the compiler that wrote it and the settings it was
/// written under, and holds its parts as sections (see docs/plan/image-compatibility.md and
/// docs/contributing/compatibility.md). A reader reads every layout from <see cref="MinimumFormat"/> to
/// <see cref="FormatVersion"/>, and every image from a revision at or above <see cref="MinimumTrustedRevision"/>. It
/// ignores an image whose settings differ, which uses a section or an instruction it does not know, or which is
/// damaged, and parses the pattern instead, so an image only ever saves work: a regular expression read from one
/// answers as one compiled from its pattern.
/// </para>
/// <para>
/// What an image stores keeps its meaning once a release has written it. An opcode keeps its number, a field keeps
/// its sense, and anything new goes in a new section.
/// </para>
/// <para>
/// What an image holds is values: the instructions, the sets as their ranges, the literals, the tables of the automata.
/// The objects that depend on the machine or the runtime (the searchers over the sets and the literals, the packed
/// tables of the vectorised searches) are made from those values by the constructors that make them from a pattern.
/// </para>
/// </remarks>
internal static class RegexImage
{
    /// <summary>The version of the layout this engine writes: a header, then the parts as sections.</summary>
    internal const byte FormatVersion = 2;

    /// <summary>The oldest layout this engine reads: 1, the single body of 1.0.10 and 1.0.11.</summary>
    internal const byte MinimumFormat = 1;

    /// <summary>
    /// The revision of the compiler: raised whenever what it writes for any pattern changes, whether the change makes
    /// it smaller, faster or right. The data set's golden check fails on changed output under an unchanged revision.
    /// </summary>
    internal const int CompilerRevision = 1;

    /// <summary>
    /// The oldest revision whose images are read: raised only when a change to the compiler fixes output that gave
    /// wrong answers, so that the images written before the fix are ignored.
    /// </summary>
    internal const int MinimumTrustedRevision = 1;

    /// <summary>The section of the group count and names.</summary>
    internal const int SectionNames = 1;

    /// <summary>The section of the program that finds a match's bounds, with its search automata.</summary>
    internal const int SectionBounds = 2;

    /// <summary>The section of the program that tracks every group, with its search automata.</summary>
    internal const int SectionCaptures = 3;

    /// <summary>The section of the automaton over ASCII, present when the pattern has one.</summary>
    internal const int SectionDfa = 4;

    /// <summary>
    /// The fingerprints of the engines that wrote layout 1, the releases 1.0.10 and 1.0.11, whose images hold what
    /// revision 1 of the compiler writes. Layout 1 recorded a hash of the engine's sources in place of a revision.
    /// </summary>
    private static readonly string[] Format1Fingerprints =
    [
        "9096543f80969d8ba477181cc4a67bfeba3f76edbd27425d7b545f875daa3199",
        "8fe300ee5e122022c8e2ba87ff05861880c0986daa87d9ce086b402dcc17e98d",
    ];

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
        => Write(pattern, flags, edition, unicodeVersion, CompilerRevision, []);

    /// <summary>
    /// Writes an image as <see cref="Write(string, RegexFlags, RegexEdition, RegexUnicodeVersion)"/> does, recording
    /// the revision given and with more sections after its own (for the tests of what a reader does with them).
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="revision">The revision of the compiler to record.</param>
    /// <param name="extra">More sections: a tag, whether a reader must know it, and its bytes.</param>
    /// <returns>The image.</returns>
    internal static byte[] Write(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion, int revision, (int Tag, bool Required, byte[] Payload)[] extra)
    {
        Writer body = new();
        foreach ((int tag, bool required, byte[] payload) in Sections(pattern, flags, edition, unicodeVersion))
        {
            Section(body, tag, required, payload);
        }

        foreach ((int tag, bool required, byte[] payload) in extra)
        {
            Section(body, tag, required, payload);
        }

        Writer image = new();
        image.Byte(Magic0);
        image.Byte(Magic1);
        image.Byte(Magic2);
        image.Byte(FormatVersion);
        image.Int(revision);
        Settings(image, flags, edition, unicodeVersion);
        Body(image, body.ToArray());
        return image.ToArray();

        static void Section(Writer w, int tag, bool required, byte[] payload)
        {
            w.Int(tag);
            w.Bool(required);
            w.Bytes(payload);
        }
    }

    /// <summary>
    /// Writes an image in layout 1, as 1.0.10 and 1.0.11 wrote it, recording the fingerprint given (for the tests of
    /// how a reader treats the images those releases wrote).
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="fingerprint">The fingerprint to record.</param>
    /// <returns>The image.</returns>
    internal static byte[] WriteFormat1(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion, string fingerprint)
    {
        // Layout 1's body is the payloads of the sections in order, with whether there is an automaton over ASCII
        // before its own.
        Writer body = new();
        bool dfa = false;
        foreach ((int tag, bool _, byte[] payload) in Sections(pattern, flags, edition, unicodeVersion))
        {
            if (tag == SectionDfa)
            {
                body.Bool(true);
                dfa = true;
            }

            body.Raw(payload);
        }

        if (!dfa)
        {
            body.Bool(false);
        }

        Writer image = new();
        image.Byte(Magic0);
        image.Byte(Magic1);
        image.Byte(Magic2);
        image.Byte(1);
        image.String(fingerprint);
        Settings(image, flags, edition, unicodeVersion);
        Body(image, body.ToArray());
        return image.ToArray();
    }

    /// <summary>
    /// Reads an image written for the settings given. False, with nothing read and the reason given, for an image of
    /// a layout or a revision this engine does not read, of other settings, with a section or an instruction it does
    /// not know, or one that is damaged.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="flags">The flags the regular expression is made with.</param>
    /// <param name="edition">The edition it is made with.</param>
    /// <param name="resolvedUnicodeVersion">The version of Unicode it is made with, resolved.</param>
    /// <param name="parts">What the image holds.</param>
    /// <param name="ignored">Why the image was not read, or <see cref="Ignored.None"/>.</param>
    /// <returns><see langword="true"/> when the image was read.</returns>
    internal static bool TryRead(ReadOnlySpan<byte> image, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion resolvedUnicodeVersion, out Parts parts, out Ignored ignored)
    {
        parts = default;
        Reader header = new(image);
        if (header.Byte() != Magic0 || header.Byte() != Magic1 || header.Byte() != Magic2)
        {
            ignored = Ignored.Damaged;
            return false;
        }

        byte format = header.Byte();
        if (!header.Ok || format < MinimumFormat || format > FormatVersion)
        {
            ignored = Ignored.Format;
            return false;
        }

        int revision = format == 1 ? (Array.IndexOf(Format1Fingerprints, header.String()) >= 0 ? 1 : -1) : header.Int();
        if (!header.Ok)
        {
            ignored = Ignored.Damaged;
            return false;
        }

        if (revision < MinimumTrustedRevision)
        {
            // In layout 1, an engine whose fingerprint no release shipped: a build of a changed engine, whose
            // compiler is not known to be any revision.
            ignored = Ignored.Revision;
            return false;
        }

        if (header.Int() != (int)flags || header.Int() != (int)edition || header.Int() != (int)resolvedUnicodeVersion)
        {
            ignored = header.Ok ? Ignored.Settings : Ignored.Damaged;
            return false;
        }

        int length = header.Int();
        ulong checksum = header.ULong();
        ReadOnlySpan<byte> body = header.Rest();
        if (!header.Ok || length != body.Length || Checksum(body) != checksum)
        {
            ignored = Ignored.Damaged;
            return false;
        }

        bool unknownOpcode = false;
        Reader r = new(body);
        Program? bounds = null;
        Program? captures = null;
        Dfa? dfa = null;
        bool linear = false;
        int groupCount = -1;
        List<KeyValuePair<string, int[]>>? groupNames = null;
        if (format == 1)
        {
            ReadNames(ref r, out linear, out groupCount, out groupNames);
            bounds = ReadProgram(ref r, ref unknownOpcode);
            captures = ReadProgram(ref r, ref unknownOpcode);
            dfa = r.Bool() ? Dfa.ReadFrom(ref r) : null;
            if (unknownOpcode)
            {
                ignored = Ignored.UnknownOpcode;
                return false;
            }

            if (!r.Ok || !r.AtEnd || bounds is null || captures is null || groupCount < 0)
            {
                ignored = Ignored.Damaged;
                return false;
            }
        }
        else
        {
            int seen = 0;
            while (r.Ok && !r.AtEnd)
            {
                int tag = r.Int();
                bool required = r.Bool();
                ReadOnlySpan<byte> payload = r.Payload();
                if (!r.Ok)
                {
                    break;
                }

                if (tag is < SectionNames or > SectionDfa)
                {
                    if (required)
                    {
                        ignored = Ignored.UnknownSection;
                        return false;
                    }

                    continue;
                }

                if ((seen & (1 << tag)) != 0)
                {
                    ignored = Ignored.Damaged;
                    return false;
                }

                seen |= 1 << tag;
                Reader s = new(payload);
                switch (tag)
                {
                    case SectionNames:
                        ReadNames(ref s, out linear, out groupCount, out groupNames);
                        break;
                    case SectionBounds:
                        bounds = ReadProgram(ref s, ref unknownOpcode);
                        break;
                    case SectionCaptures:
                        captures = ReadProgram(ref s, ref unknownOpcode);
                        break;
                    default:
                        dfa = Dfa.ReadFrom(ref s);
                        if (dfa is null)
                        {
                            s.Fail();
                        }

                        break;
                }

                if (unknownOpcode)
                {
                    ignored = Ignored.UnknownOpcode;
                    return false;
                }

                if (!s.Ok || !s.AtEnd)
                {
                    ignored = Ignored.Damaged;
                    return false;
                }
            }

            if (!r.Ok)
            {
                ignored = Ignored.Damaged;
                return false;
            }

            if (bounds is null || captures is null || groupCount < 0)
            {
                ignored = Ignored.MissingSection;
                return false;
            }
        }

        parts = new Parts(bounds, captures, dfa, linear, groupCount, groupNames!, revision);
        ignored = Ignored.None;
        return true;
    }

    /// <summary>
    /// The revision of the compiler that wrote an image, read from its header alone: 1 for layout 1 from a release,
    /// and -1 for an image whose header is not one this engine reads.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The revision, or -1.</returns>
    internal static int RevisionOf(ReadOnlySpan<byte> image)
    {
        Reader header = new(image);
        if (header.Byte() != Magic0 || header.Byte() != Magic1 || header.Byte() != Magic2)
        {
            return -1;
        }

        byte format = header.Byte();
        int revision = format == 1 ? (Array.IndexOf(Format1Fingerprints, header.String()) >= 0 ? 1 : -1) : format <= FormatVersion ? header.Int() : -1;
        return header.Ok ? revision : -1;
    }

    /// <summary>
    /// The sections of the image of a pattern, in order: its names, its two programs, each with its search automata,
    /// and its automaton over ASCII when it has one. Each is required: a reader that does not know one cannot build
    /// the regular expression without the pattern's tree, which an image does not hold.
    /// </summary>
    private static List<(int Tag, bool Required, byte[] Payload)> Sections(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
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

        Writer names = new();
        names.Bool(linear);
        names.Int(parser.GroupCount);
        names.Int(parser.GroupNames.Count);
        foreach (KeyValuePair<string, int[]> name in parser.GroupNames)
        {
            names.String(name.Key);
            names.Ints(name.Value);
        }

        Writer boundsSection = new();
        WriteProgram(boundsSection, bounds, boundsAutomata);
        Writer capturesSection = new();
        WriteProgram(capturesSection, captures, captureAutomata);
        List<(int, bool, byte[])> sections =
        [
            (SectionNames, true, names.ToArray()),
            (SectionBounds, true, boundsSection.ToArray()),
            (SectionCaptures, true, capturesSection.ToArray()),
        ];

        if (dfa is not null)
        {
            Writer dfaSection = new();
            dfa.WriteTo(dfaSection);
            sections.Add((SectionDfa, true, dfaSection.ToArray()));
        }

        return sections;
    }

    /// <summary>
    /// What an image holds, written in one form whatever the layout it was read from: equal for two images exactly
    /// when they hold the same programs, automata and names. The data set's golden check compares a release's output
    /// with this engine's in this form.
    /// </summary>
    /// <param name="parts">What an image holds.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Canonical(in Parts parts)
    {
        Writer w = new();
        w.Bool(parts.Linear);
        w.Int(parts.GroupCount);
        w.Int(parts.GroupNames.Count);
        foreach (KeyValuePair<string, int[]> name in parts.GroupNames)
        {
            w.String(name.Key);
            w.Ints(name.Value);
        }

        WriteProgram(w, parts.Bounds, parts.Bounds.Automata());
        WriteProgram(w, parts.Captures, parts.Captures.Automata());
        w.Bool(parts.Dfa is not null);
        parts.Dfa?.WriteTo(w);
        return w.ToArray();
    }

    private static void Settings(Writer w, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        w.Int((int)flags);
        w.Int((int)edition);
        w.Int((int)UnicodeTables.Resolve(unicodeVersion));
    }

    private static void Body(Writer w, byte[] body)
    {
        w.Int(body.Length);
        w.ULong(Checksum(body));
        w.Raw(body);
    }

    private static void ReadNames(ref Reader r, out bool linear, out int groupCount, out List<KeyValuePair<string, int[]>> groupNames)
    {
        linear = r.Bool();
        groupCount = r.Int();
        int names = r.Count();
        groupNames = new(r.Ok ? names : 0);
        for (int k = 0; k < names && r.Ok; k++)
        {
            groupNames.Add(new KeyValuePair<string, int[]>(r.String(), r.Ints()));
        }
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

    private static Program? ReadProgram(ref Reader r, ref bool unknownOpcode)
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
            if (!r.Ok)
            {
                break;
            }

            if (!Instructions.IsKnown(op))
            {
                // An instruction of a later engine: the image is of a compiler this one cannot follow.
                unknownOpcode = true;
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

    /// <summary>Why an image was not read.</summary>
    internal enum Ignored
    {
        /// <summary>It was read.</summary>
        None = 0,

        /// <summary>Its layout is older or newer than any this engine reads.</summary>
        Format = 1,

        /// <summary>Its compiler is older than the oldest revision trusted, or (in layout 1) no release's.</summary>
        Revision = 2,

        /// <summary>It was written for other flags, another edition or another version of Unicode.</summary>
        Settings = 3,

        /// <summary>It holds a section a reader must know, which this engine does not.</summary>
        UnknownSection = 4,

        /// <summary>It lacks a section this engine needs.</summary>
        MissingSection = 5,

        /// <summary>It holds an instruction this engine does not know.</summary>
        UnknownOpcode = 6,

        /// <summary>It is damaged: a value out of range, a length or a checksum that does not agree.</summary>
        Damaged = 7,
    }

    /// <summary>What an image holds.</summary>
    /// <param name="bounds">The program that finds a match's bounds.</param>
    /// <param name="captures">The program that tracks every group.</param>
    /// <param name="dfa">The automaton over ASCII, or null when the pattern has none.</param>
    /// <param name="linear">Whether the pattern has no backreference.</param>
    /// <param name="groupCount">The number of groups.</param>
    /// <param name="groupNames">The names of the groups.</param>
    /// <param name="revision">The revision of the compiler that wrote the image.</param>
    internal readonly struct Parts(Program bounds, Program captures, Dfa? dfa, bool linear, int groupCount, List<KeyValuePair<string, int[]>> groupNames, int revision)
    {
        public int Revision { get; } = revision;

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

        /// <summary>Reads a length and that many bytes.</summary>
        /// <returns>The bytes, or empty when the reader failed.</returns>
        public ReadOnlySpan<byte> Payload()
        {
            int count = this.Int();
            if (this.failed || count < 0 || count > this.data.Length - this.at)
            {
                this.failed = true;
                return default;
            }

            ReadOnlySpan<byte> payload = this.data.Slice(this.at, count);
            this.at += count;
            return payload;
        }

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
