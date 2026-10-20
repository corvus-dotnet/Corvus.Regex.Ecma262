// <copyright file="RunEmitter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Globalization;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions.SourceGenerator;

/// <summary>
/// Writes a program's instructions out as straight-line C#: each instruction a labelled block of the code the
/// interpreter would run for it, with the instruction's operands as constants, failure a jump to the shared
/// backtracking (which the library keeps), and resumption a jump table over the instructions. The method is
/// generic over the unit and the encoding, as the interpreter is, so one body serves UTF-8 and UTF-16.
/// </summary>
internal static class RunEmitter
{
    internal const string Ns = "global::Corvus.Ecma262.RegularExpressions.";

    /// <summary>The most instructions a program may have and be written as straight-line code.</summary>
    private const int MostInstructions = 2048;

    /// <summary>Writes the runner class for a program.</summary>
    /// <param name="sb">Where to write.</param>
    /// <param name="indent">The indentation of the class.</param>
    /// <param name="name">The class name.</param>
    /// <param name="p">The program.</param>
    public static void EmitRunner(StringBuilder sb, int indent, string name, Program p)
    {
        if (p.Insts.Length > MostInstructions)
        {
            // A program too large for straight-line code (a class of thousands of strings, such as \p{RGI_Emoji}
            // under the v flag): the runner hands it to the interpreter, which is what a regex with no generated
            // code does.
            Line(sb, indent, "/// <summary>The program is too large for straight-line code (" + p.Insts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " instructions): the interpreter runs it.</summary>");
            Line(sb, indent, "[global::System.CodeDom.Compiler.GeneratedCode(\"Corvus.Regex.Ecma262.SourceGenerator\", \"1.0\")]");
            Line(sb, indent, "private sealed class " + name + " : " + Ns + "GeneratedRunner");
            Line(sb, indent, "{");
            Line(sb, indent + 1, "/// <inheritdoc/>");
            Line(sb, indent + 1, "public override bool Run(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<char> input, int start, out int end) => " + Ns + "Matcher.Interpret<char, " + Ns + "Utf16Encoding>(p, st, input, start, out end);");
            sb.AppendLine();
            Line(sb, indent + 1, "/// <inheritdoc/>");
            Line(sb, indent + 1, "public override bool Run(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<byte> input, int start, out int end) => " + Ns + "Matcher.Interpret<byte, " + Ns + "Utf8Encoding>(p, st, input, start, out end);");
            Line(sb, indent, "}");
            return;
        }

        Line(sb, indent, "/// <summary>The program as straight-line code (see <see cref=\"global::Corvus.Ecma262.RegularExpressions.GeneratedRunner\"/>).</summary>");
        Line(sb, indent, "[global::System.CodeDom.Compiler.GeneratedCode(\"Corvus.Regex.Ecma262.SourceGenerator\", \"1.0\")]");
        Line(sb, indent, "private sealed class " + name + " : " + Ns + "GeneratedRunner");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "/// <inheritdoc/>");
        Line(sb, indent + 1, "public override bool Run(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<char> input, int start, out int end) => Run<char, " + Ns + "Utf16Encoding>(p, st, input, start, out end);");
        sb.AppendLine();
        Line(sb, indent + 1, "/// <inheritdoc/>");
        Line(sb, indent + 1, "public override bool Run(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<byte> input, int start, out int end) => Run<byte, " + Ns + "Utf8Encoding>(p, st, input, start, out end);");
        sb.AppendLine();
        Line(sb, indent + 1, "private static bool Run<TUnit, TEncoding>(" + Ns + "Program p, " + Ns + "MatchState st, global::System.ReadOnlySpan<TUnit> input, int start, out int end)");
        Line(sb, indent + 2, "where TUnit : unmanaged, global::System.IEquatable<TUnit>");
        Line(sb, indent + 2, "where TEncoding : struct, " + Ns + "ITextEncoding<TUnit>");
        Line(sb, indent + 1, "{");
        EmitBody(sb, indent + 2, p);
        Line(sb, indent + 1, "}");
        Line(sb, indent, "}");
    }

    private static void EmitBody(StringBuilder sb, int indent, Program p)
    {
        Inst[] insts = p.Insts;
        string units = p.Units ? "true" : "false";
        Line(sb, indent, "end = -1;");
        Line(sb, indent, "TEncoding enc = default;");
        Line(sb, indent, Ns + "Inst[] insts = p.Insts;");
        Line(sb, indent, Ns + "Quick[] quick = p.Quick;");
        Line(sb, indent, Ns + "Entry[] stack = st.Stack;");
        Line(sb, indent, "int[] caps = st.Caps;");
        Line(sb, indent, "int[] tmp = st.Tmp;");
        Line(sb, indent, "int[] counts = st.Counts;");
        Line(sb, indent, "int sp = 0;");
        Line(sb, indent, "const bool units = " + units + ";");
        Line(sb, indent, "int len = input.Length;");
        Line(sb, indent, "int pc = 0;");
        Line(sb, indent, "int pos = start;");
        Line(sb, indent, "long steps = st.Steps;");
        Line(sb, indent, "bool memo = st.MemoOn;");
        Line(sb, indent, "ulong[] memoBits = st.Memo;");
        Line(sb, indent, "int c;");
        Line(sb, indent, "int r;");
        Line(sb, indent, "int w;");
        Line(sb, indent, "int at;");
        Line(sb, indent, "int n;");
        Line(sb, indent, "int idx;");
        Line(sb, indent, "int a;");
        Line(sb, indent, "int from;");
        Line(sb, indent, "int to;");
        Line(sb, indent, "bool ok;");
        Line(sb, indent, "bool before;");
        Line(sb, indent, "bool after;");
        Line(sb, indent, "goto L0;");
        sb.AppendLine();

        for (int k = 0; k < insts.Length; k++)
        {
            EmitInst(sb, indent, p, k);
            sb.AppendLine();
        }

        // Failure: the shared backtracking, then the jump table over the instructions.
        Line(sb, indent - 1, "Fail:");
        Line(sb, indent, "if (!" + Ns + "Matcher.Backtrack<TUnit, TEncoding>(p, st, input, ref stack, ref sp, ref pc, ref pos, ref steps, memo, memoBits))");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "st.Steps = steps;");
        Line(sb, indent + 1, "st.Stack = stack;");
        Line(sb, indent + 1, "return false;");
        Line(sb, indent, "}");
        sb.AppendLine();
        Line(sb, indent, "switch (pc)");
        Line(sb, indent, "{");
        for (int k = 0; k < insts.Length; k++)
        {
            Line(sb, indent + 1, "case " + k + ": goto L" + k + ";");
        }

        Line(sb, indent + 1, "default: throw new global::System.InvalidOperationException(\"No instruction \" + pc);");
        Line(sb, indent, "}");
    }

    private static void EmitInst(StringBuilder sb, int indent, Program p, int k)
    {
        Inst i = p.Insts[k];
        string next = "goto L" + (k + 1) + ";";
        Line(sb, indent - 1, "L" + k + ":");
        switch (i.Op)
        {
            case Op.Char:
                if (i.R < 0x80)
                {
                    Line(sb, indent, "if (pos < len && enc.Unit(input, pos) == " + i.R + ")");
                    Line(sb, indent, "{");
                    Line(sb, indent + 1, "pos++;");
                    Line(sb, indent + 1, next);
                    Line(sb, indent, "}");
                }
                else
                {
                    Line(sb, indent, "if (pos < len && enc.Unit(input, pos) >= 0x80)");
                    Line(sb, indent, "{");
                    Line(sb, indent + 1, "r = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);");
                    Line(sb, indent + 1, "if (r == " + i.R + ")");
                    Line(sb, indent + 1, "{");
                    Line(sb, indent + 2, "pos += w;");
                    Line(sb, indent + 2, next);
                    Line(sb, indent + 1, "}");
                    Line(sb, indent, "}");
                }

                Line(sb, indent, "goto Fail;");
                break;

            case Op.Set:
            {
                CharSet set = i.Set!;
                Line(sb, indent, "if (pos < len)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "c = enc.Unit(input, pos);");
                Line(sb, indent + 1, "if (c < 0x80)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "if (" + AsciiTest(set, "c") + ")");
                Line(sb, indent + 2, "{");
                Line(sb, indent + 3, "pos++;");
                Line(sb, indent + 3, next);
                Line(sb, indent + 2, "}");
                Line(sb, indent + 1, "}");
                if (set.HasNonAscii)
                {
                    Line(sb, indent + 1, "else");
                    Line(sb, indent + 1, "{");
                    Line(sb, indent + 2, "r = units ? enc.UnitAt(input, pos, out w) : enc.Decode(input, pos, out w);");
                    Line(sb, indent + 2, "if (insts[" + k + "].Set!.Contains(r))");
                    Line(sb, indent + 2, "{");
                    Line(sb, indent + 3, "pos += w;");
                    Line(sb, indent + 3, next);
                    Line(sb, indent + 2, "}");
                    Line(sb, indent + 1, "}");
                }

                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;
            }

            case Op.Lit:
                Line(sb, indent, "if (enc.LiteralAt(input, pos, p, " + i.Lit + ", out w))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "pos += w;");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.CharBack:
            case Op.SetBack:
                Line(sb, indent, "if (pos > 0)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "r = enc.Unit(input, pos - 1);");
                Line(sb, indent + 1, "w = 1;");
                Line(sb, indent + 1, "if (r >= 0x80)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "r = units ? enc.UnitBefore(input, pos, out w) : enc.DecodeBefore(input, pos, out w);");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
                Line(sb, indent + 1, "if (" + (i.Op == Op.CharBack ? "r == " + i.R : "(r < 0x80 ? " + AsciiTest(i.Set!, "r") + " : insts[" + k + "].Set!.Contains(r))") + ")");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "pos -= w;");
                Line(sb, indent + 2, next);
                Line(sb, indent + 1, "}");
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.Fail:
                Line(sb, indent, "goto Fail;");
                break;

            case Op.Bol:
                Line(sb, indent, "if (pos == 0)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.Eol:
                Line(sb, indent, "if (pos == len)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.WordBoundary:
            case Op.NotWordBoundary:
                Line(sb, indent, "before = pos > 0 && " + Ns + "Utf8Encoding.IsWordUnit(enc.Unit(input, pos - 1));");
                Line(sb, indent, "after = pos < len && " + Ns + "Utf8Encoding.IsWordUnit(enc.Unit(input, pos));");
                Line(sb, indent, "if (" + (i.Op == Op.WordBoundary ? "before != after" : "before == after") + ")");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.LineStart:
                Line(sb, indent, "if (pos == 0 || enc.IsLineTerminatorBefore(input, pos))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.LineEnd:
                Line(sb, indent, "if (pos == len || enc.IsLineTerminatorAt(input, pos))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.WordBoundaryFold:
            case Op.NotWordBoundaryFold:
                Line(sb, indent, "before = pos > 0 && " + Ns + "Matcher.IsFoldWordRune(enc.DecodeBefore(input, pos, out _));");
                Line(sb, indent, "after = pos < len && " + Ns + "Matcher.IsFoldWordRune(enc.Decode(input, pos, out _));");
                Line(sb, indent, "if (" + (i.Op == Op.WordBoundaryFold ? "before != after" : "before == after") + ")");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                Line(sb, indent, "goto Fail;");
                break;

            case Op.Jmp:
                EmitSteps(sb, indent);
                Line(sb, indent, "goto L" + i.X + ";");
                break;

            case Op.Split:
                EmitSteps(sb, indent);
                EmitMemoCheck(sb, indent, i.M, "pos");
                Line(sb, indent, "if (!quick[" + i.Y + "].Allows<TUnit, TEncoding>(input, pos))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "if (!quick[" + i.X + "].Allows<TUnit, TEncoding>(input, pos))");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "goto Fail;");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
                Line(sb, indent + 1, "goto L" + i.X + ";");
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "if (!quick[" + i.X + "].Allows<TUnit, TEncoding>(input, pos))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "goto L" + i.Y + ";");
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Choice, " + i.Y + ", pos, 0);");
                Line(sb, indent, "goto L" + i.X + ";");
                break;

            case Op.SaveTmp:
                Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Tmp, " + i.A + ", tmp[" + i.A + "], 0);");
                Line(sb, indent, "tmp[" + i.A + "] = pos;");
                Line(sb, indent, next);
                break;

            case Op.Capture:
                Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Cap, " + i.A + ", caps[" + (2 * i.A) + "], caps[" + ((2 * i.A) + 1) + "]);");
                if (i.Flag)
                {
                    Line(sb, indent, "caps[" + (2 * i.A) + "] = pos;");
                    Line(sb, indent, "caps[" + ((2 * i.A) + 1) + "] = tmp[" + i.A + "];");
                }
                else
                {
                    Line(sb, indent, "caps[" + (2 * i.A) + "] = tmp[" + i.A + "];");
                    Line(sb, indent, "caps[" + ((2 * i.A) + 1) + "] = pos;");
                }

                Line(sb, indent, next);
                break;

            case Op.Clear:
                Line(sb, indent, "steps -= " + (i.B - i.A) + ";");
                Line(sb, indent, "for (int g = " + i.A + "; g <= " + i.B + "; g++)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "if (caps[2 * g] != " + Ns + "MatchState.None)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Cap, g, caps[2 * g], caps[(2 * g) + 1]);");
                Line(sb, indent + 2, "caps[2 * g] = " + Ns + "MatchState.None;");
                Line(sb, indent + 2, "caps[(2 * g) + 1] = " + Ns + "MatchState.None;");
                Line(sb, indent + 1, "}");
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, next);
                break;

            case Op.Backref:
            case Op.BackrefBack:
            {
                bool back = i.Op == Op.BackrefBack;
                EmitSteps(sb, indent);
                Line(sb, indent, "from = caps[" + (2 * i.A) + "];");
                Line(sb, indent, "to = caps[" + ((2 * i.A) + 1) + "];");
                Line(sb, indent, "if (from == " + Ns + "MatchState.None)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "n = to - from;");
                Line(sb, indent, back
                    ? "if (pos >= n && global::System.MemoryExtensions.SequenceEqual(input.Slice(pos - n, n), input.Slice(from, n)))"
                    : "if (len - pos >= n && global::System.MemoryExtensions.SequenceEqual(input.Slice(pos, n), input.Slice(from, n)))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "steps -= n >> 3;");
                Line(sb, indent + 1, back ? "pos -= n;" : "pos += n;");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "goto Fail;");
                break;
            }

            case Op.BackrefFold:
            case Op.BackrefUnits:
            case Op.BackrefFoldBack:
            case Op.BackrefUnitsBack:
            {
                bool back = i.Op is Op.BackrefFoldBack or Op.BackrefUnitsBack;
                bool fold = i.Op is Op.BackrefFold or Op.BackrefFoldBack;
                string flag = i.Flag ? "true" : "false";
                EmitSteps(sb, indent);
                Line(sb, indent, "from = caps[" + (2 * i.A) + "];");
                Line(sb, indent, "to = caps[" + ((2 * i.A) + 1) + "];");
                Line(sb, indent, "if (from == " + Ns + "MatchState.None)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "steps -= (to - from) >> 3;");
                Line(sb, indent, "at = pos;");
                Line(sb, indent, "ok = true;");
                Line(sb, indent, "while (from < to && ok)");
                Line(sb, indent, "{");
                if (back)
                {
                    Line(sb, indent + 1, "int want = units ? enc.UnitBefore(input, to, out w) : enc.DecodeBefore(input, to, out w);");
                    Line(sb, indent + 1, "to -= w;");
                    Line(sb, indent + 1, "if (at == 0)");
                    Line(sb, indent + 1, "{");
                    Line(sb, indent + 2, "ok = false;");
                    Line(sb, indent + 2, "break;");
                    Line(sb, indent + 1, "}");
                    sb.AppendLine();
                    Line(sb, indent + 1, "int got = units ? enc.UnitBefore(input, at, out w) : enc.DecodeBefore(input, at, out w);");
                    Line(sb, indent + 1, "at -= w;");
                }
                else
                {
                    Line(sb, indent + 1, "int want = units ? enc.UnitAt(input, from, out w) : enc.Decode(input, from, out w);");
                    Line(sb, indent + 1, "from += w;");
                    Line(sb, indent + 1, "if (at >= len)");
                    Line(sb, indent + 1, "{");
                    Line(sb, indent + 2, "ok = false;");
                    Line(sb, indent + 2, "break;");
                    Line(sb, indent + 1, "}");
                    sb.AppendLine();
                    Line(sb, indent + 1, "int got = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);");
                    Line(sb, indent + 1, "at += w;");
                }

                Line(sb, indent + 1, fold
                    ? "ok = got == want || " + Ns + "CaseFolding.Canonicalize(got, " + flag + ") == " + Ns + "CaseFolding.Canonicalize(want, " + flag + ");"
                    : "ok = got == want;");
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "if (ok)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, "pos = at;");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "goto Fail;");
                break;
            }

            case Op.Look:
                EmitSteps(sb, indent);
                Line(sb, indent, "if (quick[" + k + "].Allows<TUnit, TEncoding>(input, pos))");
                Line(sb, indent, "{");
                Line(sb, indent + 1, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + (i.Flag ? "Entry.LookNeg" : "Entry.Look") + ", " + i.X + ", pos, 0);");
                Line(sb, indent + 1, next);
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "goto Fail;");
                break;

            case Op.LookOk:
                Line(sb, indent, "{");
                Line(sb, indent + 1, "int f = sp - 1;");
                Line(sb, indent + 1, "while (stack[f].Kind != " + Ns + "Entry.Look)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "f--;");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
                Line(sb, indent + 1, "pos = stack[f].Pos;");
                Line(sb, indent + 1, "int keep = f;");
                Line(sb, indent + 1, "for (int q = f + 1; q < sp; q++)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "if (stack[q].Kind >= " + Ns + "Entry.Tmp)");
                Line(sb, indent + 2, "{");
                Line(sb, indent + 3, "stack[keep++] = stack[q];");
                Line(sb, indent + 2, "}");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
                Line(sb, indent + 1, "sp = keep;");
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, next);
                break;

            case Op.LookFail:
                Line(sb, indent, "while (sp > 0)");
                Line(sb, indent, "{");
                Line(sb, indent + 1, Ns + "Entry e = stack[--sp];");
                Line(sb, indent + 1, "if (e.Kind == " + Ns + "Entry.LookNeg)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "break;");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
                Line(sb, indent + 1, "st.Undo(in e);");
                Line(sb, indent, "}");
                sb.AppendLine();
                Line(sb, indent, "goto Fail;");
                break;

            case Op.Rep:
                EmitRep(sb, indent, p, k, next);
                break;

            case Op.RepLazy:
                EmitRepLazy(sb, indent, k, i, next);
                break;

            case Op.LoopInit:
                Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Counter, " + i.A + ", counts[" + (2 * i.A) + "], counts[" + ((2 * i.A) + 1) + "]);");
                Line(sb, indent, "counts[" + (2 * i.A) + "] = 0;");
                Line(sb, indent, "counts[" + ((2 * i.A) + 1) + "] = " + Ns + "MatchState.None;");
                Line(sb, indent, next);
                break;

            case Op.Loop:
                EmitLoop(sb, indent, k, i, next);
                break;

            case Op.LoopIter:
                EmitSteps(sb, indent);
                Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Counter, " + i.A + ", counts[" + (2 * i.A) + "], counts[" + ((2 * i.A) + 1) + "]);");
                Line(sb, indent, "counts[" + (2 * i.A) + "]++;");
                Line(sb, indent, "counts[" + ((2 * i.A) + 1) + "] = pos;");
                Line(sb, indent, next);
                break;

            case Op.Match:
                Line(sb, indent, "st.Steps = steps;");
                Line(sb, indent, "st.Stack = stack;");
                Line(sb, indent, "end = pos;");
                Line(sb, indent, "return true;");
                break;

            default:
                throw new InvalidOperationException("Unknown instruction " + i.Op);
        }
    }

    private static void EmitRep(StringBuilder sb, int indent, Program p, int k, string next)
    {
        Inst i = p.Insts[k];
        CharSet? set = i.Set;
        int max = i.Max;
        int min = i.Min;
        Line(sb, indent, "at = pos;");
        Line(sb, indent, "n = 0;");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "int floor = pos;");
        if (max < 0 && min <= 1 && (set is not null || i.R < 0x80))
        {
            if (min == 1)
            {
                Line(sb, indent + 1, "w = " + Ns + "Matcher.One<TUnit, TEncoding>(in insts[" + k + "], input, at, units);");
                Line(sb, indent + 1, "if (w == 0)");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "goto Fail;");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
                Line(sb, indent + 1, "at += w;");
                Line(sb, indent + 1, "floor = at;");
            }

            Line(sb, indent + 1, set is not null
                ? (set.IsLineRest
                    ? "at = " + Ns + "Matcher.EndOfLine<TUnit, TEncoding>(st, input, at);"
                    : "at = " + Ns + "Matcher.EndOfRun<TUnit, TEncoding>(insts[" + k + "].Set!, input, at, units);")
                : "at = " + Ns + "Matcher.EndOfRun<TUnit, TEncoding>(" + i.R + ", input, at);");
            Line(sb, indent + 1, "n = " + min + ";");
        }
        else if (set is not null)
        {
            if (max >= 12)
            {
                Line(sb, indent + 1, "at = " + Ns + "Matcher.AsciiRun<TUnit, TEncoding>(insts[" + k + "].Set!, input, at, " + max + ", units);");
                Line(sb, indent + 1, "n = at - pos;");
                Line(sb, indent + 1, "if (n >= " + min + ")");
                Line(sb, indent + 1, "{");
                Line(sb, indent + 2, "floor = pos + " + min + ";");
                Line(sb, indent + 1, "}");
                sb.AppendLine();
            }

            Line(sb, indent + 1, "while (n != " + max + " && at < len)");
            Line(sb, indent + 1, "{");
            Line(sb, indent + 2, "c = enc.Unit(input, at);");
            Line(sb, indent + 2, "if (c < 0x80)");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, "if (!(" + AsciiTest(set, "c") + "))");
            Line(sb, indent + 3, "{");
            Line(sb, indent + 4, "break;");
            Line(sb, indent + 3, "}");
            sb.AppendLine();
            Line(sb, indent + 3, "at++;");
            Line(sb, indent + 2, "}");
            Line(sb, indent + 2, "else");
            Line(sb, indent + 2, "{");
            if (set.HasNonAscii)
            {
                Line(sb, indent + 3, "r = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);");
                Line(sb, indent + 3, "if (!insts[" + k + "].Set!.Contains(r))");
                Line(sb, indent + 3, "{");
                Line(sb, indent + 4, "break;");
                Line(sb, indent + 3, "}");
                sb.AppendLine();
                Line(sb, indent + 3, "at += w;");
            }
            else
            {
                Line(sb, indent + 3, "break;");
            }

            Line(sb, indent + 2, "}");
            sb.AppendLine();
            Line(sb, indent + 2, "n++;");
            Line(sb, indent + 2, "if (n == " + min + ")");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, "floor = at;");
            Line(sb, indent + 2, "}");
            Line(sb, indent + 1, "}");
        }
        else
        {
            Line(sb, indent + 1, "while (n != " + max + " && at < len)");
            Line(sb, indent + 1, "{");
            Line(sb, indent + 2, "r = enc.Unit(input, at);");
            Line(sb, indent + 2, "w = 1;");
            Line(sb, indent + 2, "if (r >= 0x80)");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, "r = units ? enc.UnitAt(input, at, out w) : enc.Decode(input, at, out w);");
            Line(sb, indent + 2, "}");
            sb.AppendLine();
            Line(sb, indent + 2, "if (r != " + i.R + ")");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, "break;");
            Line(sb, indent + 2, "}");
            sb.AppendLine();
            Line(sb, indent + 2, "at += w;");
            Line(sb, indent + 2, "n++;");
            Line(sb, indent + 2, "if (n == " + min + ")");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, "floor = at;");
            Line(sb, indent + 2, "}");
            Line(sb, indent + 1, "}");
        }

        sb.AppendLine();
        Line(sb, indent + 1, "steps -= (at - pos) >> 3;");
        EmitSteps(sb, indent + 1);
        Line(sb, indent + 1, "if (n >= " + min + ")");
        Line(sb, indent + 1, "{");
        if (i.Atomic)
        {
            Line(sb, indent + 2, "pos = at;");
            Line(sb, indent + 2, next);
        }
        else
        {
            if (i.M != 0)
            {
                Line(sb, indent + 2, "if (memo)");
                Line(sb, indent + 2, "{");
                Line(sb, indent + 3, "idx = " + Ns + "Matcher.MemoIndex(p, st, " + i.M + ", at);");
                Line(sb, indent + 3, "if (" + Ns + "Matcher.MemoBit(memoBits, idx))");
                Line(sb, indent + 3, "{");
                Line(sb, indent + 4, "if (at > floor)");
                Line(sb, indent + 4, "{");
                Line(sb, indent + 5, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Rep, " + (k + 1) + ", at, floor);");
                Line(sb, indent + 4, "}");
                sb.AppendLine();
                Line(sb, indent + 4, "goto Fail;");
                Line(sb, indent + 3, "}");
                sb.AppendLine();
                Line(sb, indent + 3, "if (at == floor && sp < " + Ns + "Matcher.MaxMarkDepth)");
                Line(sb, indent + 3, "{");
                Line(sb, indent + 4, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Mark, 0, idx, 0);");
                Line(sb, indent + 3, "}");
                Line(sb, indent + 2, "}");
                sb.AppendLine();
            }

            Line(sb, indent + 2, "if (at > floor)");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Rep, " + (k + 1) + ", at, floor);");
            Line(sb, indent + 2, "}");
            sb.AppendLine();
            Line(sb, indent + 2, "pos = at;");
            Line(sb, indent + 2, next);
        }

        Line(sb, indent + 1, "}");
        Line(sb, indent, "}");
        sb.AppendLine();
        Line(sb, indent, "goto Fail;");
    }

    private static void EmitRepLazy(StringBuilder sb, int indent, int k, Inst i, string next)
    {
        Line(sb, indent, "at = pos;");
        Line(sb, indent, "n = 0;");
        Line(sb, indent, "while (n < " + i.Min + ")");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "w = " + Ns + "Matcher.One<TUnit, TEncoding>(in insts[" + k + "], input, at, units);");
        Line(sb, indent + 1, "if (w == 0)");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, "break;");
        Line(sb, indent + 1, "}");
        sb.AppendLine();
        Line(sb, indent + 1, "at += w;");
        Line(sb, indent + 1, "n++;");
        Line(sb, indent, "}");
        sb.AppendLine();
        Line(sb, indent, "steps -= n >> 3;");
        EmitSteps(sb, indent);
        Line(sb, indent, "if (n == " + i.Min + ")");
        Line(sb, indent, "{");
        if (i.M != 0)
        {
            Line(sb, indent + 1, "if (memo)");
            Line(sb, indent + 1, "{");
            Line(sb, indent + 2, "idx = " + Ns + "Matcher.MemoIndex(p, st, " + i.M + ", at);");
            Line(sb, indent + 2, "if (" + Ns + "Matcher.MemoBit(memoBits, idx))");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, "if (n != " + i.Max + ")");
            Line(sb, indent + 3, "{");
            Line(sb, indent + 4, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.RepLazy, " + k + ", at, n);");
            Line(sb, indent + 3, "}");
            sb.AppendLine();
            Line(sb, indent + 3, "goto Fail;");
            Line(sb, indent + 2, "}");
            sb.AppendLine();
            Line(sb, indent + 2, "if (n == " + i.Max + " && sp < " + Ns + "Matcher.MaxMarkDepth)");
            Line(sb, indent + 2, "{");
            Line(sb, indent + 3, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Mark, 0, idx, 0);");
            Line(sb, indent + 2, "}");
            Line(sb, indent + 1, "}");
            sb.AppendLine();
        }

        Line(sb, indent + 1, "if (n != " + i.Max + ")");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.RepLazy, " + k + ", at, n);");
        Line(sb, indent + 1, "}");
        sb.AppendLine();
        Line(sb, indent + 1, "pos = at;");
        Line(sb, indent + 1, next);
        Line(sb, indent, "}");
        sb.AppendLine();
        Line(sb, indent, "goto Fail;");
    }

    private static void EmitLoop(StringBuilder sb, int indent, int k, Inst i, string next)
    {
        int a = i.A;
        EmitSteps(sb, indent);
        Line(sb, indent, "{");
        Line(sb, indent + 1, "long count = counts[" + (2 * a) + "];");
        Line(sb, indent + 1, "long left = len - pos;");
        string iter = i.Iter != 0 ? " || (count < " + i.Min + " && left - " + i.Need + " < (" + i.Min + " - count) * " + i.Iter + ")" : string.Empty;
        Line(sb, indent + 1, "if (left < " + i.Need + iter + ")");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, "goto Fail;");
        Line(sb, indent + 1, "}");
        sb.AppendLine();
        Line(sb, indent + 1, "if (count > " + i.Min + " && pos == counts[" + ((2 * a) + 1) + "])");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, "goto Fail;");
        Line(sb, indent + 1, "}");
        sb.AppendLine();
        Line(sb, indent + 1, "if (count < " + i.Min + ")");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, next);
        Line(sb, indent + 1, "}");
        if (i.Max >= 0)
        {
            sb.AppendLine();
            Line(sb, indent + 1, "if (count >= " + i.Max + ")");
            Line(sb, indent + 1, "{");
            Line(sb, indent + 2, "goto L" + i.X + ";");
            Line(sb, indent + 1, "}");
        }

        Line(sb, indent, "}");
        sb.AppendLine();
        EmitMemoCheck(sb, indent, i.M, "pos");
        Line(sb, indent, "if (!quick[" + i.X + "].Allows<TUnit, TEncoding>(input, pos))");
        Line(sb, indent, "{");
        Line(sb, indent + 1, next);
        Line(sb, indent, "}");
        sb.AppendLine();
        Line(sb, indent, "if (!quick[" + (k + 1) + "].Allows<TUnit, TEncoding>(input, pos))");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "goto L" + i.X + ";");
        Line(sb, indent, "}");
        sb.AppendLine();
        if (i.Flag)
        {
            Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Choice, " + (k + 1) + ", pos, 0);");
            Line(sb, indent, "goto L" + i.X + ";");
        }
        else
        {
            Line(sb, indent, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Choice, " + i.X + ", pos, 0);");
            Line(sb, indent, next);
        }
    }

    /// <summary>The memo check of a split or loop: a place from which every way has failed before fails at once; otherwise its mark is pushed.</summary>
    private static void EmitMemoCheck(StringBuilder sb, int indent, int m, string pos)
    {
        if (m == 0)
        {
            return;
        }

        Line(sb, indent, "if (memo)");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "idx = " + Ns + "Matcher.MemoIndex(p, st, " + m + ", " + pos + ");");
        Line(sb, indent + 1, "if (" + Ns + "Matcher.MemoBit(memoBits, idx))");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, "goto Fail;");
        Line(sb, indent + 1, "}");
        sb.AppendLine();
        Line(sb, indent + 1, "if (sp < " + Ns + "Matcher.MaxMarkDepth)");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, Ns + "Matcher.Push(ref stack, ref sp, " + Ns + "Entry.Mark, 0, idx, 0);");
        Line(sb, indent + 1, "}");
        Line(sb, indent, "}");
        sb.AppendLine();
    }

    /// <summary>The step count and its check, at the instructions that can run many times.</summary>
    private static void EmitSteps(StringBuilder sb, int indent)
    {
        Line(sb, indent, "if (--steps < 0)");
        Line(sb, indent, "{");
        Line(sb, indent + 1, "st.Stack = stack;");
        Line(sb, indent + 1, "if (!st.Check(sp))");
        Line(sb, indent + 1, "{");
        Line(sb, indent + 2, "st.Steps = 0;");
        Line(sb, indent + 2, "return false;");
        Line(sb, indent + 1, "}");
        sb.AppendLine();
        Line(sb, indent + 1, "steps = " + Ns + "Matcher.CheckEvery;");
        Line(sb, indent + 1, "memo = st.MemoOn;");
        Line(sb, indent + 1, "memoBits = st.Memo;");
        Line(sb, indent, "}");
        sb.AppendLine();
    }

    /// <summary>The test of an ASCII unit against a set, as its two bitmaps.</summary>
    internal static string AsciiTest(CharSet set, string c)
    {
        ulong a0 = set.Ascii0;
        ulong a1 = set.Ascii1;
        if (a0 == 0 && a1 == 0)
        {
            return "false";
        }

        if (a0 == ulong.MaxValue && a1 == ulong.MaxValue)
        {
            return "true";
        }

        // One range of ASCII, or all of ASCII but one range, is a subtraction and a compare (the caller has
        // established that c is ASCII).
        if (OneRange(a0, a1, out int lo, out int hi))
        {
            return lo == hi ? "(" + c + " == " + lo + ")" : "((uint)(" + c + " - " + lo + ") <= " + (hi - lo) + ")";
        }

        if (OneRange(~a0, ~a1, out lo, out hi))
        {
            return lo == hi ? "(" + c + " != " + lo + ")" : "((uint)(" + c + " - " + lo + ") > " + (hi - lo) + ")";
        }

        return "(" + c + " < 64 ? ((" + Hex(a0) + " >> " + c + ") & 1) != 0 : ((" + Hex(a1) + " >> (" + c + " - 64)) & 1) != 0)";
    }

    /// <summary>Whether the set bits of a 128-bit ASCII bitmap are one contiguous range, and which.</summary>
    private static bool OneRange(ulong a0, ulong a1, out int lo, out int hi)
    {
        lo = -1;
        hi = -1;
        for (int c = 0; c < 128; c++)
        {
            bool on = c < 64 ? ((a0 >> c) & 1) != 0 : ((a1 >> (c - 64)) & 1) != 0;
            if (on)
            {
                if (lo < 0)
                {
                    lo = c;
                }
                else if (c != hi + 1)
                {
                    return false;
                }

                hi = c;
            }
        }

        return lo >= 0;
    }

    private static string Hex(ulong v) => "0x" + v.ToString("X", CultureInfo.InvariantCulture) + "UL";

    internal static void Line(StringBuilder sb, int indent, string text)
    {
        for (int k = 0; k < indent; k++)
        {
            sb.Append("    ");
        }

        sb.AppendLine(text);
    }
}
