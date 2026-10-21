// <copyright file="DeepStack.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// Runs the work that goes a level deeper for each level of a pattern's groups and classes (the reader, the
/// compilers and the walks of the syntax tree) where that depth cannot overflow the stack.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EcmaRegexParser.MaxNesting"/> allows two hundred levels, and the reader and the compilers take some
/// kilobytes of stack for each, which is more than a thread can be relied on to hold: a thread of a host that embeds
/// the engine may have a few hundred kilobytes, and the main thread on Windows has 1 MB. A stack overflow ends the
/// process, and no caller can catch it. So a pattern that opens few groups and classes is read on the stack of the
/// thread that asks, and any other on a thread of its own, with a stack for the deepest nesting the limits allow.
/// Only the pages the work touches are used.
/// </para>
/// <para>
/// Where no thread can be started (a runtime without threads), the work runs where it is asked, and the recursive
/// steps of the reader and the compiler check that there is stack left (<see cref="Ensure"/>): a pattern too deep
/// for the stack there is refused as too large, which a caller can catch, rather than overflowing it.
/// </para>
/// </remarks>
internal static class DeepStack
{
    /// <summary>The number of groups and classes a pattern may open and still be read on the stack of the thread that asks.</summary>
    public const int Shallow = 32;

    /// <summary>The stack of the thread that reads a pattern with more groups and classes than that.</summary>
    public const int StackSize = 64 << 20;

    /// <summary>
    /// Gets a value indicating whether a pattern opens few enough groups and classes to be read on the stack of the
    /// thread that asks. A class of the v flag's grammar may hold a class, and the reader goes a level deeper for
    /// each, as it does for a group, so a <c>[</c> counts as a <c>(</c> does. In the other grammars a <c>[</c> opens
    /// no level, and counting it only sends a pattern with many of them to a thread of its own, which is safe.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>Whether the pattern is shallow.</returns>
    public static bool IsShallow(ReadOnlySpan<char> pattern)
    {
        int opened = 0;
        for (int k = 0; k < pattern.Length; k++)
        {
            char c = pattern[k];
            if ((c == '(' || c == '[') && ++opened > Shallow)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs work for a pattern, on a thread with a deep stack unless the pattern is shallow.</summary>
    /// <typeparam name="T">The result of the work.</typeparam>
    /// <param name="pattern">The pattern.</param>
    /// <param name="work">The work.</param>
    /// <returns>The result of the work.</returns>
    /// <exception cref="RegexPatternTooLargeException">The stack was too shallow for the pattern.</exception>
    public static T Run<T>(string pattern, Func<T> work)
    {
        if (IsShallow(pattern.AsSpan()))
        {
            return RunHere(work);
        }

        T result = default!;
        ExceptionDispatchInfo? error = null;
        Thread thread;
        try
        {
            thread = new Thread(
                () =>
                {
                    try
                    {
                        result = RunHere(work);
                    }
                    catch (Exception e)
                    {
                        error = ExceptionDispatchInfo.Capture(e);
                    }
                },
                StackSize);
            thread.Start();
        }
        catch (Exception e) when (e is PlatformNotSupportedException or NotSupportedException or OutOfMemoryException or ThreadStartException)
        {
            // No thread could be started: the work runs here, and refuses a pattern too deep for this stack.
            return RunHere(work);
        }

        thread.Join();
        error?.Throw();
        return result;
    }

    /// <summary>
    /// Checks that the stack has room for another level of the reader or the compiler, and refuses the pattern as
    /// too large when it has not.
    /// </summary>
    /// <exception cref="InsufficientExecutionStackException">The stack has too little room left.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Ensure() => RuntimeHelpers.EnsureSufficientExecutionStack();

    /// <summary>
    /// Runs work for a pattern on the stack of the thread that asks, refusing the pattern as too large where that
    /// stack has too little room for it.
    /// </summary>
    /// <typeparam name="T">The result of the work.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>The result of the work.</returns>
    /// <exception cref="RegexPatternTooLargeException">The stack was too shallow for the pattern.</exception>
    internal static T RunHere<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (InsufficientExecutionStackException)
        {
            throw new RegexPatternTooLargeException("Its groups and classes nest too deep for the stack of the thread that reads it.");
        }
    }
}
