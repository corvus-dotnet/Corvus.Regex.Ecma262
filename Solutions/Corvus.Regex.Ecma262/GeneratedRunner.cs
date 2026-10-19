// <copyright file="GeneratedRunner.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.ComponentModel;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The seam for the source generator: code it emits that runs a program's instructions as straight-line code in
/// place of the interpreter, over each encoding. Not for use by hand; the engine's types it takes are hidden from
/// editors for the same reason.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class GeneratedRunner
{
    /// <summary>Runs the program from a position over UTF-16, as <c>Matcher.Interpret</c> would.</summary>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns>Whether the program matched at the position.</returns>
    public abstract bool Run(Program p, MatchState st, ReadOnlySpan<char> input, int start, out int end);

    /// <summary>Runs the program from a position over UTF-8, as <c>Matcher.Interpret</c> would.</summary>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="start">The position.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns>Whether the program matched at the position.</returns>
    public abstract bool Run(Program p, MatchState st, ReadOnlySpan<byte> input, int start, out int end);

    /// <summary>Gets a value indicating whether the runner's <c>Scan</c> finds its own starts (the search emitted as code), so that the matcher hands it every search from the start.</summary>
    public virtual bool OwnsSearch => false;

    /// <summary>
    /// Tries the program at each start a search yields (see <see cref="CandidateScan"/>) until one matches: the
    /// generator's structured code loops here itself, so that its setting up is done once per search rather
    /// than once per start; otherwise each start is run.
    /// </summary>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="scan">The search for starts.</param>
    /// <param name="start">The start of the match.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns>Whether the program matched at some start.</returns>
    public virtual bool Scan(Program p, MatchState st, ReadOnlySpan<char> input, ref CandidateScan scan, out int start, out int end)
    {
        end = -1;
        while (Matcher.NextStart<char, Utf16Encoding>(p, st, input, ref scan, out start))
        {
            if (this.Run(p, st, input, start, out end))
            {
                return true;
            }

            if (st.Stop != MatchStop.None)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The same over UTF-8.</summary>
    /// <param name="p">The program.</param>
    /// <param name="st">The state.</param>
    /// <param name="input">The text.</param>
    /// <param name="scan">The search for starts.</param>
    /// <param name="start">The start of the match.</param>
    /// <param name="end">The end of the match.</param>
    /// <returns>Whether the program matched at some start.</returns>
    public virtual bool Scan(Program p, MatchState st, ReadOnlySpan<byte> input, ref CandidateScan scan, out int start, out int end)
    {
        end = -1;
        while (Matcher.NextStart<byte, Utf8Encoding>(p, st, input, ref scan, out start))
        {
            if (this.Run(p, st, input, start, out end))
            {
                return true;
            }

            if (st.Stop != MatchStop.None)
            {
                return false;
            }
        }

        return false;
    }
}
