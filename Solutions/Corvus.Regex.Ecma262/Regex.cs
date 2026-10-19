// <copyright file="Regex.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Threading;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// An ECMA-262 regular expression: the grammar and the answers of ECMAScript 2025, with the shape of
/// <c>System.Text.RegularExpressions.Regex</c>. It matches UTF-16 text (<see cref="string"/> and
/// <see cref="ReadOnlySpan{T}"/> of <see cref="char"/>) and UTF-8 text (<see cref="ReadOnlySpan{T}"/> of
/// <see cref="byte"/>) natively, with no transcoding either way. An instance is immutable and safe to share
/// between threads.
/// </summary>
public sealed partial class Regex
{
    /// <summary>The match timeout that means no timeout.</summary>
    public static readonly TimeSpan InfiniteMatchTimeout = Timeout.InfiniteTimeSpan;

    private readonly Program program;
    private readonly EcmaRegexNode root;
    private readonly bool linear;
    private readonly TimeSpan limit;
    private readonly bool unicode;
    private readonly GeneratedRunner? captureRunner;
    private Program? captureProgram;
    private Dfa? dfa;
    private bool dfaTried;

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class with no flags.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <exception cref="RegexParseException">The pattern is not valid.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern)
        : this(pattern, RegexFlags.None, InfiniteMatchTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, RegexFlags flags)
        : this(pattern, flags, InfiniteMatchTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class, with the flags as <c>new RegExp(pattern, flags)</c> takes them.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags: any of <c>d</c>, <c>g</c>, <c>i</c>, <c>m</c>, <c>s</c>, <c>u</c>, <c>v</c> and <c>y</c>, each at most once.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags, or the flags are not valid.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, string flags)
        : this(pattern, ParseFlags(flags), InfiniteMatchTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class with a match timeout.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout)
        : this(pattern, flags, matchTimeout, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Regex"/> class whose programs run as the generated code given
    /// rather than the interpreter: the seam for the source generator, not for use by hand.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The limit of time on a match.</param>
    /// <param name="bounds">The generated code for the program that finds a match's bounds, or null.</param>
    /// <param name="captures">The generated code for the program that tracks every group, or null.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, GeneratedRunner? bounds, GeneratedRunner? captures)
    {
        this.captureRunner = captures;
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        ValidateFlags(flags);
        ValidateMatchTimeout(matchTimeout);
        this.Pattern = pattern;
        this.Flags = flags;
        this.MatchTimeout = matchTimeout;
        bool unicode = (flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;
        EcmaRegexParser parser = new(
            pattern.AsSpan(),
            unicode,
            ignoreCase: (flags & RegexFlags.IgnoreCase) != 0,
            multiline: (flags & RegexFlags.Multiline) != 0,
            dotAll: (flags & RegexFlags.DotAll) != 0);
        this.root = parser.Parse();
        this.linear = !parser.HasBackreferences;
        this.limit = this.linear ? TimeSpan.Zero : (matchTimeout == InfiniteMatchTimeout ? TimeSpan.Zero : matchTimeout);
        this.unicode = unicode;
        this.GroupCount = parser.GroupCount;
        this.GroupNames = parser.GroupNames;
        this.program = Compiler.Compile(this.root, unicode, this.GroupCount, trackAll: false);
        this.program.Runner = bounds;
    }

    /// <summary>Gets the pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets the flags.</summary>
    public RegexFlags Flags { get; }

    /// <summary>Gets the time a match may take before it is abandoned, or <see cref="InfiniteMatchTimeout"/>.</summary>
    public TimeSpan MatchTimeout { get; }

    /// <summary>Gets the number of capturing groups of the pattern.</summary>
    public int GroupCount { get; }

    /// <summary>Gets the flags as the string <c>RegExp.prototype.flags</c> gives: the letters in alphabetical order.</summary>
    public string FlagsString
    {
        get
        {
            Span<char> letters = stackalloc char[8];
            int n = 0;
            if ((this.Flags & RegexFlags.HasIndices) != 0)
            {
                letters[n++] = 'd';
            }

            if ((this.Flags & RegexFlags.Global) != 0)
            {
                letters[n++] = 'g';
            }

            if ((this.Flags & RegexFlags.IgnoreCase) != 0)
            {
                letters[n++] = 'i';
            }

            if ((this.Flags & RegexFlags.Multiline) != 0)
            {
                letters[n++] = 'm';
            }

            if ((this.Flags & RegexFlags.DotAll) != 0)
            {
                letters[n++] = 's';
            }

            if ((this.Flags & RegexFlags.Unicode) != 0)
            {
                letters[n++] = 'u';
            }

            if ((this.Flags & RegexFlags.UnicodeSets) != 0)
            {
                letters[n++] = 'v';
            }

            if ((this.Flags & RegexFlags.Sticky) != 0)
            {
                letters[n++] = 'y';
            }

            return letters.Slice(0, n).ToString();
        }
    }

    /// <summary>Gets a value indicating whether the pattern is matched by code point (the <c>u</c> or <c>v</c> flag) rather than by UTF-16 code unit.</summary>
    public bool IsUnicode => (this.Flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;

    /// <summary>Creates a regular expression without throwing for a pattern that is not valid.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="regex">The regular expression, or null.</param>
    /// <param name="error">Why the pattern was refused, or null.</param>
    /// <returns><see langword="true"/> when the pattern is valid in the grammar of its flags and not over a limit of size.</returns>
    public static bool TryCreate(string pattern, RegexFlags flags, out Regex? regex, out string? error)
    {
        try
        {
            regex = new Regex(pattern, flags);
            error = null;
            return true;
        }
        catch (RegexParseException e)
        {
            regex = null;
            error = e.Message;
            return false;
        }
        catch (RegexPatternTooLargeException e)
        {
            regex = null;
            error = e.Message;
            return false;
        }
    }

    /// <summary>Whether a pattern is a valid ECMA-262 pattern in the grammar of the flags.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns><see langword="true"/> when it is. A pattern over a limit of size is not reported as valid.</returns>
    public static bool IsValid(string pattern, RegexFlags flags) => TryCreate(pattern, flags, out _, out _);

    /// <summary>Whether a pattern matches anywhere in a text, with no flags.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool IsMatch(string input, string pattern) => new Regex(pattern).IsMatch(input);

    /// <summary>Whether a pattern matches anywhere in a text.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool IsMatch(string input, string pattern, RegexFlags flags) => new Regex(pattern, flags).IsMatch(input);

    /// <summary>Whether the pattern matches anywhere in a text (at its start alone with <see cref="RegexFlags.Sticky"/>).</summary>
    /// <param name="input">The text.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatch(string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return this.IsMatch(input.AsSpan(), 0);
    }

    /// <summary>Whether the pattern matches anywhere in a text from a position (at that position alone with <see cref="RegexFlags.Sticky"/>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatch(string input, int startAt)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return this.IsMatch(input.AsSpan(), startAt);
    }

    /// <summary>Whether the pattern matches anywhere in a UTF-16 text.</summary>
    /// <param name="input">The text.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatch(ReadOnlySpan<char> input) => this.IsMatch(input, 0);

    /// <summary>Whether the pattern matches anywhere in a UTF-16 text from a position.</summary>
    /// <param name="input">The text.</param>
    /// <param name="startAt">The position, in UTF-16 code units.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatch(ReadOnlySpan<char> input, int startAt)
    {
        ValidateStart(input.Length, startAt);
        startAt = this.Normalize(input, startAt);
        bool sticky = (this.Flags & RegexFlags.Sticky) != 0;
        if (startAt == 0 && !sticky)
        {
            Dfa? dfa = this.Automaton();
            if (dfa is not null)
            {
                DfaResult r = dfa.IsMatch<char, Utf16Encoding>(input);
                if (r != DfaResult.Bail)
                {
                    return r == DfaResult.Match;
                }
            }
        }

        bool ok = Matcher.IsMatch<char, Utf16Encoding>(this.program, input, startAt, sticky, this.Limit, out MatchStop stop);
        return stop == MatchStop.None ? ok : throw this.Abandoned(stop);
    }

    /// <summary>Whether the pattern matches anywhere in a UTF-8 text.</summary>
    /// <param name="utf8">The text. Bytes that are not UTF-8 are read as U+FFFD one at a time.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatch(ReadOnlySpan<byte> utf8) => this.IsMatch(utf8, 0);

    /// <summary>Whether the pattern matches anywhere in a UTF-8 text from a position.</summary>
    /// <param name="utf8">The text. Bytes that are not UTF-8 are read as U+FFFD one at a time.</param>
    /// <param name="startAt">The position, in bytes, at the start of a character.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatch(ReadOnlySpan<byte> utf8, int startAt)
    {
        ValidateStart(utf8.Length, startAt);
        startAt = this.Normalize(utf8, startAt);
        bool sticky = (this.Flags & RegexFlags.Sticky) != 0;
        if (startAt == 0 && !sticky)
        {
            Dfa? dfa = this.Automaton();
            if (dfa is not null)
            {
                DfaResult r = dfa.IsMatch<byte, Utf8Encoding>(utf8);
                if (r != DfaResult.Bail)
                {
                    return r == DfaResult.Match;
                }
            }
        }

        bool ok = Matcher.IsMatch<byte, Utf8Encoding>(this.program, utf8, startAt, sticky, this.Limit, out MatchStop stop);
        return stop == MatchStop.None ? ok : throw this.Abandoned(stop);
    }

    /// <inheritdoc/>
    public override string ToString() => this.Pattern;

    /// <summary>Reads the flags as the string <c>new RegExp(pattern, flags)</c> takes.</summary>
    /// <param name="flags">The string.</param>
    /// <returns>The flags.</returns>
    /// <exception cref="RegexParseException">A letter is not a flag, or is repeated.</exception>
    public static RegexFlags ParseFlags(string flags)
    {
        if (flags is null)
        {
            throw new ArgumentNullException(nameof(flags));
        }

        RegexFlags result = RegexFlags.None;
        for (int k = 0; k < flags.Length; k++)
        {
            RegexFlags flag = flags[k] switch
            {
                'd' => RegexFlags.HasIndices,
                'g' => RegexFlags.Global,
                'i' => RegexFlags.IgnoreCase,
                'm' => RegexFlags.Multiline,
                's' => RegexFlags.DotAll,
                'u' => RegexFlags.Unicode,
                'v' => RegexFlags.UnicodeSets,
                'y' => RegexFlags.Sticky,
                _ => throw new RegexParseException("Invalid regular expression flags", k),
            };
            if ((result & flag) != 0)
            {
                throw new RegexParseException("Invalid regular expression flags", k);
            }

            result |= flag;
        }

        return result;
    }

    private TimeSpan Limit => this.limit;

    private static void ValidateFlags(RegexFlags flags)
    {
        if ((flags & ~(RegexFlags.HasIndices | RegexFlags.Global | RegexFlags.IgnoreCase | RegexFlags.Multiline | RegexFlags.DotAll | RegexFlags.Unicode | RegexFlags.UnicodeSets | RegexFlags.Sticky)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }

        if ((flags & RegexFlags.Unicode) != 0 && (flags & RegexFlags.UnicodeSets) != 0)
        {
            throw new RegexParseException("Invalid regular expression flags", 0);
        }

        if ((flags & RegexFlags.UnicodeSets) != 0)
        {
            throw new NotSupportedException("The v flag (UnicodeSets) is not supported yet.");
        }
    }

    private static void ValidateMatchTimeout(TimeSpan matchTimeout)
    {
        if (matchTimeout != InfiniteMatchTimeout && (matchTimeout <= TimeSpan.Zero || matchTimeout.TotalMilliseconds > int.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(matchTimeout));
        }
    }

    private static void ValidateStart(int length, int startAt)
    {
        if ((uint)startAt > (uint)length)
        {
            throw new ArgumentOutOfRangeException(nameof(startAt));
        }
    }

    private RegexMatchTimeoutException Abandoned(MatchStop stop) => new(this.Pattern, this.MatchTimeout, stop == MatchStop.Memory);

    /// <summary>The program that tracks every group, for the members that report captures.</summary>
    private Program CaptureProgram()
    {
        Program? cp = this.captureProgram;
        if (cp is null)
        {
            // Simplify changes the tree in place, and did so when the first program was compiled; compiling the
            // same tree again gives the same program apart from the groups it tracks.
            cp = Compiler.Compile(this.root, this.unicode, this.GroupCount, trackAll: true);
            cp.Runner = this.captureRunner;
            Interlocked.CompareExchange(ref this.captureProgram, cp, null);
            cp = this.captureProgram;
        }

        return cp;
    }

    private Dfa? Automaton()
    {
        if (!this.dfaTried)
        {
            if (this.linear)
            {
                Dfa? built = Dfa.Build(this.root);
                Interlocked.CompareExchange(ref this.dfa, built, null);
            }

            this.dfaTried = true;
        }

        return this.dfa;
    }
}
