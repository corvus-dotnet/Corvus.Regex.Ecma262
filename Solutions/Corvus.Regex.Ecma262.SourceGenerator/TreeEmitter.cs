// <copyright file="TreeEmitter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
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
        e.EmitBody(code, indent + 2);

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

        public void EmitBody(StringBuilder output, int baseIndent)
        {
            this.code = new StringBuilder();
            this.indent = baseIndent;
            this.Line("end = -1;");
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
            this.Emit(this.p.Root!, Follow.Unknown);
            this.Line("st.Steps = steps;");
            this.Line("end = pos;");
            this.Line("return true;");
            sb_label(this.code, baseIndent - 1, "Fail");
            this.Line("st.Steps = steps;");
            this.Line("return false;");
            output.Append(prologue);
            output.Append(this.decls);
            output.AppendLine();
            output.Append(this.code);

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
        private void EmitSet(CharSet set, string at, string fail, bool bounded)
        {
            if (set.IsEmpty)
            {
                this.Line("goto " + fail + ";");
                return;
            }

            string bound = bounded ? at + " < len && " : string.Empty;
            this.Line("c = " + bound + "true ? enc.Unit(input, " + at + ") : -1;");
            if (!set.HasNonAscii)
            {
                this.Line("if (c >= 0 && c < 0x80 && " + RunEmitter.AsciiTest(set, "c") + ")");
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
                // A run of ASCII characters is one bounds check and a comparison each.
                int j = i;
                while (j < n.Subs.Length && n.Subs[j].Kind == EcmaRegexNodeKind.Char && IsLiteralRune(n.Subs[j].CodePoint))
                {
                    j++;
                }

                if (j - i >= 2)
                {
                    StringBuilder test = new();
                    test.Append("len - pos < ").Append(j - i);
                    for (int k = i; k < j; k++)
                    {
                        test.Append(" || enc.Unit(input, pos").Append(k == i ? string.Empty : " + " + (k - i)).Append(") != ").Append(n.Subs[k].CodePoint);
                    }

                    this.Line("if (" + test + ")");
                    this.Line("{");
                    this.Line("    goto " + this.done + ";");
                    this.Line("}");
                    sb_blank(this.code);
                    this.Line("pos += " + (j - i) + ";");
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

                this.Line(set is not null
                    ? "at = " + Ns + "Matcher.EndOfRun<TUnit, TEncoding>(" + this.SetName(set) + ", input, at, units);"
                    : "at = " + Ns + "Matcher.EndOfRun<TUnit, TEncoding>(" + body.CodePoint + ", input, at);");
                this.Line("n = " + min + ";");
            }
            else
            {
                if (set is not null && max >= 20)
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
            if (filter)
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
}
