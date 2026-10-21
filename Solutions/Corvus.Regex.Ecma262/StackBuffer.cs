// <copyright file="StackBuffer.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The largest buffer, in elements, that is put on the stack. A buffer whose length a pattern or a text decides (the
/// slots of the groups, of which a pattern may make some hundreds of thousands) is on the stack only up to this, and
/// on the heap beyond it, so that no pattern can overflow the stack of the thread that matches.
/// </summary>
internal static class StackBuffer
{
    /// <summary>The largest length of a buffer on the stack.</summary>
    public const int MaxLength = 256;
}
