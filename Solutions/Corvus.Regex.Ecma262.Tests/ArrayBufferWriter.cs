// <copyright file="ArrayBufferWriter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

#if NETFRAMEWORK
using System.Buffers;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The buffer writer the span tests write into: .NET Framework has no <c>System.Buffers.ArrayBufferWriter&lt;T&gt;</c>,
/// so this one, of the same name and shape, stands in for it on that target.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ArrayBufferWriter<T> : IBufferWriter<T>
{
    private T[] buffer = new T[256];
    private int written;

    /// <summary>Gets what has been written.</summary>
    public ReadOnlySpan<T> WrittenSpan => this.buffer.AsSpan(0, this.written);

    /// <inheritdoc/>
    public void Advance(int count) => this.written += count;

    /// <inheritdoc/>
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        this.Ensure(sizeHint);
        return this.buffer.AsMemory(this.written);
    }

    /// <inheritdoc/>
    public Span<T> GetSpan(int sizeHint = 0)
    {
        this.Ensure(sizeHint);
        return this.buffer.AsSpan(this.written);
    }

    private void Ensure(int sizeHint)
    {
        int need = this.written + Math.Max(sizeHint, 1);
        if (need > this.buffer.Length)
        {
            Array.Resize(ref this.buffer, Math.Max(need, this.buffer.Length * 2));
        }
    }
}
#endif