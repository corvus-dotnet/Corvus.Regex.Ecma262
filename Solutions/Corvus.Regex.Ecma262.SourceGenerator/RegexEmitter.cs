// <copyright file="RegexEmitter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Globalization;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions.SourceGenerator;

/// <summary>
/// The C# of a pattern's regular expression: a static class holding one <c>Regex</c>, made the first time it is
/// used, with the pattern's programs written out as straight-line code. The source generator writes its
/// <c>[GeneratedRegex]</c> members with it, and a code generator of its own takes it from the
/// <c>Corvus.Regex.Ecma262.CodeGeneration.Sources</c> package, compiled into itself, to write the same code into
/// the code it generates (a source generator does not run on another's output, so an attribute there would never
/// be given its body).
/// </summary>
public static class RegexEmitter
{
    private const string Ns = "global::Corvus.Ecma262.RegularExpressions.";

    /// <summary>The largest image of a compiled regular expression written into generated code: 64 KiB.</summary>
    private const int MostImageBytes = 64 * 1024;

    /// <summary>
    /// Writes the class that holds a pattern's regular expression, with the process-wide default match timeout.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="holderName">The name of the class.</param>
    /// <param name="source">The class, a <c>private static class</c> to nest in a type, whose
    /// <c>Instance</c> is the regular expression; <see langword="null"/> when the pattern does not compile.</param>
    /// <param name="error">Why the pattern does not compile, as the library's <c>Regex.TryCreate</c> says; or
    /// <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the pattern compiles.</returns>
    public static bool TryEmit(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion, string holderName, out string? source, out string? error)
    {
        return TryEmit(pattern, flags, null, edition, unicodeVersion, holderName, out source, out error);
    }

    /// <summary>Writes the class that holds a pattern's regular expression.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="timeoutMilliseconds">The match timeout in milliseconds, -1 for none; or <see langword="null"/>
    /// for the process-wide default (<c>Regex.DefaultMatchTimeout</c>).</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="holderName">The name of the class.</param>
    /// <param name="source">The class, a <c>private static class</c> to nest in a type, whose
    /// <c>Instance</c> is the regular expression; <see langword="null"/> when the pattern does not compile.</param>
    /// <param name="error">Why the pattern does not compile, as the library's <c>Regex.TryCreate</c> says; or
    /// <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the pattern compiles.</returns>
    public static bool TryEmit(string pattern, RegexFlags flags, int? timeoutMilliseconds, RegexEdition edition, RegexUnicodeVersion unicodeVersion, string holderName, out string? source, out string? error)
    {
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        if (holderName is null)
        {
            throw new ArgumentNullException(nameof(holderName));
        }

        source = null;
        error = CheckSettings((int)flags, (int)edition, (int)unicodeVersion);
        if (error is null && timeoutMilliseconds is int ms && ms < -1)
        {
            error = "the timeout " + ms.ToString(CultureInfo.InvariantCulture) + " is not -1 or more";
        }

        if (error is not null)
        {
            return false;
        }

        string runners;
        try
        {
            runners = Runners(pattern, (int)flags, edition, unicodeVersion);
        }
        catch (RegexPatternTooLargeException e)
        {
            error = e.Message;
            return false;
        }
        catch (RegexParseException e)
        {
            error = e.Message;
            return false;
        }

        StringBuilder sb = new();
        AppendHolder(sb, 0, holderName, Summary(pattern, Letters((int)flags)), pattern, (int)flags, timeoutMilliseconds, (int)edition, (int)unicodeVersion, runners);
        source = sb.ToString();
        return true;
    }

    /// <summary>
    /// Whether a pattern compiles, as <c>Regex.TryCreate</c> decides it: read and compiled as the library reads and
    /// compiles it, with nothing written. A code generator asks this of a pattern it will not emit code for, such as
    /// one a precompiled program holds.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="error">Why the pattern does not compile, as <c>Regex.TryCreate</c> says; or
    /// <see langword="null"/>.</param>
    /// <param name="tooLarge"><see langword="true"/> when the pattern is valid and over a limit of size, as
    /// <c>RegexPatternTooLargeException</c> reports, rather than not valid.</param>
    /// <returns><see langword="true"/> when the pattern compiles.</returns>
    public static bool TryValidate(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion, out string? error, out bool tooLarge)
    {
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        tooLarge = false;
        error = CheckSettings((int)flags, (int)edition, (int)unicodeVersion);
        if (error is not null)
        {
            return false;
        }

        try
        {
            DeepStack.Run(pattern, (flags & RegexFlags.UnicodeSets) != 0, () => CompileOnThisStack(pattern, (int)flags, edition, unicodeVersion));
            return true;
        }
        catch (RegexPatternTooLargeException e)
        {
            error = e.Message;
            tooLarge = true;
            return false;
        }
        catch (RegexParseException e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>What is wrong with the flags, the edition and the version of Unicode, or <see langword="null"/>.</summary>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    internal static string? CheckSettings(int flags, int edition, int unicodeVersion)
    {
        if ((flags & ~0xFF) != 0)
        {
            return "the flags " + flags.ToString(CultureInfo.InvariantCulture) + " are not RegexFlags";
        }

        if ((flags & (int)RegexFlags.Unicode) != 0 && (flags & (int)RegexFlags.UnicodeSets) != 0)
        {
            return "the u and v flags cannot both be given";
        }

        if (edition is not (int)RegexEdition.Current and not (int)RegexEdition.Es2020)
        {
            return "the edition " + edition.ToString(CultureInfo.InvariantCulture) + " is not a RegexEdition";
        }

        if (unicodeVersion is not ((int)RegexUnicodeVersion.Default or (int)RegexUnicodeVersion.V13 or (int)RegexUnicodeVersion.V17 or (int)RegexUnicodeVersion.V18 or (int)RegexUnicodeVersion.Latest))
        {
            return "the version " + unicodeVersion.ToString(CultureInfo.InvariantCulture) + " is not a RegexUnicodeVersion";
        }

        if (edition == (int)RegexEdition.Es2020 && (flags & (int)(RegexFlags.HasIndices | RegexFlags.UnicodeSets)) != 0)
        {
            return "ECMAScript 2020 has no d or v flag";
        }

        return null;
    }

    /// <summary>The two programs of a pattern (the bounds of a match, and every group) as runner classes.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <returns>The text of the classes, indented for a nested class.</returns>
    internal static string Runners(string pattern, int flags, RegexEdition edition = RegexEdition.Current, RegexUnicodeVersion unicodeVersion = RegexUnicodeVersion.Default)
    {
        // The reader, the compiler and the emitters go a level deeper for each level of groups (and with the v flag,
        // of classes), so a pattern that nests deeply is read on a stack of its own (see DeepStack).
        return DeepStack.Run(pattern, (flags & (int)RegexFlags.UnicodeSets) != 0, () => RunnersOnThisStack(pattern, flags, edition, unicodeVersion));
    }

    /// <summary>Appends the class that holds a regular expression, with its runner classes.</summary>
    /// <param name="sb">The text so far.</param>
    /// <param name="indent">The level of indentation of the class.</param>
    /// <param name="holder">The name of the class.</param>
    /// <param name="summary">What the class's summary calls the regular expression (XML).</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="timeoutMilliseconds">The match timeout, -1 for none, or <see langword="null"/> for the default.</param>
    /// <param name="edition">The edition.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="runners">The runner classes, from <see cref="Runners"/>.</param>
    internal static void AppendHolder(StringBuilder sb, int indent, string holder, string summary, string pattern, int flags, int? timeoutMilliseconds, int edition, int unicodeVersion, string runners)
    {
        string flagsText = "(" + Ns + "RegexFlags)" + flags.ToString(CultureInfo.InvariantCulture);
        string timeout = timeoutMilliseconds is not int ms
            ? Ns + "Regex.DefaultMatchTimeout"
            : ms < 0
            ? Ns + "Regex.InfiniteMatchTimeout"
            : "global::System.TimeSpan.FromMilliseconds(" + ms.ToString(CultureInfo.InvariantCulture) + ")";

        string settings = "(" + Ns + "RegexEdition)" + edition.ToString(CultureInfo.InvariantCulture)
            + ", (" + Ns + "RegexUnicodeVersion)" + unicodeVersion.ToString(CultureInfo.InvariantCulture) + ", ";

        Indent(sb, indent).AppendLine("/// <summary>Holds the regular expression " + summary + ".</summary>");
        Indent(sb, indent).AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Corvus.Regex.Ecma262.SourceGenerator\", \"1.0\")]");
        Indent(sb, indent).Append("private static class ").AppendLine(holder);
        Indent(sb, indent).AppendLine("{");
        Indent(sb, indent + 1).AppendLine("/// <summary>The regular expression.</summary>");
        Indent(sb, indent + 1).Append("internal static readonly " + Ns + "Regex Instance = new(")
            .Append(FormatLiteral(pattern)).Append(", ").Append(flagsText).Append(", ").Append(timeout).Append(", ").Append(settings).AppendLine("new Bounds(), new Captures(), Compiled);");
        sb.AppendLine();
        foreach (string line in runners.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                sb.AppendLine();
            }
            else
            {
                Indent(sb, indent).AppendLine(trimmed);
            }
        }

        Indent(sb, indent).AppendLine("}");
    }

    /// <summary>
    /// A pattern and its flags as a summary names them: <c>&lt;c&gt;/pattern/flags&lt;/c&gt;</c>, with the
    /// characters <see cref="FormatLiteral"/> escapes written as <c>\uXXXX</c>, since a line break would end the
    /// comment and a lone surrogate cannot be written.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flagsText">The flags' letters.</param>
    /// <returns>The XML text.</returns>
    internal static string Summary(string pattern, string flagsText)
    {
        StringBuilder sb = new(pattern.Length + flagsText.Length + 9);
        sb.Append("<c>/");
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default:
                    if (IsEscaped(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('/').Append(flagsText).Append("</c>").ToString();
    }

    /// <summary>The letters of flags, in the order JavaScript writes them.</summary>
    /// <param name="flags">The flags.</param>
    /// <returns>The letters.</returns>
    internal static string Letters(int flags)
    {
        StringBuilder sb = new();
        foreach ((RegexFlags flag, char letter) in new[] { (RegexFlags.HasIndices, 'd'), (RegexFlags.Global, 'g'), (RegexFlags.IgnoreCase, 'i'), (RegexFlags.Multiline, 'm'), (RegexFlags.DotAll, 's'), (RegexFlags.Unicode, 'u'), (RegexFlags.UnicodeSets, 'v'), (RegexFlags.Sticky, 'y') })
        {
            if ((flags & (int)flag) != 0)
            {
                sb.Append(letter);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// A string as a C# literal in quotes. Characters outside printable ASCII, other than letters and the like
    /// beyond it, are escaped, as are surrogates (a lone surrogate cannot be written to a UTF-8 file).
    /// </summary>
    /// <param name="value">The string.</param>
    /// <returns>The literal.</returns>
    internal static string FormatLiteral(string value)
    {
        StringBuilder sb = new(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\0': sb.Append("\\0"); break;
                case '\a': sb.Append("\\a"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\v': sb.Append("\\v"); break;
                default:
                    if (IsEscaped(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Whether a character is written escaped: the controls, the line and paragraph separators, the byte order mark
    /// and the surrogates (a lone surrogate cannot be written to a UTF-8 file).
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>Whether it is escaped.</returns>
    internal static bool IsEscaped(char c)
    {
        return c < 0x20 || (c >= 0x7F && c <= 0x9F) || char.IsSurrogate(c) || c is (char)0x2028 or (char)0x2029 or (char)0xFEFF;
    }

    /// <summary>Appends a level of indentation, four spaces, for each level.</summary>
    /// <param name="sb">The text so far.</param>
    /// <param name="level">The level.</param>
    /// <returns>The text.</returns>
    internal static StringBuilder Indent(StringBuilder sb, int level)
    {
        for (int k = 0; k < level; k++)
        {
            sb.Append("    ");
        }

        return sb;
    }

    private static (Program Bounds, Program Captures) CompileOnThisStack(string pattern, int flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        bool unicode = (flags & (int)(RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;
        EcmaRegexParser parser = new(
            pattern.AsSpan(),
            unicode,
            ignoreCase: (flags & (int)RegexFlags.IgnoreCase) != 0,
            multiline: (flags & (int)RegexFlags.Multiline) != 0,
            dotAll: (flags & (int)RegexFlags.DotAll) != 0,
            unicodeSets: (flags & (int)RegexFlags.UnicodeSets) != 0,
            edition: edition,
            unicodeVersion: unicodeVersion);
        EcmaRegexNode root = parser.Parse();
        return (Compiler.Compile(root, unicode, parser.GroupCount, trackAll: false), Compiler.Compile(root, unicode, parser.GroupCount, trackAll: true));
    }

    private static string RunnersOnThisStack(string pattern, int flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        (Program bounds, Program captures) = CompileOnThisStack(pattern, flags, edition, unicodeVersion);
        StringBuilder sb = new();
        if (!TreeEmitter.TryEmitRunner(sb, 1, "Bounds", bounds))
        {
            RunEmitter.EmitRunner(sb, 1, "Bounds", bounds);
        }

        sb.AppendLine();
        if (!TreeEmitter.TryEmitRunner(sb, 1, "Captures", captures))
        {
            RunEmitter.EmitRunner(sb, 1, "Captures", captures);
        }

        sb.AppendLine();
        AppendImage(sb, 1, RegexImage.Write(pattern, (RegexFlags)flags, edition, unicodeVersion));
        return sb.ToString();
    }

    /// <summary>
    /// Appends the image of the compiled regular expression (see <see cref="RegexImage"/>) as a property the holder
    /// passes to the constructor: a span over the bytes, which the compiler keeps in the assembly's data, so nothing is
    /// made when it is read. An image larger than <see cref="MostImageBytes"/> is left out, and that regular expression
    /// is compiled from its pattern when first used, as one made at run time is.
    /// </summary>
    /// <param name="sb">The output.</param>
    /// <param name="indent">The indentation.</param>
    /// <param name="image">The image.</param>
    private static void AppendImage(StringBuilder sb, int indent, byte[] image)
    {
        Indent(sb, indent).AppendLine("/// <summary>The compiled regular expression, read by the constructor in place of the pattern.</summary>");
        if (image.Length > MostImageBytes)
        {
            Indent(sb, indent).AppendLine("internal static global::System.ReadOnlySpan<byte> Compiled => default;");
            return;
        }

        Indent(sb, indent).AppendLine("internal static global::System.ReadOnlySpan<byte> Compiled => new byte[]");
        Indent(sb, indent).AppendLine("{");
        for (int k = 0; k < image.Length; k += 24)
        {
            Indent(sb, indent + 1);
            int end = Math.Min(image.Length, k + 24);
            for (int j = k; j < end; j++)
            {
                sb.Append("0x").Append(image[j].ToString("X2", CultureInfo.InvariantCulture)).Append(j + 1 < image.Length ? (j + 1 < end ? ", " : ",") : string.Empty);
            }

            sb.AppendLine();
        }

        Indent(sb, indent).AppendLine("};");
    }
}
