// <copyright file="EcmaRegexNode.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// This file is the syntax tree of the Corvus.JsonSchema .NET translator (src/Corvus.Text.Json.CodeGeneration/EcmaRegexParser.cs),
// without what only the translator read: the order a class wrote its members in, the General_Category values .NET has
// a class for, and the measurements of the tree.

using System;
using System.Collections.Generic;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The kinds of node in the syntax tree of an ECMA-262 pattern.</summary>
internal enum EcmaRegexNodeKind
{
    /// <summary>Nothing: an empty alternative.</summary>
    Empty,

    /// <summary>One character.</summary>
    Char,

    /// <summary>One character of a set.</summary>
    Set,

    /// <summary>Terms one after another.</summary>
    Cat,

    /// <summary>Alternatives.</summary>
    Alt,

    /// <summary>A group, capturing or not.</summary>
    Group,

    /// <summary>A quantified term.</summary>
    Repeat,

    /// <summary><c>^</c> outside a group with the m modifier.</summary>
    Bol,

    /// <summary><c>$</c> outside a group with the m modifier.</summary>
    Eol,

    /// <summary><c>^</c> inside a group with the m modifier.</summary>
    LineStart,

    /// <summary><c>$</c> inside a group with the m modifier.</summary>
    LineEnd,

    /// <summary><c>\b</c>.</summary>
    WordBoundary,

    /// <summary><c>\B</c>.</summary>
    NotWordBoundary,

    /// <summary>A lookahead or a lookbehind.</summary>
    Look,

    /// <summary>A backreference.</summary>
    Backref,
}

/// <summary>One element of the syntax tree of an ECMA-262 pattern.</summary>
internal sealed class EcmaRegexNode(EcmaRegexNodeKind kind)
{
    /// <summary>The max of a quantifier with no upper bound.</summary>
    public const int Unbounded = -1;

    private static readonly EcmaRegexNode[] NoNodes = [];

    /// <summary>Gets the kind of the node.</summary>
    public EcmaRegexNodeKind Kind { get; } = kind;

    /// <summary>Gets or sets the character of a Char: a code point with the u flag grammar and a UTF-16 code unit without.</summary>
    public int CodePoint { get; set; }

    /// <summary>Gets or sets the set of a Set: inclusive ranges, sorted, that neither overlap nor touch.</summary>
    public int[] Set { get; set; } = [];

    /// <summary>Gets or sets the children of Cat and Alt, and the one child of Group, Repeat and Look.</summary>
    public EcmaRegexNode[] Subs { get; set; } = NoNodes;

    /// <summary>Gets or sets the least count of a Repeat.</summary>
    public int Min { get; set; }

    /// <summary>Gets or sets the greatest count of a Repeat, or <see cref="Unbounded"/>.</summary>
    public int Max { get; set; }

    /// <summary>Gets or sets a value indicating whether a Repeat is lazy.</summary>
    public bool Lazy { get; set; }

    /// <summary>Gets or sets the number of the capturing group of a Group, counted from 1 as ECMA-262 does, or 0.</summary>
    public int Group { get; set; }

    /// <summary>
    /// Gets or sets the groups a Backref names. A name can belong to several groups in separate alternatives, of
    /// which at most one has taken part at any time.
    /// </summary>
    public int[] Groups { get; set; } = [];

    /// <summary>Gets or sets the group name of a named Backref until it is resolved.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets a value indicating whether a Backref, WordBoundary or NotWordBoundary is inside a case-insensitive group.</summary>
    public bool Fold { get; set; }

    /// <summary>Gets or sets a value indicating whether a Look is a lookbehind.</summary>
    public bool Behind { get; set; }

    /// <summary>Gets or sets a value indicating whether a Look is negative.</summary>
    public bool Negative { get; set; }
}
