// <copyright file="RegexEdition.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The edition of ECMA-262 whose grammar a pattern is read with.</summary>
public enum RegexEdition
{
    /// <summary>Today's grammar, ECMAScript 2025: the default.</summary>
    Current = 0,

    /// <summary>
    /// The grammar of ECMAScript 2020 (ECMA-262, 11th edition), which JSON Schema 2020-12 names. It refuses what later
    /// editions added, and nothing else: the <c>d</c> and <c>v</c> flags, modifier groups such as <c>(?i:...)</c>, a
    /// group name used by two groups, and a property name or value that is not in the edition's tables. A pattern it
    /// accepts has the same answers as in today's grammar, under the same Unicode version.
    /// </summary>
    Es2020 = 1,
}
