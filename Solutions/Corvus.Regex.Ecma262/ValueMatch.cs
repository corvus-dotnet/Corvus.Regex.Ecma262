// <copyright file="ValueMatch.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Where a match is in a text, with nothing allocated: the position and the length, in the units of the text.</summary>
public readonly struct ValueMatch : IEquatable<ValueMatch>
{
    internal ValueMatch(int index, int length)
    {
        this.Index = index;
        this.Length = length;
    }

    /// <summary>Gets the position of the match, in the units of the text.</summary>
    public int Index { get; }

    /// <summary>Gets the length of the match, in the units of the text.</summary>
    public int Length { get; }

    /// <summary>Whether two values are the same.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public static bool operator ==(ValueMatch left, ValueMatch right) => left.Equals(right);

    /// <summary>Whether two values differ.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    public static bool operator !=(ValueMatch left, ValueMatch right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ValueMatch other) => this.Index == other.Index && this.Length == other.Length;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ValueMatch other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (this.Index * 397) ^ this.Length;
}

/// <summary>What one group captured, with nothing allocated: the position and the length in the units of the text, or that the group did not take part.</summary>
public readonly struct ValueCapture : IEquatable<ValueCapture>
{
    internal ValueCapture(int index, int length)
    {
        this.Index = index;
        this.Length = length;
    }

    /// <summary>Gets the position of the capture, in the units of the text, or -1 when the group did not take part.</summary>
    public int Index { get; }

    /// <summary>Gets the length of the capture, in the units of the text, or 0 when the group did not take part.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the group took part in the match.</summary>
    public bool Success => this.Index >= 0;

    /// <summary>Whether two values are the same.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public static bool operator ==(ValueCapture left, ValueCapture right) => left.Equals(right);

    /// <summary>Whether two values differ.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    public static bool operator !=(ValueCapture left, ValueCapture right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ValueCapture other) => this.Index == other.Index && this.Length == other.Length;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ValueCapture other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (this.Index * 397) ^ this.Length;
}
