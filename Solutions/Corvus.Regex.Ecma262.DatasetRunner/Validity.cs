// <copyright file="Validity.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The zero-allocation reader of the grammar (<see cref="Regex.IsValidPattern(ReadOnlySpan{char}, RegexFlags, RegexEdition, RegexUnicodeVersion)"/>)
/// judged against the data's verdict for each grammar: it answers whether a pattern is valid as V8 does, over the
/// pattern as UTF-16 and, where it has one, as UTF-8. It asks the grammar alone, so a pattern the engine refuses as
/// too large is still one it finds valid when the data does.
/// </summary>
internal static class Validity
{
    private static readonly UTF8Encoding Strict = new(false, true);

    /// <summary>Judges the reader on one pattern in one grammar: the number of its wrong verdicts, each appended.</summary>
    public static int Check(string file, string pattern, string modeFlags, bool expectedValid, Action<string> wrong)
    {
        RegexFlags flags = Regex.ParseFlags(modeFlags);
        int wrongCount = 0;
        if (Regex.IsValidPattern(pattern.AsSpan(), flags, Factory.Edition, Factory.UnicodeVersion) != expectedValid)
        {
            wrongCount++;
            wrong($"READER\t{file}\t/{pattern}/{modeFlags}\tutf16\texpected {(expectedValid ? "valid" : "not valid")}");
        }

        byte[] utf8;
        try
        {
            utf8 = Strict.GetBytes(pattern);
        }
        catch (EncoderFallbackException)
        {
            // A lone surrogate has no UTF-8; there the reader refuses the bytes, and UTF-16 has been judged.
            return wrongCount;
        }

        if (Regex.IsValidPattern(utf8, flags, Factory.Edition, Factory.UnicodeVersion) != expectedValid)
        {
            wrongCount++;
            wrong($"READER\t{file}\t/{pattern}/{modeFlags}\tutf8\texpected {(expectedValid ? "valid" : "not valid")}");
        }

        return wrongCount;
    }
}
