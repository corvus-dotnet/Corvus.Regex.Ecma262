// <copyright file="Allocations.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// What a piece of work allocates on the thread that runs it once warm. The runtime can allocate once on that thread
/// while it is still compiling and tiering the code (a run on arm64 counted 6,432 bytes in one window and nothing
/// once warm), so the work is warmed, then counted in three windows, and the least is the answer: an allocation the
/// work itself makes is in every window.
/// </summary>
internal static class Allocations
{
    /// <summary>The least bytes allocated on this thread over the windows, and every window's count for the message.</summary>
    /// <param name="pass">One pass of the work.</param>
    /// <param name="passes">The passes in each window.</param>
    /// <param name="windows">Every window's count, for the message of a failure.</param>
    /// <returns>The least count.</returns>
    public static long Least(Action pass, int passes, out string windows)
    {
        for (int k = 0; k < 300; k++)
        {
            pass();
        }

        long least = long.MaxValue;
        long[] counts = new long[3];
        for (int w = 0; w < counts.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < passes; k++)
            {
                pass();
            }

            counts[w] = GC.GetAllocatedBytesForCurrentThread() - before;
            least = Math.Min(least, counts[w]);
        }

        windows = string.Join(", ", counts);
        return least;
    }
}