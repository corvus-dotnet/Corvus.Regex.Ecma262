// <copyright file="Capture.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>A piece of the input text that a match or a group captured.</summary>
public class Capture
{
    private readonly string text;
    private string? value;

    /// <summary>Initializes a new instance of the <see cref="Capture"/> class.</summary>
    /// <param name="text">The input.</param>
    /// <param name="index">The start of the piece, or -1 for a group that did not take part.</param>
    /// <param name="length">The length of the piece.</param>
    internal Capture(string text, int index, int length)
    {
        this.text = text;
        this.Index = index < 0 ? 0 : index;
        this.Length = index < 0 ? 0 : length;
    }

    /// <summary>Gets the position of the piece in the input, in UTF-16 code units. 0 for a group that did not take part.</summary>
    public int Index { get; }

    /// <summary>Gets the length of the piece, in UTF-16 code units. 0 for a group that did not take part.</summary>
    public int Length { get; }

    /// <summary>Gets the piece. The empty string for a group that did not take part.</summary>
    public string Value => this.value ??= this.Length == 0 ? string.Empty : this.text.Substring(this.Index, this.Length);

    /// <summary>Gets the piece as a span of the input, allocating nothing.</summary>
    public ReadOnlySpan<char> ValueSpan => this.text.AsSpan(this.Index, this.Length);

    /// <summary>Gets the input.</summary>
    internal string Text => this.text;

    /// <summary>The piece.</summary>
    /// <returns><see cref="Value"/>.</returns>
    public override string ToString() => this.Value;
}
