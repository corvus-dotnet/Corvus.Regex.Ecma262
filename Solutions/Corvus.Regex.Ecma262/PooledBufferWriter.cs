// <copyright file="PooledBufferWriter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Buffers;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>A buffer writer over pooled memory, for a result that is copied out once (the string of a replacement).</summary>
/// <typeparam name="T">The unit.</typeparam>
internal sealed class PooledBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private T[] buffer;
    private int written;

    /// <summary>Initializes a new instance of the <see cref="PooledBufferWriter{T}"/> class.</summary>
    /// <param name="capacity">The capacity to start with.</param>
    public PooledBufferWriter(int capacity)
    {
        this.buffer = ArrayPool<T>.Shared.Rent(Math.Max(capacity, 16));
    }

    /// <summary>Gets the length written.</summary>
    public int WrittenLength => this.written;

    /// <summary>Gets what was written.</summary>
    public ReadOnlySpan<T> WrittenSpan => this.buffer.AsSpan(0, this.written);

    /// <inheritdoc/>
    public void Advance(int count)
    {
        this.written += count;
    }

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

    /// <inheritdoc/>
    public void Dispose()
    {
        T[] b = this.buffer;
        this.buffer = [];
        if (b.Length != 0)
        {
            ArrayPool<T>.Shared.Return(b);
        }
    }

    /// <summary>What was written, as a string (for a writer of characters).</summary>
    /// <returns>The string.</returns>
    public override string ToString()
    {
        if (typeof(T) == typeof(char))
        {
            char[] chars = (char[])(object)this.buffer;
            return new string(chars, 0, this.written);
        }

        return base.ToString() ?? string.Empty;
    }

    private void Ensure(int sizeHint)
    {
        int need = Math.Max(sizeHint, 1);
        if (this.buffer.Length - this.written >= need)
        {
            return;
        }

        T[] bigger = ArrayPool<T>.Shared.Rent(Math.Max(this.buffer.Length * 2, this.written + need));
        this.buffer.AsSpan(0, this.written).CopyTo(bigger);
        ArrayPool<T>.Shared.Return(this.buffer);
        this.buffer = bigger;
    }
}
