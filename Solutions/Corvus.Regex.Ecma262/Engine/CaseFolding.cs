// <copyright file="CaseFolding.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of the Canonicalize function of fold.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself fold.go).
// The parser applies the equivalence to every literal and class as it reads them, through UnicodeTables, so a
// case-insensitive group is matched with plain classes. Only a backreference needs the function when matching.

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Case-insensitive matching of a backreference.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class CaseFolding
{
    /// <summary>
    /// The Canonicalize function of ECMA-262 for case-insensitive matching. With the u flag it is Unicode simple
    /// case folding. With no flag it is the single character uppercase mapping of a UTF-16 code unit, which never
    /// maps a character outside ASCII into ASCII.
    /// </summary>
    /// <param name="r">The code point.</param>
    /// <param name="unicode">Whether the pattern was read with the u flag grammar.</param>
    /// <returns>The canonical code point.</returns>
    public static int Canonicalize(int r, bool unicode) => UnicodeTables.V17.Canonicalize(r, unicode);

    /// <summary>
    /// The Canonicalize function of ECMA-262 for case-insensitive matching, with the case mappings of a version of
    /// Unicode. With the u flag it is Unicode simple case folding. With no flag it is the single character uppercase
    /// mapping of a UTF-16 code unit, which never maps a character outside ASCII into ASCII.
    /// </summary>
    /// <param name="r">The code point.</param>
    /// <param name="unicode">Whether the pattern was read with the u flag grammar.</param>
    /// <param name="version">The version of Unicode the pattern was read with.</param>
    /// <returns>The canonical code point.</returns>
    public static int Canonicalize(int r, bool unicode, RegexUnicodeVersion version) => UnicodeTables.For(version).Canonicalize(r, unicode);
}
