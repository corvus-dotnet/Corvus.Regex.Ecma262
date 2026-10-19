// <copyright file="TreeEmitter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions.SourceGenerator;

/// <summary>
/// Emits a program as structured code from its tree: each construct that can be retried (an alternation, a loop
/// over one character or class, an optional group) keeps its state in locals and owns a label, and whatever
/// follows it jumps to that label when it fails, so the common constructs push nothing on the matcher's stack.
/// The pattern as a whole is refused (and left to <see cref="RunEmitter"/>) when it holds a loop over a group
/// other than an optional one, a lookbehind, or more than the emitter sizes for.
/// </summary>
internal static class TreeEmitter
{
    private const string Ns = RunEmitter.Ns;
    private const int MostNodes = 1500;

    /// <summary>Emits the runner for a program as structured code, or reports that the program is left to the instruction emitter.</summary>
    /// <param name="sb">The output.</param>
    /// <param name="indent">The indentation.</param>
    /// <param name="name">The class name.</param>
    /// <param name="p">The program.</param>
    /// <returns>Whether the runner was emitted.</returns>
    public static bool TryEmitRunner(StringBuilder sb, int indent, string name, Program p)
    {
        if (p.Root is null || !Supported(p.Root, p, 0, out int nodes) || nodes > MostNodes)
        {
            return false;
        }

        Emitter e = new(p);
        StringBuilder code = new();
        e.EmitBody(code, indent + 2, scan: false);
        Emitter e2 = new(p);
        StringBuilder scanCode = new();
        StringBuilder findCode = new();
        StringBuilder statics = new();
        bool ownsSearch = Finder.TryEmit(p, findCode, statics, indent + 1);
        e2.EmitBody(scanCode, indent + 2, scan: true, ownsSearch: ownsSearch);

        RunEmitter.Line(sb, indent, "/// <summary>The program as structured code (see <see cref=\"global::Corvus.Ecma262.RegularExpressions.GeneratedRunner\"/>).</summary>");
        RunEmitter.Line(sb, indent, "[global::System.CodeDom.Compiler.GeneratedCode(\"Corvus.Regex.Ecma262.SourceGenerator\", \"1.0\")]");
        RunEmitter.Line(sb, indent, "private sealed class " + name + " : " + Ns + "GeneratedRunner");
        RunEmitter.Line(sb, indent, "{");
        for (int k = 0; k < e.Sets.Count; k++)
        {
            StringBuilder pairs = new();
            foreach (int v in e.Sets[k].Ranges)
            {
                if (pairs.Length > 0)
                {
                    pairs.Append(", ");
                }

                pairs.Append(v.ToString(CultureInfo.InvariantCulture));
            }

            RunEmitter.Line(sb, indent + 1, "private static readonly " + Ns + "CharSet S" + k + " = new " + Ns + "CharSet(new int[] { " + pairs + " });");
        }

        if (e.Sets.Count > 0)
        {
            sb.AppendLine();
        }

        if (statics.Length > 0)
        {
            RunEmitter.Line(sb, 0, "#if NET8_0_OR_GREATER");
            sb.Append(statics);
            RunEmitter.Line(sb, 0, "#endif");
            sb.AppendLine();
        }

        if (ownsSearch)
        {
            RunEmitter.Line(sb, 0, "#if NET8_0_OR_GREATER");
            RunEmitter.Line(sb, indent + 1, "/// <inheritdoc/>");
            RunEmitter.Line(sb, indent + 1, "public override bool OwnsSearch => true;");
            RunEmitter.Line(sb, 0, "#endif");
            sb.AppendLine();
            sb.Append(findCode);
            sb.AppendLine();
        }

        RunEmitter.Line(sb, indent + 1, "/// <inheritdoc/>");
        RunEmitter.Line(sb, indent + 1, "public override bool Run(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<char> input, int start, out int end) => Run<char, " + Ns + "Utf16Encoding>(p, st, input, start, out end);");
        sb.AppendLine();
        RunEmitter.Line(sb, indent + 1, "/// <inheritdoc/>");
        RunEmitter.Line(sb, indent + 1, "public override bool Run(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<byte> input, int start, out int end) => Run<byte, " + Ns + "Utf8Encoding>(p, st, input, start, out end);");
        sb.AppendLine();
        RunEmitter.Line(sb, indent + 1, "private static bool Run<TUnit, TEncoding>(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<TUnit> input, int start, out int end)");
        RunEmitter.Line(sb, indent + 2, "where TUnit : unmanaged, global::System.IEquatable<TUnit>");
        RunEmitter.Line(sb, indent + 2, "where TEncoding : struct, " + Ns + "ITextEncoding<TUnit>");
        RunEmitter.Line(sb, indent + 1, "{");
        sb.Append(code);
        RunEmitter.Line(sb, indent + 1, "}");
        sb.AppendLine();
        RunEmitter.Line(sb, indent + 1, "/// <inheritdoc/>");
        RunEmitter.Line(sb, indent + 1, "public override bool Scan(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<char> input, ref " + Ns + "CandidateScan scan, out int start, out int end) => Scan<char, " + Ns + "Utf16Encoding>(p, st, input, ref scan, out start, out end);");
        sb.AppendLine();
        RunEmitter.Line(sb, indent + 1, "/// <inheritdoc/>");
        RunEmitter.Line(sb, indent + 1, "public override bool Scan(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<byte> input, ref " + Ns + "CandidateScan scan, out int start, out int end) => Scan<byte, " + Ns + "Utf8Encoding>(p, st, input, ref scan, out start, out end);");
        sb.AppendLine();
        RunEmitter.Line(sb, indent + 1, "private static bool Scan<TUnit, TEncoding>(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<TUnit> input, ref " + Ns + "CandidateScan scan, out int start, out int end)");
        RunEmitter.Line(sb, indent + 2, "where TUnit : unmanaged, global::System.IEquatable<TUnit>");
        RunEmitter.Line(sb, indent + 2, "where TEncoding : struct, " + Ns + "ITextEncoding<TUnit>");
        RunEmitter.Line(sb, indent + 1, "{");
        sb.Append(scanCode);
        RunEmitter.Line(sb, indent + 1, "}");
        RunEmitter.Line(sb, indent, "}");
        return true;
    }

    /// <summary>Whether the emitter takes a tree: no loop over a group but an optional one, no lookbehind, no negative lookahead with a group that is kept.</summary>
    private static bool Supported(EcmaRegexNode n, Program p, int depth, out int nodes)
    {
        nodes = 1;
        if (depth > 200)
        {
            return false;
        }

        switch (n.Kind)
        {
            case EcmaRegexNodeKind.Repeat:
            {
                EcmaRegexNode body = n.Subs[0];
                bool single = body.Kind is EcmaRegexNodeKind.Char or EcmaRegexNodeKind.Set;
                if (!single && n.Max != 0 && !(n.Min == 0 && n.Max == 1))
                {
                    return false;
                }

                break;
            }

            case EcmaRegexNodeKind.Look:
                if (n.Behind)
                {
                    return false;
                }

                break;
        }

        foreach (EcmaRegexNode sub in n.Subs)
        {
            if (!Supported(sub, p, depth + 1, out int inner))
            {
                return false;
            }

            nodes += inner;
        }

        return true;
    }

    /// <summary>What can start whatever follows a node: a set of first characters, whether the end of the text is allowed, or unknown.</summary>
    private readonly struct Follow(CharSet? set, bool atEnd, bool unknown)
    {
        public static readonly Follow Unknown = new(null, false, true);

        public CharSet? Set { get; } = set;

        public bool AtEnd { get; } = atEnd;

        public bool IsUnknown { get; } = unknown;
    }

    private sealed class Emitter(Program p)
    {
        private readonly Program p = p;
        private readonly Dictionary<CharSet, int> setIndex = [];
        private readonly StringBuilder decls = new();
        private readonly Dictionary<int[], CharSet> classes = [];
        private StringBuilder code = new();
        private int indent;
        private int next;
        private string done = "Fail";

        public List<CharSet> Sets { get; } = [];

        public void EmitBody(StringBuilder output, int baseIndent, bool scan, bool ownsSearch = false)
        {
            this.code = new StringBuilder();
            this.indent = baseIndent;
            this.Line("end = -1;");
            if (scan)
            {
                this.Line("start = -1;");
            }

            this.Line("TEncoding enc = default;");
            this.Line("int[] caps = st.Caps;");
            this.Line("const bool units = " + (this.p.Units ? "true" : "false") + ";");
            this.Line("int len = input.Length;");
            this.Line("int pos = start;");
            this.Line("long steps = st.Steps;");
            this.Line("bool memo = st.MemoOn;");
            this.Line("ulong[] memoBits = st.Memo;");
            this.Line("int c;");
            this.Line("int r;");
            this.Line("int w;");
            this.Line("int at;");
            this.Line("int n;");
            this.Line("int j;");
            StringBuilder prologue = this.code;
            this.code = new StringBuilder();
            if (scan)
            {
                // The loop over the starts the search yields: the setting up above is done once for them all.
                if (ownsSearch)
                {
                    RunEmitter.Line(this.code, 0, "#if NET8_0_OR_GREATER");
                    this.Line("while (scan.Kind == " + Ns + "CandidateScan.Emitted ? Find<TUnit, TEncoding>(p, st, input, ref scan, out start) : " + Ns + "Matcher.NextStart<TUnit, TEncoding>(p, st, input, ref scan, out start))");
                    RunEmitter.Line(this.code, 0, "#else");
                    this.Line("while (" + Ns + "Matcher.NextStart<TUnit, TEncoding>(p, st, input, ref scan, out start))");
                    RunEmitter.Line(this.code, 0, "#endif");
                }
                else
                {
                    this.Line("while (" + Ns + "Matcher.NextStart<TUnit, TEncoding>(p, st, input, ref scan, out start))");
                }

                this.Line("{");
                this.indent++;
                this.Line("pos = start;");
            }

            this.Emit(this.p.Root!, Follow.Unknown);
            this.Line("st.Steps = steps;");
            this.Line("end = pos;");
            this.Line("return true;");
            sb_label(this.code, this.indent - 1, "Fail");
            if (scan)
            {
                this.Line("continue;");
                this.indent--;
                this.Line("}");
                sb_blank(this.code);
            }

            this.Line("st.Steps = steps;");
            this.Line("return false;");
            output.Append(prologue);
            output.Append(this.decls);
            output.AppendLine();
            output.Append(this.code);

            static void sb_blank(StringBuilder sb) => sb.AppendLine();

            static void sb_label(StringBuilder sb, int indent, string label)
            {
                RunEmitter.Line(sb, indent, label + ":");
            }
        }

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

        private static bool IsLiteralRune(int r) => r < 0x80;

        private void Line(string text) => RunEmitter.Line(this.code, this.indent, text);

        private void Label(string label) => RunEmitter.Line(this.code, this.indent - 1, label + ":");

        private string Local(string name, string type = "int")
        {
            RunEmitter.Line(this.decls, this.indent, type + " " + name + " = " + (type == "bool" ? "false" : "0") + ";");
            return name;
        }

        private int Id() => this.next++;

        private CharSet Class(EcmaRegexNode n)
        {
            if (!this.classes.TryGetValue(n.Set, out CharSet? set))
            {
                set = new CharSet(n.Set);
                this.classes[n.Set] = set;
            }

            return set;
        }

        private string SetName(CharSet set)
        {
            if (!this.setIndex.TryGetValue(set, out int k))
            {
                k = this.Sets.Count;
                this.Sets.Add(set);
                this.setIndex[set] = k;
            }

            return "S" + k;
        }

        /// <summary>The first characters of a node as pairs, and whether it can be empty; null when unknown (a backreference, a lookaround, an assertion on what comes before).</summary>
        private bool? First(EcmaRegexNode n, List<int> pairs)
        {
            switch (n.Kind)
            {
                case EcmaRegexNodeKind.Empty:
                    return true;
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
                        bool? empty = this.First(sub, pairs);
                        if (empty != true)
                        {
                            return empty;
                        }
                    }

                    return true;
                case EcmaRegexNodeKind.Alt:
                {
                    bool any = false;
                    foreach (EcmaRegexNode sub in n.Subs)
                    {
                        bool? empty = this.First(sub, pairs);
                        if (empty is null)
                        {
                            return null;
                        }

                        any |= empty.Value;
                    }

                    return any;
                }

                case EcmaRegexNodeKind.Group:
                    return this.First(n.Subs[0], pairs);
                case EcmaRegexNodeKind.Repeat:
                {
                    if (n.Max == 0)
                    {
                        return true;
                    }

                    bool? empty = this.First(n.Subs[0], pairs);
                    return empty is null ? null : (n.Min == 0 || empty.Value);
                }

                case EcmaRegexNodeKind.Eol:
                    return true;
                case EcmaRegexNodeKind.LineEnd:
                    pairs.AddRange([0x0A, 0x0A, 0x0D, 0x0D, 0x2028, 0x2029]);
                    return true;
                default:
                    return null;
            }
        }

        /// <summary>What follows a node that is followed by another node and then by a follow.</summary>
        private Follow Before(EcmaRegexNode after, Follow follow)
        {
            List<int> pairs = [];
            bool? empty = this.First(after, pairs);
            if (empty is null)
            {
                return Follow.Unknown;
            }

            if (!empty.Value)
            {
                return new Follow(new CharSet(EcmaRegexUnicode.SetOf([.. pairs], pairs.Count)), false, false);
            }

            if (follow.IsUnknown)
            {
                return Follow.Unknown;
            }

            if (follow.Set is not null)
            {
                pairs.AddRange(follow.Set.Ranges);
            }

            return new Follow(new CharSet(EcmaRegexUnicode.SetOf([.. pairs], pairs.Count)), follow.AtEnd, false);
        }

        private void Emit(EcmaRegexNode n, Follow follow)
        {
            switch (n.Kind)
            {
                case EcmaRegexNodeKind.Empty:
                    break;
                case EcmaRegexNodeKind.Char:
                    this.EmitChar(n.CodePoint, "pos", this.done, true);
                    break;
                case EcmaRegexNodeKind.Set:
                    this.EmitSet(this.Class(n), "pos", this.done, true);
                    break;
                case EcmaRegexNodeKind.Cat:
                    this.EmitCat(n, follow);
                    break;
                case EcmaRegexNodeKind.Alt:
                    this.EmitAlt(n, follow);
                    break;
                case EcmaRegexNodeKind.Group:
                    this.EmitGroup(n, follow);
                    break;
                case EcmaRegexNodeKind.Repeat:
                    this.EmitRepeat(n, follow);
                    break;
                case EcmaRegexNodeKind.Bol:
                    this.Line("if (pos != 0)");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    break;
                case EcmaRegexNodeKind.Eol:
                    this.Line("if (pos != len)");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    break;
                case EcmaRegexNodeKind.LineStart:
                    this.Line("if (pos != 0 && !enc.IsLineTerminatorBefore(input, pos))");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    break;
                case EcmaRegexNodeKind.LineEnd:
                    this.Line("if (pos != len && !enc.IsLineTerminatorAt(input, pos))");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    break;
                case EcmaRegexNodeKind.WordBoundary:
                case EcmaRegexNodeKind.NotWordBoundary:
                {
                    bool want = n.Kind == EcmaRegexNodeKind.WordBoundary;
                    if (n.Fold)
                    {
                        this.Line("if ((pos > 0 && " + Ns + "Matcher.IsFoldWordRune(enc.DecodeBefore(input, pos, out w))) == (pos < len && " + Ns + "Matcher.IsFoldWordRune(enc.Decode(input, pos, out w))) == " + (want ? "true" : "false") + ")");
                    }
                    else
                    {
                        this.Line("if ((pos > 0 && " + Ns + "Utf8Encoding.IsWordUnit(enc.Unit(input, pos - 1))) == (pos < len && " + Ns + "Utf8Encoding.IsWordUnit(enc.Unit(input, pos))) == " + (want ? "true" : "false") + ")");
                    }

                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    break;
                }

                case EcmaRegexNodeKind.Backref:
                {
                    // When several groups share the name, at most one has taken part, and the others match the
                    // empty string: matching each in turn matches the one that took part.
                    foreach (int g in n.Groups)
                    {
                        if (!this.p.TrackedGroups.TryGetValue(g, out int t))
                        {
                            continue;
                        }

                        this.Line("if (!" + Ns + "Matcher.BackrefAt<TUnit, TEncoding>(input, pos, caps[" + (2 * t) + "], caps[" + ((2 * t) + 1) + "], " + (n.Fold ? "true" : "false") + ", " + (this.p.Unicode ? "true" : "false") + ", out w))");
                        this.Line("{");
                        this.Line("    goto " + this.done + ";");
                        this.Line("}");
                        this.Line("pos += w;");
                    }

                    break;
                }

                case EcmaRegexNodeKind.Look:
                    this.EmitLook(n);
                    break;
                default:
                    throw new InvalidOperationException("Unknown node kind " + n.Kind);
            }
        }

        /// <summary>One character at a position variable: taken (the variable advanced) or a jump to a label.</summary>
        private void EmitChar(int cp, string at, string fail, bool bounded)
        {
            string bound = bounded ? at + " < len && " : string.Empty;
            if (cp < 0x80)
            {
                this.Line("if (" + bound + "enc.Unit(input, " + at + ") == " + cp + ")");
                this.Line("{");
                this.Line("    " + at + "++;");
                this.Line("}");
                this.Line("else");
                this.Line("{");
                this.Line("    goto " + fail + ";");
                this.Line("}");
            }
            else
            {
                this.Line("if (" + bound + "enc.Unit(input, " + at + ") >= 0x80 && (units ? enc.UnitAt(input, " + at + ", out w) : enc.Decode(input, " + at + ", out w)) == " + cp + ")");
                this.Line("{");
                this.Line("    " + at + " += w;");
                this.Line("}");
                this.Line("else");
                this.Line("{");
                this.Line("    goto " + fail + ";");
                this.Line("}");
            }
        }

        /// <summary>One member of a set at a position variable: taken or a jump to a label.</summary>
        /// <param name="set">The set.</param>
        /// <param name="at">The position variable.</param>
        /// <param name="fail">The label to jump to when the position holds no member.</param>
        /// <param name="bounded">Whether the position may be at the end of the text; when not, the caller has checked that as many units as the terms it emits together need are left.</param>
        /// <param name="recheck">When the caller has checked the units left, the units the terms after this one need: a member beyond ASCII may take more than one unit, so the check is made again after one.</param>
        private void EmitSet(CharSet set, string at, string fail, bool bounded, int recheck = 0)
        {
            if (set.IsEmpty)
            {
                this.Line("goto " + fail + ";");
                return;
            }

            this.Line(bounded ? "c = " + at + " < len ? enc.Unit(input, " + at + ") : -1;" : "c = enc.Unit(input, " + at + ");");
            if (!set.HasNonAscii)
            {
                this.Line("if ((uint)c < 0x80 && " + RunEmitter.AsciiTest(set, "c") + ")");
                this.Line("{");
                this.Line("    " + at + "++;");
                this.Line("}");
                this.Line("else");
                this.Line("{");
                this.Line("    goto " + fail + ";");
                this.Line("}");
                return;
            }

            this.Line("if (c < 0)");
            this.Line("{");
            this.Line("    goto " + fail + ";");
            this.Line("}");
            this.Line("else if (c < 0x80)");
            this.Line("{");
            this.Line("    if (!(" + RunEmitter.AsciiTest(set, "c") + "))");
            this.Line("    {");
            this.Line("        goto " + fail + ";");
            this.Line("    }");
            sb_blank(this.code);
            this.Line("    " + at + "++;");
            this.Line("}");
            this.Line("else");
            this.Line("{");
            this.Line("    r = units ? enc.UnitAt(input, " + at + ", out w) : enc.Decode(input, " + at + ", out w);");
            this.Line("    if (!" + this.SetName(set) + ".Contains(r))");
            this.Line("    {");
            this.Line("        goto " + fail + ";");
            this.Line("    }");
            sb_blank(this.code);
            this.Line("    " + at + " += w;");
            if (!bounded && recheck > 0)
            {
                this.Line("    if (len - " + at + " < " + recheck + ")");
                this.Line("    {");
                this.Line("        goto " + fail + ";");
                this.Line("    }");
            }

            this.Line("}");

            static void sb_blank(StringBuilder sb) => sb.AppendLine();
        }

        private void EmitCat(EcmaRegexNode n, Follow follow)
        {
            // What follows each term: computed from the right.
            Follow[] follows = new Follow[n.Subs.Length];
            Follow f = follow;
            for (int k = n.Subs.Length - 1; k >= 0; k--)
            {
                follows[k] = f;
                f = this.Before(n.Subs[k], f);
            }

            int i = 0;
            while (i < n.Subs.Length)
            {
                // A run of terms that take one unit each where they are ASCII (characters, and sets, whose members
                // beyond ASCII may take more and check again) is one bounds check for them all, then a comparison
                // each: as the runtime's generator checks a match's least length once.
                int j = i;
                while (j < n.Subs.Length && ((n.Subs[j].Kind == EcmaRegexNodeKind.Char && IsLiteralRune(n.Subs[j].CodePoint)) || (n.Subs[j].Kind == EcmaRegexNodeKind.Set && !this.Class(n.Subs[j]).IsEmpty)))
                {
                    j++;
                }

                if (j - i >= 2)
                {
                    this.Line("if (len - pos < " + (j - i) + ")");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    sb_blank(this.code);
                    int k = i;
                    while (k < j)
                    {
                        if (n.Subs[k].Kind == EcmaRegexNodeKind.Set)
                        {
                            this.EmitSet(this.Class(n.Subs[k]), "pos", this.done, false, j - k - 1);
                            k++;
                            continue;
                        }

                        int k2 = k;
                        StringBuilder test = new();
                        while (k2 < j && n.Subs[k2].Kind == EcmaRegexNodeKind.Char)
                        {
                            test.Append(k2 == k ? string.Empty : " || ").Append("enc.Unit(input, pos").Append(k2 == k ? string.Empty : " + " + (k2 - k)).Append(") != ").Append(n.Subs[k2].CodePoint);
                            k2++;
                        }

                        this.Line("if (" + test + ")");
                        this.Line("{");
                        this.Line("    goto " + this.done + ";");
                        this.Line("}");
                        sb_blank(this.code);
                        this.Line("pos += " + (k2 - k) + ";");
                        k = k2;
                    }

                    i = j;
                    continue;
                }

                this.Emit(n.Subs[i], follows[i]);
                i++;
            }

            static void sb_blank(StringBuilder sb) => sb.AppendLine();
        }

        private void EmitAlt(EcmaRegexNode n, Follow follow)
        {
            int k = this.Id();
            string outer = this.done;
            string startPos = this.Local("alt" + k + "Pos");
            string branch = this.Local("alt" + k + "Branch");
            int m = this.p.PointOf.TryGetValue(n, out int point) ? point : 0;
            this.Line(startPos + " = pos;");
            if (m != 0)
            {
                this.Line("if (memo && " + Ns + "Matcher.MemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos)))");
                this.Line("{");
                this.Line("    goto " + outer + ";");
                this.Line("}");
            }

            string[] backs = new string[n.Subs.Length];
            for (int b = 0; b < n.Subs.Length; b++)
            {
                if (b > 0)
                {
                    this.Label("AltNext" + k + "_" + b);
                    this.EmitSteps();
                    this.Line("pos = " + startPos + ";");
                }

                this.done = b + 1 < n.Subs.Length ? "AltNext" + k + "_" + (b + 1) : "AltOut" + k;
                this.Emit(n.Subs[b], follow);
                backs[b] = this.done;
                this.Line(branch + " = " + b + ";");
                this.Line("goto AltMatch" + k + ";");
            }

            this.Label("AltOut" + k);
            if (m != 0)
            {
                this.Line("if (memo)");
                this.Line("{");
                this.Line("    " + Ns + "Matcher.SetMemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", " + startPos + "));");
                this.Line("}");
            }

            this.Line("goto " + outer + ";");
            this.Label("AltBack" + k);
            this.Line("switch (" + branch + ")");
            this.Line("{");
            for (int b = 0; b < n.Subs.Length; b++)
            {
                this.Line("    case " + b + ": goto " + backs[b] + ";");
            }

            this.Line("    default: goto " + outer + ";");
            this.Line("}");
            this.Label("AltMatch" + k);
            this.done = "AltBack" + k;
        }

        private void EmitGroup(EcmaRegexNode n, Follow follow)
        {
            if (n.Group == 0 || !this.p.TrackedGroups.TryGetValue(n.Group, out int t))
            {
                this.Emit(n.Subs[0], follow);
                return;
            }

            int k = this.Id();
            string outer = this.done;
            string startPos = this.Local("grp" + k + "Pos");
            string old0 = this.Local("grp" + k + "Old0");
            string old1 = this.Local("grp" + k + "Old1");
            this.Line(startPos + " = pos;");
            this.Line(old0 + " = caps[" + (2 * t) + "];");
            this.Line(old1 + " = caps[" + ((2 * t) + 1) + "];");
            this.done = "GrpBack" + k;
            this.Emit(n.Subs[0], follow);
            this.Line("caps[" + (2 * t) + "] = " + startPos + ";");
            this.Line("caps[" + ((2 * t) + 1) + "] = pos;");
            this.Line("goto GrpMatch" + k + ";");
            this.Label("GrpBack" + k);
            this.Line("caps[" + (2 * t) + "] = " + old0 + ";");
            this.Line("caps[" + ((2 * t) + 1) + "] = " + old1 + ";");
            this.Line("goto " + outer + ";");
            this.Label("GrpMatch" + k);
        }

        private void EmitLook(EcmaRegexNode n)
        {
            int k = this.Id();
            string outer = this.done;
            string savePos = this.Local("look" + k + "Pos");
            List<int> slots = [];
            this.CollectSlots(n.Subs[0], slots);
            string[] olds = new string[2 * slots.Count];
            for (int s = 0; s < slots.Count; s++)
            {
                olds[2 * s] = this.Local("look" + k + "Old" + (2 * s));
                olds[(2 * s) + 1] = this.Local("look" + k + "Old" + ((2 * s) + 1));
                this.Line(olds[2 * s] + " = caps[" + (2 * slots[s]) + "];");
                this.Line(olds[(2 * s) + 1] + " = caps[" + ((2 * slots[s]) + 1) + "];");
            }

            this.Line(savePos + " = pos;");
            this.done = "LookFail" + k;
            this.Emit(n.Subs[0], Follow.Unknown);

            // The body matched. A lookaround is atomic: what follows never retries the body.
            this.Line("pos = " + savePos + ";");
            if (n.Negative)
            {
                this.RestoreSlots(slots, olds);
                this.Line("goto " + outer + ";");
                this.Label("LookFail" + k);
                this.Line("pos = " + savePos + ";");
                this.done = outer;
            }
            else
            {
                this.Line("goto LookMatch" + k + ";");
                this.Label("LookFail" + k);
                this.Line("pos = " + savePos + ";");
                this.Line("goto " + outer + ";");
                if (slots.Count > 0)
                {
                    // Backtracking past the lookahead lets go of the groups it set.
                    this.Label("LookBack" + k);
                    this.RestoreSlots(slots, olds);
                    this.Line("goto " + outer + ";");
                    this.done = "LookBack" + k;
                }
                else
                {
                    this.done = outer;
                }

                this.Label("LookMatch" + k);
            }
        }

        private void CollectSlots(EcmaRegexNode n, List<int> slots)
        {
            if (n.Kind == EcmaRegexNodeKind.Group && n.Group != 0 && this.p.TrackedGroups.TryGetValue(n.Group, out int t))
            {
                slots.Add(t);
            }

            foreach (EcmaRegexNode sub in n.Subs)
            {
                this.CollectSlots(sub, slots);
            }
        }

        private void RestoreSlots(List<int> slots, string[] olds)
        {
            for (int s = 0; s < slots.Count; s++)
            {
                this.Line("caps[" + (2 * slots[s]) + "] = " + olds[2 * s] + ";");
                this.Line("caps[" + ((2 * slots[s]) + 1) + "] = " + olds[(2 * s) + 1] + ";");
            }
        }

        private void EmitRepeat(EcmaRegexNode n, Follow follow)
        {
            EcmaRegexNode body = n.Subs[0];
            if (n.Max == 0)
            {
                return;
            }

            if (body.Kind is EcmaRegexNodeKind.Char or EcmaRegexNodeKind.Set)
            {
                if (n.Lazy)
                {
                    this.EmitLazyRun(n, follow);
                }
                else
                {
                    this.EmitGreedyRun(n, follow);
                }

                return;
            }

            // An optional group: the body or nothing, in the order the flag gives. Past its least count of zero,
            // ECMA-262 refuses an iteration that consumed nothing.
            this.EmitOptional(n, follow);
        }

        private void EmitOptional(EcmaRegexNode n, Follow follow)
        {
            EcmaRegexNode body = n.Subs[0];
            int k = this.Id();
            string outer = this.done;
            string startPos = this.Local("opt" + k + "Pos");
            string taken = this.Local("opt" + k + "Taken", "bool");
            int m = this.p.PointOf.TryGetValue(n, out int point) ? point : 0;
            bool nullable = Nullable(body);
            this.Line(startPos + " = pos;");
            if (m != 0)
            {
                this.Line("if (memo && " + Ns + "Matcher.MemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos)))");
                this.Line("{");
                this.Line("    goto " + outer + ";");
                this.Line("}");
            }

            string bodyBack;
            if (!n.Lazy)
            {
                // The body first; when it is exhausted, nothing.
                this.done = "OptSkip" + k;
                this.Emit(body, follow);
                if (nullable)
                {
                    this.Line("if (pos == " + startPos + ")");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                }

                bodyBack = this.done;
                this.Line(taken + " = true;");
                this.Line("goto OptMatch" + k + ";");
                this.Label("OptSkip" + k);
                this.EmitSteps();
                this.Line("pos = " + startPos + ";");
                this.Line(taken + " = false;");
                this.Line("goto OptMatch" + k + ";");
                this.Label("OptBack" + k);
                this.Line("if (" + taken + ")");
                this.Line("{");
                this.Line("    goto " + bodyBack + ";");
                this.Line("}");
                sb_blank(this.code);
                this.Label("OptOut" + k);
                if (m != 0)
                {
                    this.Line("if (memo)");
                    this.Line("{");
                    this.Line("    " + Ns + "Matcher.SetMemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", " + startPos + "));");
                    this.Line("}");
                }

                this.Line("goto " + outer + ";");
            }
            else
            {
                // Nothing first; when what follows fails, the body.
                this.Line(taken + " = false;");
                this.Line("goto OptMatch" + k + ";");
                this.Label("OptBack" + k);
                this.Line("if (" + taken + ")");
                this.Line("{");
                this.Line("    goto OptBody" + k + ";");
                this.Line("}");
                sb_blank(this.code);
                this.EmitSteps();
                this.Line("pos = " + startPos + ";");
                this.Line(taken + " = true;");
                this.done = "OptOut" + k;
                this.Emit(body, follow);
                if (nullable)
                {
                    this.Line("if (pos == " + startPos + ")");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                }

                bodyBack = this.done;
                this.Line("goto OptMatch" + k + ";");
                this.Label("OptBody" + k);
                this.Line("goto " + bodyBack + ";");
                this.Label("OptOut" + k);
                if (m != 0)
                {
                    this.Line("if (memo)");
                    this.Line("{");
                    this.Line("    " + Ns + "Matcher.SetMemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", " + startPos + "));");
                    this.Line("}");
                }

                this.Line("goto " + outer + ";");
            }

            this.Label("OptMatch" + k);
            this.done = "OptBack" + k;

            static void sb_blank(StringBuilder sb) => sb.AppendLine();
        }

        /// <summary>A greedy loop over one character or class: the run taken whole, then given back a character at a time (by vector where what follows allows only ASCII) when what follows fails.</summary>
        private void EmitGreedyRun(EcmaRegexNode n, Follow follow)
        {
            EcmaRegexNode body = n.Subs[0];
            CharSet? set = body.Kind == EcmaRegexNodeKind.Set ? this.Class(body) : null;
            if (set is not null && set.IsEmpty)
            {
                if (n.Min > 0)
                {
                    this.Line("goto " + this.done + ";");
                }

                return;
            }

            int k = this.Id();
            string outer = this.done;
            int min = n.Min;
            int max = n.Max;
            int m = this.p.PointOf.TryGetValue(n, out int point) ? point : 0;
            string floor = this.Local("rep" + k + "Floor");
            string endPos = this.Local("rep" + k + "End");
            this.Line("at = pos;");
            this.Line("n = 0;");
            this.Line(floor + " = pos;");
            if (max < 0 && min <= 1 && (set is not null || body.CodePoint < 0x80))
            {
                if (min == 1)
                {
                    if (set is not null)
                    {
                        this.EmitSet(set, "at", outer, true);
                    }
                    else
                    {
                        this.EmitChar(body.CodePoint, "at", outer, true);
                    }

                    this.Line(floor + " = at;");
                }

                // A run of one character is not worth a vector call: the next unit is looked at first.
                string more = set is not null
                    ? (set.HasNonAscii ? "(c < 0x80 ? " + RunEmitter.AsciiTest(set, "c") + " : true)" : "(c < 0x80 && " + RunEmitter.AsciiTest(set, "c") + ")")
                    : "c == " + body.CodePoint;
                this.Line("if (at < len)");
                this.Line("{");
                this.Line("    c = enc.Unit(input, at);");
                this.Line("    if (" + more + ")");
                this.Line("    {");
                string vector = set is not null
                    ? (set.IsLineRest
                        ? "at = " + Ns + "Matcher.EndOfLine<TUnit, TEncoding>(st, input, at);"
                        : "at = " + Ns + "Matcher.EndOfRun<TUnit, TEncoding>(" + this.SetName(set) + ", input, at, units);")
                    : "at = " + Ns + "Matcher.EndOfRun<TUnit, TEncoding>(" + body.CodePoint + ", input, at);";
                if (set is null || (!set.HasNonAscii && !set.IsLineRest))
                {
                    // Most runs are a word long: the next units are walked in code, and the vector search is
                    // called only for a run that goes on past them (as the runtime does for a bounded run).
                    string member = set is not null ? "(c = enc.Unit(input, at)) < 0x80 && " + RunEmitter.AsciiTest(set, "c") : "enc.Unit(input, at) == " + body.CodePoint;
                    this.Line("        at++;");
                    this.Line("        j = len - at < 8 ? len : at + 8;");
                    this.Line("        while (at < j && " + member + ")");
                    this.Line("        {");
                    this.Line("            at++;");
                    this.Line("        }");
                    sb_blank(this.code);
                    this.Line("        if (at == j)");
                    this.Line("        {");
                    this.Line("            " + vector);
                    this.Line("        }");
                }
                else
                {
                    this.Line("        " + vector);
                }

                this.Line("    }");
                this.Line("}");
                sb_blank(this.code);
                this.Line("n = " + min + ";");
            }
            else
            {
                if (set is not null && max >= 12)
                {
                    this.Line("at = " + Ns + "Matcher.AsciiRun<TUnit, TEncoding>(" + this.SetName(set) + ", input, at, " + max + ", units);");
                    this.Line("n = at - pos;");
                    this.Line("if (n >= " + min + ")");
                    this.Line("{");
                    this.Line("    " + floor + " = pos + " + min + ";");
                    this.Line("}");
                    sb_blank(this.code);
                }

                this.Line("while (" + (max < 0 ? "true" : "n != " + max) + ")");
                this.Line("{");
                this.indent++;
                if (set is not null)
                {
                    this.EmitSet(set, "at", "RepEnd" + k, true);
                }
                else
                {
                    this.EmitChar(body.CodePoint, "at", "RepEnd" + k, true);
                }

                sb_blank(this.code);
                this.Line("n++;");
                this.Line("if (n == " + min + ")");
                this.Line("{");
                this.Line("    " + floor + " = at;");
                this.Line("}");
                this.indent--;
                this.Line("}");
                sb_blank(this.code);
                this.Label("RepEnd" + k);
            }

            this.Line("steps -= (at - pos) >> 3;");
            this.EmitSteps();
            if (min > 0)
            {
                this.Line("if (n < " + min + ")");
                this.Line("{");
                this.Line("    goto " + outer + ";");
                this.Line("}");
                sb_blank(this.code);
            }

            this.Line("pos = at;");
            bool atomic = set is not null
                ? (!follow.IsUnknown && follow.Set is not null && !follow.Set.Intersects(set))
                : (!follow.IsUnknown && (follow.Set is null || !follow.Set.Contains(body.CodePoint)));
            if (atomic)
            {
                // Nothing that follows can take a character of the run, so no give-back can succeed.
                return;
            }

            bool filter = !follow.IsUnknown && follow.Set is not null && !follow.Set.HasNonAscii;
            string fset = filter ? this.SetName(follow.Set!) : string.Empty;
            if (m != 0)
            {
                // What follows has failed from the end of the run before: the run gives back at once.
                this.Line("if (memo && " + Ns + "Matcher.MemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos)))");
                this.Line("{");
                this.Line("    goto RepGive" + k + ";");
                this.Line("}");
                sb_blank(this.code);
            }

            this.Line("goto RepMatch" + k + ";");
            this.Label("RepBack" + k);

            // What follows failed, from the end of the run as it stood (what follows may have moved on from there).
            this.Line("pos = " + endPos + ";");
            this.EmitSteps();
            if (m != 0)
            {
                this.Line("if (memo)");
                this.Line("{");
                this.Line("    " + Ns + "Matcher.SetMemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos));");
                this.Line("}");
                sb_blank(this.code);
            }

            this.Label("RepGive" + k);
            this.Line("if (pos <= " + floor + ")");
            this.Line("{");
            this.Line("    goto " + outer + ";");
            this.Line("}");
            sb_blank(this.code);
            int followOne = -1;
            if (filter && follow.Set!.Count == 1)
            {
                for (int ch = 0; ch < 128; ch++)
                {
                    if (follow.Set.HasAscii(ch))
                    {
                        followOne = ch;
                    }
                }
            }

            if (followOne >= 0)
            {
                // What follows can go on only from one ASCII character: the last one in the run is found by the
                // span search at any length (a failed candidate for an email address gives back a whole word).
                this.Line("j = typeof(TUnit) == typeof(char) ? global::System.MemoryExtensions.LastIndexOf(global::System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(input).Slice(" + floor + ", pos - " + floor + "), '\\u" + followOne.ToString("X4", CultureInfo.InvariantCulture) + "') : global::System.MemoryExtensions.LastIndexOf(global::System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(input).Slice(" + floor + ", pos - " + floor + "), (byte)" + followOne + ");");
                this.Line("if (j < 0)");
                this.Line("{");
                this.Line("    goto " + outer + ";");
                this.Line("}");
                sb_blank(this.code);
                this.Line("pos = " + floor + " + j;");
            }
            else if (filter)
            {
                // What follows can go on only from a member of a set of ASCII characters: the last such position
                // in the run is found by vector when the run is long enough to pay for it.
                this.Line("if (pos - " + floor + " >= 16)");
                this.Line("{");
                this.Line("    j = " + Ns + "Matcher.LastAllowedIn<TUnit, TEncoding>(input, " + floor + ", pos, " + fset + ");");
                this.Line("    if (j < 0)");
                this.Line("    {");
                this.Line("        goto " + outer + ";");
                this.Line("    }");
                sb_blank(this.code);
                this.Line("    pos = j;");
                this.Line("}");
                this.Line("else");
                this.Line("{");
                this.Line("    pos -= " + Ns + "Matcher.GiveBack<TUnit, TEncoding>(input, pos, units);");
                this.Line("    c = enc.Unit(input, pos);");
                this.Line("    if (c >= 0x80 || !(" + RunEmitter.AsciiTest(follow.Set!, "c") + "))");
                this.Line("    {");
                this.Line("        goto RepGive" + k + ";");
                this.Line("    }");
                this.Line("}");
            }
            else
            {
                this.Line("pos -= " + Ns + "Matcher.GiveBack<TUnit, TEncoding>(input, pos, units);");
            }

            if (m != 0)
            {
                sb_blank(this.code);
                this.Line("if (memo && " + Ns + "Matcher.MemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos)))");
                this.Line("{");
                this.Line("    goto RepGive" + k + ";");
                this.Line("}");
            }

            this.Label("RepMatch" + k);
            this.Line(endPos + " = pos;");
            this.done = "RepBack" + k;

            static void sb_blank(StringBuilder sb) => sb.AppendLine();
        }

        /// <summary>A lazy loop over one character or class: the least count taken, then one more each time what follows fails.</summary>
        private void EmitLazyRun(EcmaRegexNode n, Follow follow)
        {
            EcmaRegexNode body = n.Subs[0];
            CharSet? set = body.Kind == EcmaRegexNodeKind.Set ? this.Class(body) : null;
            if (set is not null && set.IsEmpty)
            {
                if (n.Min > 0)
                {
                    this.Line("goto " + this.done + ";");
                }

                return;
            }

            int k = this.Id();
            string outer = this.done;
            int min = n.Min;
            int max = n.Max;
            int m = this.p.PointOf.TryGetValue(n, out int point) ? point : 0;
            string count = this.Local("lazy" + k + "Count");
            string endPos = this.Local("lazy" + k + "End");
            this.Line("at = pos;");
            if (min > 0)
            {
                this.Line("n = 0;");
                this.Line("while (n < " + min + ")");
                this.Line("{");
                this.indent++;
                if (set is not null)
                {
                    this.EmitSet(set, "at", outer, true);
                }
                else
                {
                    this.EmitChar(body.CodePoint, "at", outer, true);
                }

                sb_blank(this.code);
                this.Line("n++;");
                this.indent--;
                this.Line("}");
                sb_blank(this.code);
                this.Line("steps -= n >> 3;");
                this.EmitSteps();
            }

            this.Line("pos = at;");
            this.Line(count + " = " + min + ";");
            bool filter = !follow.IsUnknown && follow.Set is not null && !follow.Set.HasNonAscii;
            if (m != 0)
            {
                this.Line("if (memo && " + Ns + "Matcher.MemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos)))");
                this.Line("{");
                this.Line("    goto LazyTake" + k + ";");
                this.Line("}");
                sb_blank(this.code);
            }

            this.Line("goto LazyMatch" + k + ";");
            this.Label("LazyBack" + k);
            this.Line("pos = " + endPos + ";");
            this.EmitSteps();
            if (m != 0)
            {
                this.Line("if (memo)");
                this.Line("{");
                this.Line("    " + Ns + "Matcher.SetMemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos));");
                this.Line("}");
                sb_blank(this.code);
            }

            this.Label("LazyTake" + k);
            if (max >= 0)
            {
                this.Line("if (" + count + " >= " + max + ")");
                this.Line("{");
                this.Line("    goto " + outer + ";");
                this.Line("}");
                sb_blank(this.code);
            }

            if (set is not null)
            {
                this.EmitSet(set, "pos", outer, true);
            }
            else
            {
                this.EmitChar(body.CodePoint, "pos", outer, true);
            }

            this.Line(count + "++;");
            if (filter)
            {
                this.Line("if (pos < len)");
                this.Line("{");
                this.Line("    c = enc.Unit(input, pos);");
                this.Line("    if (c >= 0x80 || !(" + RunEmitter.AsciiTest(follow.Set!, "c") + "))");
                this.Line("    {");
                this.Line("        goto LazyTake" + k + ";");
                this.Line("    }");
                this.Line("}");
                if (!follow.AtEnd)
                {
                    this.Line("else");
                    this.Line("{");
                    this.Line("    goto LazyTake" + k + ";");
                    this.Line("}");
                }
            }

            if (m != 0)
            {
                this.Line("if (memo && " + Ns + "Matcher.MemoBit(memoBits, " + Ns + "Matcher.MemoIndex(p, st, " + m + ", pos)))");
                this.Line("{");
                this.Line("    goto LazyTake" + k + ";");
                this.Line("}");
            }

            this.Label("LazyMatch" + k);
            this.Line(endPos + " = pos;");
            this.done = "LazyBack" + k;

            static void sb_blank(StringBuilder sb) => sb.AppendLine();
        }

        private void EmitSteps()
        {
            this.Line("if (--steps < 0)");
            this.Line("{");
            this.Line("    if (!st.Check(0))");
            this.Line("    {");
            this.Line("        st.Steps = 0;");
            this.Line("        return false;");
            this.Line("    }");
            sb_blank(this.code);
            this.Line("    steps = " + Ns + "Matcher.CheckEvery;");
            this.Line("    memo = st.MemoOn;");
            this.Line("    memoBits = st.Memo;");
            this.Line("}");

            static void sb_blank(StringBuilder sb) => sb.AppendLine();
        }
    }

    /// <summary>
    /// The search for starts as code, the way .NET's generator emits it: direct span searches for the characters
    /// that begin the alternatives, with checks of a position or two before a start is tried; a literal at a
    /// fixed distance from the start; the leading literal; a first set as a range or a few values; and
    /// <c>SearchValues</c> held in static fields where the runtime's search is the one to use, so that the JIT
    /// knows their type and inlines them.
    /// </summary>
    private static class Finder
    {
        public static bool TryEmit(Program p, StringBuilder find, StringBuilder statics, int indent)
        {

            if (p.Anchored || p.Root is null)
            {
                return false;
            }

            AlternationSearch? alternation = p.Shape == SimpleShape.LiteralAlternation ? p.Alternation : (p.RequiredAlternation is not null && p.RequiredAlternationHi == 0 ? p.RequiredAlternation : null);
            if (alternation is not null && alternation.Literals.All(IsAscii) && alternation.Literals.Length <= 8)
            {
                EmitAlternation(p, alternation, p.Shape == SimpleShape.LiteralAlternation ? "p.Alternation!" : "p.RequiredAlternation!", find, statics, indent);
                return true;
            }

            if (p.Shape == SimpleShape.None && p.RequiredCount >= 0 && p.RequiredOffset >= 0 && p.RequiredUtf16.Length > 0 && IsAscii(p.RequiredUtf16) && p.RequiredAlternation is null)
            {
                EmitFixed(p, find, statics, indent);
                return true;
            }

            if (p.Shape == SimpleShape.None && p.Utf16Prefix.Length > 0 && IsAscii(p.Utf16Prefix) && p.CaseNeedleUtf16 is null)
            {
                EmitPrefix(p, find, statics, indent);
                return true;
            }

            if ((p.Shape == SimpleShape.CaseLiteral || p.Shape == SimpleShape.Sequence || p.Shape == SimpleShape.None) && p.CaseNeedleUtf16 is not null && p.CaseForms.Length >= 2 && p.CaseForms[0].All(x => x < 0x80))
            {
                EmitCaseLiteral(p, find, statics, indent);
                return true;
            }

            if (p.Shape == SimpleShape.None && p.AfterLoopUtf16.Length > 0 && IsAscii(p.AfterLoopUtf16) && p.AfterLoopMax < 0 && p.AfterLoopAlternation is null && p.AfterLoopSets.Length > 0 && p.AfterLoopSets.All(set => !set.HasNonAscii && !set.IsEmpty) && p.CaseNeedleUtf16 is null && p.RequiredAlternation is null)
            {
                EmitAfterLoop(p, find, statics, indent);
                return true;
            }

            if (p.Shape == SimpleShape.None && p.First is not null && !p.First.HasNonAscii && !p.First.IsEmpty && !p.SkipRun && !p.LeadingWordBoundary && p.Utf16Prefix.Length == 0 && p.RequiredCount < 0 && p.RequiredCountMax < 0 && p.AfterLoopUtf16.Length == 0 && p.AfterLoopAlternation is null && p.AfterLoopMax < 0 && p.CaseNeedleUtf16 is null && p.RequiredAlternation is null)
            {
                EmitFirst(p, find, statics, indent);
                return true;
            }

            return false;
        }

        /// <summary>The count of leading characters of a literal in its forms whose forms are all ASCII: under the u flag s folds to the long s and k to the Kelvin sign, which the search ignoring case does not know, so the search is for the part before the first such character and the match checks the rest (as .NET's generator searches for "sherloc").</summary>
        private static int AsciiFormsPrefix(int[][] forms)
        {
            int n = 0;
            while (n < forms.Length && forms[n].All(x => x < 0x80))
            {
                n++;
            }

            return n;
        }

        private static bool IsAscii(string s)
        {
            foreach (char c in s)
            {
                if (c >= 0x80)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A call of a span extension by its static name: the generated file has no using directive for System.</summary>
        private static string Call(string method, string span, string args) => "global::System.MemoryExtensions." + method + "(" + span + (args.Length > 0 ? ", " + args : string.Empty) + ")";

        private const string Chars = "global::System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, char>(input)";
        private const string Bytes8 = "global::System.Runtime.InteropServices.MemoryMarshal.Cast<TUnit, byte>(input)";

        private static string Ch(int c) => "'\\u" + c.ToString("X4", CultureInfo.InvariantCulture) + "'";

        private static string Str(string s)
        {
            StringBuilder sb = new("\"");
            foreach (char c in s)
            {
                sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }

            return sb.Append('"').ToString();
        }

        private static string Bytes(string ascii)
        {
            StringBuilder sb = new("new byte[] { ");
            for (int k = 0; k < ascii.Length; k++)
            {
                if (k > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(((int)ascii[k]).ToString(CultureInfo.InvariantCulture));
            }

            return sb.Append(" }").ToString();
        }

        private static void Head(StringBuilder find, int indent)
        {
            RunEmitter.Line(find, 0, "#if NET8_0_OR_GREATER");
            RunEmitter.Line(find, indent, "/// <summary>The search for starts, as code.</summary>");
            RunEmitter.Line(find, indent, "private static bool Find<TUnit, TEncoding>(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<TUnit> input, ref " + Ns + "CandidateScan scan, out int start)");
            RunEmitter.Line(find, indent + 1, "where TUnit : unmanaged, global::System.IEquatable<TUnit>");
            RunEmitter.Line(find, indent + 1, "where TEncoding : struct, " + Ns + "ITextEncoding<TUnit>");
            RunEmitter.Line(find, indent, "{");
            RunEmitter.Line(find, indent + 1, "start = -1;");
            RunEmitter.Line(find, indent + 1, "int len = input.Length;");
            RunEmitter.Line(find, indent + 1, "int pos = scan.Next;");
        }

        private static void Tail(StringBuilder find, int indent)
        {
            RunEmitter.Line(find, indent, "}");
            RunEmitter.Line(find, 0, "#endif");
        }

        /// <summary>An alternation of ASCII literals: exact, the characters that begin them searched for and a position or two checked; in their forms, the runtime's search ignoring case, with the forms it does not know found by the cursor's pass.</summary>
        private static void EmitAlternation(Program p, AlternationSearch alternation, string field, StringBuilder find, StringBuilder statics, int indent)
        {
            string[] literals = alternation.Literals;
            int minLen = literals.Min(l => l.Length);
            // Forms only where some character has more than one: a leading-literal alternation carries a forms
            // table whether or not the pattern has the i flag.
            bool forms = alternation.Forms is not null && alternation.Forms.Any(a => a.Any(f => f.Length > 1));
            Head(find, indent);
            int i1 = indent + 1;
            if (forms)
            {
                int[] prefixes = alternation.Forms!.Select(AsciiFormsPrefix).ToArray();
                bool parts = prefixes.All(n => n >= 2);
                RunEmitter.Line(find, i1, "int i;");
                if (parts)
                {
                    // Over UTF-16 the runtime's search ignoring case for each literal's leading part whose forms
                    // are all ASCII (under the u flag s folds to the long s and k to the Kelvin sign, which that
                    // search does not know): the match checks the rest, as .NET's generator searches for
                    // "sherloc", "holmes" and "watson". Over UTF-8 the runtime's search, and its pass for the
                    // forms apart.
                    string[] heads = literals.Select((l, k) => l.Substring(0, prefixes[k])).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    int minHead = heads.Min(h => h.Length);
                    string name = "F" + Math.Abs(string.Join("|", heads).GetHashCode()).ToString(CultureInfo.InvariantCulture);
                    RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<string> " + name + " = global::System.Buffers.SearchValues.Create(new string[] { " + string.Join(", ", heads.Select(Str)) + " }, global::System.StringComparison.OrdinalIgnoreCase);");
                    RunEmitter.Line(find, i1, "if (typeof(TUnit) == typeof(char))");
                    RunEmitter.Line(find, i1, "{");
                    RunEmitter.Line(find, i1 + 1, "i = pos <= len - " + minHead + " ? " + Call("IndexOfAny", Chars + ".Slice(pos)", name) + " : -1;");
                    RunEmitter.Line(find, i1 + 1, "i = i < 0 ? -1 : pos + i;");
                    RunEmitter.Line(find, i1, "}");
                    RunEmitter.Line(find, i1, "else");
                    RunEmitter.Line(find, i1, "{");
                    RunEmitter.Line(find, i1 + 1, "TEncoding enc = default;");
                    RunEmitter.Line(find, i1 + 1, "i = enc.IndexOfAlternation(input, pos, " + field + ");");
                    RunEmitter.Line(find, i1 + 1, p.Unicode
                        ? "i = " + Ns + "Matcher.ExtraStart<TUnit, TEncoding>(st, input, pos, i, " + field + ");"
                        : "_ = st;");
                    RunEmitter.Line(find, i1, "}");
                    sb_blank(find);
                    RunEmitter.Line(find, i1, "start = i;");
                }
                else
                {
                    // The runtime's search: it holds the search for any of several strings ignoring case where
                    // that pays and the packed fingerprint search where it does not, and its pass for the forms
                    // apart (the long s, the Kelvin sign) knows which of the two it chose.
                    RunEmitter.Line(find, i1, "TEncoding enc = default;");
                    RunEmitter.Line(find, i1, "i = enc.IndexOfAlternation(input, pos, " + field + ");");
                    RunEmitter.Line(find, i1, p.Unicode
                        ? "start = " + Ns + "Matcher.ExtraStart<TUnit, TEncoding>(st, input, pos, i, " + field + ");"
                        : "start = i;");
                }

                RunEmitter.Line(find, i1, "if (start < 0 || len - start < p.Need)");
                RunEmitter.Line(find, i1, "{");
                RunEmitter.Line(find, i1 + 1, "start = -1;");
                RunEmitter.Line(find, i1 + 1, "return false;");
                RunEmitter.Line(find, i1, "}");
                sb_blank(find);
                RunEmitter.Line(find, i1, "scan.Next = start + 1;");
                RunEmitter.Line(find, i1, "return true;");
                Tail(find, indent);
                return;
            }

            if (alternation.MultiUtf16.Two)
            {
                // No position is rare enough to search for alone: the needle compares two at every vector, which
                // beats a search for one and a check of the other (Tom|river over sixteen million characters:
                // 3.0 ms against 7.2). The needle is called, and gives the alternative's length.
                RunEmitter.Line(find, i1, "TEncoding enc = default;");
                RunEmitter.Line(find, i1, "int i = enc.IndexOfAlternation(input, pos, " + field + ");");
                RunEmitter.Line(find, i1, "if (i < 0 || len - i < p.Need)");
                RunEmitter.Line(find, i1, "{");
                RunEmitter.Line(find, i1 + 1, "return false;");
                RunEmitter.Line(find, i1, "}");
                sb_blank(find);
                RunEmitter.Line(find, i1, "start = i;");
                RunEmitter.Line(find, i1, "scan.Next = i + 1;");
                RunEmitter.Line(find, i1, "return true;");
                Tail(find, indent);
                return;
            }

            // Exact: the position every alternative has whose distinct characters (up to three) are rarest in
            // text, searched for by the span search, as the needle chooses it (Tom|river: m and v, not T and the
            // common r); then up to two other positions at which the alternatives have at most two distinct
            // characters, the rarest first.
            int searchAt = 0;
            double searchShare = double.MaxValue;
            for (int d = 0; d < minLen; d++)
            {
                char[] values = literals.Select(l => l[d]).Distinct().ToArray();
                if (values.Length <= 3)
                {
                    double share = values.Sum(v => 255 - UnitRarity.Of((ushort)v));
                    if (share < searchShare)
                    {
                        searchShare = share;
                        searchAt = d;
                    }
                }
            }

            List<char> firsts = literals.Select(l => l[searchAt]).Distinct().ToList();
            List<(int Offset, char[] Values)> checks = [];
            for (int d = 0; d < minLen; d++)
            {
                char[] values = literals.Select(l => l[d]).Distinct().ToArray();
                if (d != searchAt && values.Length <= 2)
                {
                    checks.Add((d, values));
                }
            }

            checks = checks.OrderBy(c => c.Values.Sum(v => 255 - UnitRarity.Of((ushort)v))).Take(2).ToList();
            RunEmitter.Line(find, i1, "TEncoding enc = default;");
            RunEmitter.Line(find, i1, "int c = 0;");
            RunEmitter.Line(find, i1, "_ = c;");
            RunEmitter.Line(find, i1, "int limit = len - " + minLen + ";");
            RunEmitter.Line(find, i1, "while (pos <= limit)");
            RunEmitter.Line(find, i1, "{");
            int i2 = i1 + 1;
            string method = firsts.Count == 1 ? "IndexOf" : "IndexOfAny";
            string args16 = string.Join(", ", firsts.Select(f => Ch(f)));
            string args8 = string.Join(", ", firsts.Select(f => "(byte)" + Ch(f)));
            string from = searchAt == 0 ? "pos" : "pos + " + searchAt;
            if (firsts.Count > 3)
            {
                string name = "F" + Math.Abs(string.Join("|", literals).GetHashCode()).ToString(CultureInfo.InvariantCulture);
                RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<string> " + name + " = global::System.Buffers.SearchValues.Create(new string[] { " + string.Join(", ", literals.Select(Str)) + " }, global::System.StringComparison.Ordinal);");
                RunEmitter.Line(find, i2, "int i;");
                RunEmitter.Line(find, i2, "if (typeof(TUnit) == typeof(char))");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "i = " + Call("IndexOfAny", Chars + ".Slice(pos)", name) + ";");
                RunEmitter.Line(find, i2, "}");
                RunEmitter.Line(find, i2, "else");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "i = enc.IndexOfAlternation(input, pos, " + field + ");");
                RunEmitter.Line(find, i2 + 1, "i = i < 0 ? -1 : i - pos;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                RunEmitter.Line(find, i2, "if (i < 0)");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "break;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                RunEmitter.Line(find, i2, "pos += i;");
            }
            else
            {
                RunEmitter.Line(find, i2, "int i = typeof(TUnit) == typeof(char)");
                RunEmitter.Line(find, i2 + 1, "? " + Call(method, Chars + ".Slice(" + from + ")", args16));
                RunEmitter.Line(find, i2 + 1, ": " + Call(method, Bytes8 + ".Slice(" + from + ")", args8) + ";");
                RunEmitter.Line(find, i2, "if (i < 0)");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "break;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                RunEmitter.Line(find, i2, "pos += i;");
                RunEmitter.Line(find, i2, "if (pos > limit)");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "break;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                foreach ((int offset, char[] values) in checks)
                {
                    string unit = "enc.Unit(input, pos + " + offset + ")";
                    string test = values.Length == 1 ? unit + " != " + (int)values[0] : "(c = " + unit + ") != " + (int)values[0] + " && c != " + (int)values[1];
                    RunEmitter.Line(find, i2, "if (" + test + ")");
                    RunEmitter.Line(find, i2, "{");
                    RunEmitter.Line(find, i2 + 1, "pos++;");
                    RunEmitter.Line(find, i2 + 1, "continue;");
                    RunEmitter.Line(find, i2, "}");
                    sb_blank(find);
                }
            }

            RunEmitter.Line(find, i2, "if (len - pos < p.Need)");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "break;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);

            // A start is one at which some alternative is whole: a start the checks let through that none is at
            // ("She" for Sher) costs a comparison here, not an attempt.
            for (int k = 0; k < literals.Length; k++)
            {
                RunEmitter.Line(statics, indent, "private static readonly byte[] L" + k + " = " + Bytes(literals[k]) + ";");
            }

            StringBuilder whole = new();
            for (int k = 0; k < literals.Length; k++)
            {
                if (k > 0)
                {
                    whole.Append(" || ");
                }

                whole.Append("(typeof(TUnit) == typeof(char) ? " + Call("StartsWith", Chars + ".Slice(pos)", Call("AsSpan", Str(literals[k]), string.Empty)) + " : " + Call("StartsWith", Bytes8 + ".Slice(pos)", "L" + k) + ")");
            }

            RunEmitter.Line(find, i2, "if (!(" + whole + "))");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "pos++;");
            RunEmitter.Line(find, i2 + 1, "continue;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);
            RunEmitter.Line(find, i2, "scan.Next = pos + 1;");
            RunEmitter.Line(find, i2, "start = pos;");
            RunEmitter.Line(find, i2, "return true;");
            RunEmitter.Line(find, i1, "}");
            sb_blank(find);
            RunEmitter.Line(find, i1, "return false;");
            Tail(find, indent);
        }

        /// <summary>A literal every match holds a fixed number of units after its start: found, then the start's own first characters checked before it is tried.</summary>
        private static void EmitFixed(Program p, StringBuilder find, StringBuilder statics, int indent)
        {
            string literal = p.RequiredUtf16;
            int offset16 = p.RequiredOffset;
            int offset8 = p.RequiredOffsetUtf8;
            if (literal.Length > 1)
            {
                RunEmitter.Line(statics, indent, "private static readonly byte[] B0 = " + Bytes(literal) + ";");
            }

            Head(find, indent);
            int i1 = indent + 1;
            RunEmitter.Line(find, i1, "TEncoding enc = default;");
            RunEmitter.Line(find, i1, "int c;");
            RunEmitter.Line(find, i1, "int offset = typeof(TUnit) == typeof(char) ? " + offset16 + " : " + offset8 + ";");
            if (offset8 < 0)
            {
                RunEmitter.Line(find, i1, "if (offset < 0)");
                RunEmitter.Line(find, i1, "{");
                RunEmitter.Line(find, i1 + 1, "scan.Kind = " + Ns + "CandidateScan.Fixed;");
                RunEmitter.Line(find, i1 + 1, "scan.Next = pos + " + p.RequiredCount + ";");
                RunEmitter.Line(find, i1 + 1, "scan.Floor = pos;");
                RunEmitter.Line(find, i1 + 1, "return " + Ns + "Matcher.NextStart<TUnit, TEncoding>(p, st, input, ref scan, out start);");
                RunEmitter.Line(find, i1, "}");
                sb_blank(find);
            }

            RunEmitter.Line(find, i1, "while (true)");
            RunEmitter.Line(find, i1, "{");
            int i2 = i1 + 1;
            RunEmitter.Line(find, i2, "if (pos + offset > len - " + literal.Length + ")");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "return false;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);
            string args16 = literal.Length == 1 ? Ch(literal[0]) : Call("AsSpan", Str(literal), string.Empty);
            string args8 = literal.Length == 1 ? "(byte)" + (int)literal[0] : "B0";
            string method16 = "IndexOf";
            if (literal.Length > 1 && p.RequiredUtf16Needle is { SearchesOneUnit: true } needle)
            {
                // A unit rare enough to search for alone, as the needle does: the literal compared where it is.
                int rare = needle.Offset1;
                int unit = needle.Value1;
                RunEmitter.Line(find, i2, "int i = typeof(TUnit) == typeof(char)");
                RunEmitter.Line(find, i2 + 1, "? " + Call("IndexOf", Chars + ".Slice(pos + offset + " + rare + ")", Ch(unit)));
                RunEmitter.Line(find, i2 + 1, ": " + Call("IndexOf", Bytes8 + ".Slice(pos + offset + " + rare + ")", "(byte)" + unit) + ";");
                RunEmitter.Line(find, i2, "if (i < 0)");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "return false;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                RunEmitter.Line(find, i2, "pos += i;");
                RunEmitter.Line(find, i2, "if (pos + offset > len - " + literal.Length + ")");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "return false;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                RunEmitter.Line(find, i2, "if (!(typeof(TUnit) == typeof(char) ? " + Call("StartsWith", Chars + ".Slice(pos + offset)", Call("AsSpan", Str(literal), string.Empty)) + " : " + Call("StartsWith", Bytes8 + ".Slice(pos + offset)", "B0") + "))");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "pos++;");
                RunEmitter.Line(find, i2 + 1, "continue;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
            }
            else
            {
                if (p.RequiredUtf16Needle is { PrefersStringSearch: true })
                {
                    // Common letters at both ends: the runtime's fingerprint search, held in a static field.
                    RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<string> P0 = global::System.Buffers.SearchValues.Create(new string[] { " + Str(literal) + " }, global::System.StringComparison.Ordinal);");
                    method16 = "IndexOfAny";
                    args16 = "P0";
                }

                if (literal.Length > 1 && method16 == "IndexOf")
                {
                    // No unit rare enough to search for alone and no preference for the string search: the needle
                    // compares its two rarest units at every vector (Sherlock Holmes: 25 us against the span
                    // search's 32, whose anchors S and s let many starts through).
                    RunEmitter.Line(find, i2, "int i = enc.IndexOfRequired(input, pos + offset, p);");
                    RunEmitter.Line(find, i2, "if (i < 0)");
                    RunEmitter.Line(find, i2, "{");
                    RunEmitter.Line(find, i2 + 1, "return false;");
                    RunEmitter.Line(find, i2, "}");
                    sb_blank(find);
                    RunEmitter.Line(find, i2, "pos = i - offset;");
                }
                else
                {
                    RunEmitter.Line(find, i2, "int i = typeof(TUnit) == typeof(char)");
                    RunEmitter.Line(find, i2 + 1, "? " + Call(method16, Chars + ".Slice(pos + offset)", args16));
                    RunEmitter.Line(find, i2 + 1, ": " + Call("IndexOf", Bytes8 + ".Slice(pos + offset)", args8) + ";");
                    RunEmitter.Line(find, i2, "if (i < 0)");
                    RunEmitter.Line(find, i2, "{");
                    RunEmitter.Line(find, i2 + 1, "return false;");
                    RunEmitter.Line(find, i2, "}");
                    sb_blank(find);
                    RunEmitter.Line(find, i2, "pos += i;");
                }
            }

            RunEmitter.Line(find, i2, "if (len - pos < p.Need)");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "return false;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);

            // The start's first characters, where they are a character or an ASCII class: checked here, cheaply.
            EcmaRegexNode root = p.Root!;
            while (root.Kind == EcmaRegexNodeKind.Group)
            {
                root = root.Subs[0];
            }

            EcmaRegexNode[] terms = root.Kind == EcmaRegexNodeKind.Cat ? root.Subs : [root];
            int at = 0;
            for (int k = 0; k < terms.Length && k < 2; k++)
            {
                EcmaRegexNode t = terms[k];
                string? test = null;
                if (t.Kind == EcmaRegexNodeKind.Char && t.CodePoint < 0x80)
                {
                    test = "enc.Unit(input, pos + " + at + ") != " + t.CodePoint;
                }
                else if (t.Kind == EcmaRegexNodeKind.Set)
                {
                    CharSet set = new(t.Set);
                    if (!set.HasNonAscii && !set.IsEmpty)
                    {
                        test = "(c = enc.Unit(input, pos + " + at + ")) >= 0x80 || !" + RunEmitter.AsciiTest(set, "c");
                    }
                }

                if (test is null)
                {
                    break;
                }

                RunEmitter.Line(find, i2, "if (" + test + ")");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "pos++;");
                RunEmitter.Line(find, i2 + 1, "continue;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                at++;
            }

            RunEmitter.Line(find, i2, "scan.Next = pos + 1;");
            RunEmitter.Line(find, i2, "start = pos;");
            RunEmitter.Line(find, i2, "return true;");
            RunEmitter.Line(find, i1, "}");
            Tail(find, indent);
        }

        /// <summary>The literal every match starts with in its forms: where every form is ASCII, the runtime's search ignoring case, held in a static field (over UTF-8 the runtime's needle); else the runtime's needle and the cursor's pass, inside the generated loop.</summary>
        private static void EmitCaseLiteral(Program p, StringBuilder find, StringBuilder statics, int indent)
        {
            bool ascii = AsciiFormsPrefix(p.CaseForms) == p.CaseForms.Length;
            Head(find, indent);
            int i1 = indent + 1;
            RunEmitter.Line(find, i1, "int i;");
            if (ascii)
            {
                // Every form ASCII: the runtime's search ignoring case knows them all.
                string literal = new(p.CaseForms.Select(f => (char)f[0]).ToArray());
                RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<string> C0 = global::System.Buffers.SearchValues.Create(new string[] { " + Str(literal) + " }, global::System.StringComparison.OrdinalIgnoreCase);");
                RunEmitter.Line(find, i1, "if (typeof(TUnit) == typeof(char))");
                RunEmitter.Line(find, i1, "{");
                RunEmitter.Line(find, i1 + 1, "i = pos <= len - " + literal.Length + " ? " + Call("IndexOfAny", Chars + ".Slice(pos)", "C0") + " : -1;");
                RunEmitter.Line(find, i1 + 1, "i = i < 0 ? -1 : pos + i;");
                RunEmitter.Line(find, i1, "}");
                RunEmitter.Line(find, i1, "else");
                RunEmitter.Line(find, i1, "{");
                RunEmitter.Line(find, i1 + 1, "TEncoding enc = default;");
                RunEmitter.Line(find, i1 + 1, "i = enc.IndexOfCasePrefix(input, pos, p);");
                RunEmitter.Line(find, i1, "}");
            }
            else
            {
                // A form apart (under the u flag s folds to the long s and k to the Kelvin sign): the runtime's
                // needle, which sees every form of one unit in its pass (Holmes: 41 us, against 44 by the
                // string search for the part before the s), and the cursor's pass for the forms that take more
                // units (over UTF-8).
                RunEmitter.Line(find, i1, "TEncoding enc = default;");
                RunEmitter.Line(find, i1, "i = enc.IndexOfCasePrefix(input, pos, p);");
                RunEmitter.Line(find, i1, "i = " + Ns + "Matcher.ExtraStartCase<TUnit, TEncoding>(p, st, input, pos, i);");
            }

            sb_blank(find);
            RunEmitter.Line(find, i1, "if (i < 0 || len - i < p.Need)");
            RunEmitter.Line(find, i1, "{");
            RunEmitter.Line(find, i1 + 1, "return false;");
            RunEmitter.Line(find, i1, "}");
            sb_blank(find);
            RunEmitter.Line(find, i1, "start = i;");
            RunEmitter.Line(find, i1, "scan.Next = i + 1;");
            RunEmitter.Line(find, i1, "return true;");
            Tail(find, indent);
        }

        /// <summary>The literal every match starts with: the span search for it.</summary>
        private static void EmitPrefix(Program p, StringBuilder find, StringBuilder statics, int indent)
        {
            string literal = p.Utf16Prefix;
            if (literal.Length > 1)
            {
                RunEmitter.Line(statics, indent, "private static readonly byte[] B0 = " + Bytes(literal) + ";");
            }

            Head(find, indent);
            int i1 = indent + 1;
            RunEmitter.Line(find, i1, "if (pos > len - " + literal.Length + ")");
            RunEmitter.Line(find, i1, "{");
            RunEmitter.Line(find, i1 + 1, "return false;");
            RunEmitter.Line(find, i1, "}");
            sb_blank(find);
            if (literal.Length > 1 && p.Utf16PrefixNeedle is { SearchesOneUnit: true } needle)
            {
                // A unit rare enough to search for alone, as the needle does: the literal compared where it is.
                int at = needle.Offset1;
                int unit = needle.Value1;
                RunEmitter.Line(find, i1, "while (true)");
                RunEmitter.Line(find, i1, "{");
                RunEmitter.Line(find, i1 + 1, "int i = typeof(TUnit) == typeof(char)");
                RunEmitter.Line(find, i1 + 2, "? " + Call("IndexOf", Chars + ".Slice(pos + " + at + ")", Ch(unit)));
                RunEmitter.Line(find, i1 + 2, ": " + Call("IndexOf", Bytes8 + ".Slice(pos + " + at + ")", "(byte)" + unit) + ";");
                RunEmitter.Line(find, i1 + 1, "if (i < 0)");
                RunEmitter.Line(find, i1 + 1, "{");
                RunEmitter.Line(find, i1 + 2, "return false;");
                RunEmitter.Line(find, i1 + 1, "}");
                sb_blank(find);
                RunEmitter.Line(find, i1 + 1, "pos += i;");
                RunEmitter.Line(find, i1 + 1, "if (pos > len - " + literal.Length + ")");
                RunEmitter.Line(find, i1 + 1, "{");
                RunEmitter.Line(find, i1 + 2, "return false;");
                RunEmitter.Line(find, i1 + 1, "}");
                sb_blank(find);
                RunEmitter.Line(find, i1 + 1, "if (!(typeof(TUnit) == typeof(char) ? " + Call("StartsWith", Chars + ".Slice(pos)", Call("AsSpan", Str(literal), string.Empty)) + " : " + Call("StartsWith", Bytes8 + ".Slice(pos)", "B0") + "))");
                RunEmitter.Line(find, i1 + 1, "{");
                RunEmitter.Line(find, i1 + 2, "pos++;");
                RunEmitter.Line(find, i1 + 2, "continue;");
                RunEmitter.Line(find, i1 + 1, "}");
                sb_blank(find);
                RunEmitter.Line(find, i1 + 1, "if (len - pos < p.Need)");
                RunEmitter.Line(find, i1 + 1, "{");
                RunEmitter.Line(find, i1 + 2, "return false;");
                RunEmitter.Line(find, i1 + 1, "}");
                sb_blank(find);
                RunEmitter.Line(find, i1 + 1, "start = pos;");
                RunEmitter.Line(find, i1 + 1, "scan.Next = start + 1;");
                RunEmitter.Line(find, i1 + 1, "return true;");
                RunEmitter.Line(find, i1, "}");
                Tail(find, indent);
                return;
            }

            string args16 = literal.Length == 1 ? Ch(literal[0]) : Call("AsSpan", Str(literal), string.Empty);
            string args8 = literal.Length == 1 ? "(byte)" + (int)literal[0] : "B0";
            string method16 = "IndexOf";
            if (p.Utf16PrefixNeedle is { PrefersStringSearch: true })
            {
                // Common letters at both ends: the runtime's fingerprint search, held in a static field.
                RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<string> P0 = global::System.Buffers.SearchValues.Create(new string[] { " + Str(literal) + " }, global::System.StringComparison.Ordinal);");
                method16 = "IndexOfAny";
                args16 = "P0";
            }

            RunEmitter.Line(find, i1, "int i = typeof(TUnit) == typeof(char)");
            RunEmitter.Line(find, i1 + 1, "? " + Call(method16, Chars + ".Slice(pos)", args16));
            RunEmitter.Line(find, i1 + 1, ": " + Call("IndexOf", Bytes8 + ".Slice(pos)", args8) + ";");
            RunEmitter.Line(find, i1, "if (i < 0 || len - (pos + i) < p.Need)");
            RunEmitter.Line(find, i1, "{");
            RunEmitter.Line(find, i1 + 1, "return false;");
            RunEmitter.Line(find, i1, "}");
            sb_blank(find);
            RunEmitter.Line(find, i1, "start = pos + i;");
            RunEmitter.Line(find, i1, "scan.Next = start + 1;");
            RunEmitter.Line(find, i1, "return true;");
            Tail(find, indent);
        }

        /// <summary>A first set of ASCII: one range by the range search, up to three values by the span search, more by search values held in a static field.</summary>
        /// <summary>A run of ASCII sets without upper bound then an ASCII literal: the literal is searched for, and the runs before each occurrence are walked back, last loop first, as the runtime does; a failed start fails for every later start of the same run, so the next occurrence is searched for next.</summary>
        private static void EmitAfterLoop(Program p, StringBuilder find, StringBuilder statics, int indent)
        {
            string literal = p.AfterLoopUtf16;
            CharSet[] sets = p.AfterLoopSets;
            int[] mins = p.AfterLoopMins;
            int least = 0;
            foreach (int m in mins)
            {
                least += m;
            }

            if (literal.Length > 1)
            {
                RunEmitter.Line(statics, indent, "private static readonly byte[] B0 = " + Bytes(literal) + ";");
            }

            Head(find, indent);
            int i1 = indent + 1;
            RunEmitter.Line(find, i1, "TEncoding enc = default;");
            RunEmitter.Line(find, i1, "int c;");
            RunEmitter.Line(find, i1, "while (true)");
            RunEmitter.Line(find, i1, "{");
            int i2 = i1 + 1;
            RunEmitter.Line(find, i2, "if (pos + " + least + " > len - " + literal.Length + ")");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "return false;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);
            string args16 = literal.Length == 1 ? Ch(literal[0]) : Call("AsSpan", Str(literal), string.Empty);
            string args8 = literal.Length == 1 ? "(byte)" + (int)literal[0] : "B0";
            RunEmitter.Line(find, i2, "int i = typeof(TUnit) == typeof(char)");
            RunEmitter.Line(find, i2 + 1, "? " + Call("IndexOf", Chars + ".Slice(pos + " + least + ")", args16));
            RunEmitter.Line(find, i2 + 1, ": " + Call("IndexOf", Bytes8 + ".Slice(pos + " + least + ")", args8) + ";");
            RunEmitter.Line(find, i2, "if (i < 0)");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "return false;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);
            RunEmitter.Line(find, i2, "i += pos + " + least + ";");
            RunEmitter.Line(find, i2, "int s = i;");
            for (int k = sets.Length - 1; k >= 0; k--)
            {
                if (mins[k] == 1)
                {
                    RunEmitter.Line(find, i2, "int runEnd" + k + " = s;");
                }

                RunEmitter.Line(find, i2, "while (s > pos && (c = enc.Unit(input, s - 1)) < 0x80 && " + RunEmitter.AsciiTest(sets[k], "c") + ")");
                RunEmitter.Line(find, i2, "{");
                RunEmitter.Line(find, i2 + 1, "s--;");
                RunEmitter.Line(find, i2, "}");
                sb_blank(find);
                if (mins[k] == 1)
                {
                    RunEmitter.Line(find, i2, "if (s == runEnd" + k + ")");
                    RunEmitter.Line(find, i2, "{");
                    RunEmitter.Line(find, i2 + 1, "pos = i + 1;");
                    RunEmitter.Line(find, i2 + 1, "continue;");
                    RunEmitter.Line(find, i2, "}");
                    sb_blank(find);
                }
            }

            RunEmitter.Line(find, i2, "if (len - s < p.Need)");
            RunEmitter.Line(find, i2, "{");
            RunEmitter.Line(find, i2 + 1, "return false;");
            RunEmitter.Line(find, i2, "}");
            sb_blank(find);
            RunEmitter.Line(find, i2, "scan.Next = i + 1;");
            RunEmitter.Line(find, i2, "start = s;");
            RunEmitter.Line(find, i2, "return true;");
            RunEmitter.Line(find, i1, "}");
            Tail(find, indent);
        }

        private static void EmitFirst(Program p, StringBuilder find, StringBuilder statics, int indent)
        {
            CharSet first = p.First!;
            List<int> members = [];
            for (int c = 0; c < 128; c++)
            {
                if (first.HasAscii(c))
                {
                    members.Add(c);
                }
            }

            bool range = members.Count > 1 && members[members.Count - 1] - members[0] == members.Count - 1;
            string search16;
            string search8;
            string method;
            if (range)
            {
                method = "IndexOfAnyInRange";
                search16 = Ch(members[0]) + ", " + Ch(members[members.Count - 1]);
                search8 = "(byte)" + members[0] + ", (byte)" + members[members.Count - 1];
            }
            else if (members.Count <= 3)
            {
                method = members.Count == 1 ? "IndexOf" : "IndexOfAny";
                search16 = string.Join(", ", members.Select(Ch));
                search8 = string.Join(", ", members.Select(m => "(byte)" + m));
            }
            else
            {
                string ascii = new(members.Select(m => (char)m).ToArray());
                RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<char> G0c = global::System.Buffers.SearchValues.Create(" + Str(ascii) + ");");
                RunEmitter.Line(statics, indent, "private static readonly global::System.Buffers.SearchValues<byte> G0b = global::System.Buffers.SearchValues.Create(" + Bytes(ascii) + ");");
                method = "IndexOfAny";
                search16 = "G0c";
                search8 = "G0b";
            }

            Head(find, indent);
            int i1 = indent + 1;
            RunEmitter.Line(find, i1, "if (len - pos < p.Need)");
            RunEmitter.Line(find, i1, "{");
            RunEmitter.Line(find, i1 + 1, "return false;");
            RunEmitter.Line(find, i1, "}");
            sb_blank(find);
            RunEmitter.Line(find, i1, "int i = typeof(TUnit) == typeof(char)");
            RunEmitter.Line(find, i1 + 1, "? " + Call(method, Chars + ".Slice(pos)", search16));
            RunEmitter.Line(find, i1 + 1, ": " + Call(method, Bytes8 + ".Slice(pos)", search8) + ";");
            RunEmitter.Line(find, i1, "if (i < 0 || len - (pos + i) < p.Need)");
            RunEmitter.Line(find, i1, "{");
            RunEmitter.Line(find, i1 + 1, "return false;");
            RunEmitter.Line(find, i1, "}");
            sb_blank(find);
            RunEmitter.Line(find, i1, "start = pos + i;");
            RunEmitter.Line(find, i1, "scan.Next = start + 1;");
            RunEmitter.Line(find, i1, "return true;");
            Tail(find, indent);
        }

        private static void sb_blank(StringBuilder sb) => sb.AppendLine();
    }
}
