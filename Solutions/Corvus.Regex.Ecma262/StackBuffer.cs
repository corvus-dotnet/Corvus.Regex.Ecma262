// <copyright file="StackBuffer.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Buffers;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The largest buffer, in elements, that is put on the stack. A buffer whose length a pattern or a text decides (the
/// slots of the groups, of which a pattern may make some hundreds of thousands) is on the stack only up to this, and
/// rented from <see cref="ArrayPool{T}.Shared"/> beyond it, so that no pattern can overflow the stack of the thread
/// that matches and a match still allocates nothing.
/// </summary>
internal static class StackBuffer
{
    /// <summary>The largest length of a buffer on the stack.</summary>
    public const int MaxLength = 256;

    /// <summary>
    /// A buffer rented from the shared pool when its length is over <see cref="MaxLength"/>, and nothing otherwise;
    /// disposing of it returns what was rented. A buffer the pool lends is not cleared, so it is used only where every
    /// element is written before it is read. The pattern at each use is
    /// <c>using Pooled&lt;int&gt; pool = new(n); Span&lt;int&gt; s = n &lt;= MaxLength ? stackalloc int[n] : pool.Span;</c>.
    /// </summary>
    /// <typeparam name="T">The element.</typeparam>
    public readonly struct Pooled<T> : IDisposable
    {
        private readonly T[]? array;
        private readonly int length;

        /// <summary>Initializes a new instance of the <see cref="Pooled{T}"/> struct.</summary>
        /// <param name="length">The length of the buffer.</param>
        public Pooled(int length)
        {
            this.array = length > MaxLength ? ArrayPool<T>.Shared.Rent(length) : null;
            this.length = length;
        }

        /// <summary>Gets the rented buffer, cut to its length; empty when nothing was rented.</summary>
        public Span<T> Span => this.array.AsSpan(0, this.array is null ? 0 : this.length);

        /// <summary>Returns the rented buffer to the pool.</summary>
        public void Dispose()
        {
            if (this.array is T[] rented)
            {
                ArrayPool<T>.Shared.Return(rented);
            }
        }
    }
}
