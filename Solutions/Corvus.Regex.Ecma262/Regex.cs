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

    /// <summary>
    /// The name of the <see cref="AppContext"/> data that sets the default match timeout, which
    /// <c>System.Text.RegularExpressions</c> reads too.
    /// </summary>
    private const string DefaultMatchTimeoutKey = "REGEX_DEFAULT_MATCH_TIMEOUT";

    /// <summary>The default match timeout in ticks once it has been read, or <see cref="long.MinValue"/> before.</summary>
    private static long defaultMatchTimeoutTicks = long.MinValue;

    private readonly Program program;
    private readonly EcmaRegexNode? root;
    private readonly bool linear;
    private readonly TimeSpan limit;
    private readonly bool unicode;
    private readonly GeneratedRunner? captureRunner;
    private Program? captureProgram;
    private Dfa? dfa;
    private bool dfaTried;

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class with no flags and the <see cref="DefaultMatchTimeout"/>.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <exception cref="RegexParseException">The pattern is not valid.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern)
        : this(pattern, RegexFlags.None, DefaultMatchTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class with the <see cref="DefaultMatchTimeout"/>.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, RegexFlags flags)
        : this(pattern, flags, DefaultMatchTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Regex"/> class, with the flags as <c>new RegExp(pattern, flags)</c> takes them and the <see cref="DefaultMatchTimeout"/>.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags: any of <c>d</c>, <c>g</c>, <c>i</c>, <c>m</c>, <c>s</c>, <c>u</c>, <c>v</c> and <c>y</c>, each at most once.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags, or the flags are not valid.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, string flags)
        : this(pattern, ParseFlags(flags), DefaultMatchTimeout)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Regex"/> class with a match timeout: the time each match may take
    /// before it is abandoned with <see cref="RegexMatchTimeoutException"/>. A match with a timeout is also abandoned,
    /// with <see cref="RegexMatchTimeoutException.ExceededMemory"/>, when its backtracking stack reaches its cap.
    /// Every member that matches gives each of its matches the timeout, whatever the pattern. Without one a match has
    /// no limit of time or of memory, so a pattern or a text from outside should be given one, here or for every
    /// instance with <see cref="DefaultMatchTimeout"/>.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout)
        : this(pattern, flags, matchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Regex"/> class with a match timeout, the edition of the grammar
    /// and the version of Unicode, which are independent of each other. A validator of JSON Schema 2020-12, which names
    /// ECMAScript 2020, takes <see cref="RegexEdition.Es2020"/>.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags and edition.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
        : this(pattern, flags, matchTimeout, edition, unicodeVersion, null, null)
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
        : this(pattern, flags, matchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, bounds, captures)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Regex"/> class whose programs run as the generated code given
    /// rather than the interpreter: the seam for the source generator, not for use by hand.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The limit of time on a match.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="bounds">The generated code for the program that finds a match's bounds, or null.</param>
    /// <param name="captures">The generated code for the program that tracks every group, or null.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion, GeneratedRunner? bounds, GeneratedRunner? captures)
        : this(pattern, flags, matchTimeout, edition, unicodeVersion, bounds, captures, default, RegexBuildOptions.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Regex"/> class with a match timeout, the edition of the grammar,
    /// the version of Unicode, and when what its matches use is built: with <see cref="RegexBuildOptions.Eager"/>,
    /// in the constructor, so that the first match costs what a later one does.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <param name="buildOptions">When what the matches use is built.</param>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar of its flags and edition.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The build options are not <see cref="RegexBuildOptions"/>.</exception>
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion, RegexBuildOptions buildOptions)
        : this(pattern, flags, matchTimeout, edition, unicodeVersion, null, null, default, buildOptions)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Regex"/> class from the compiled form the source generator wrote,
    /// with its programs run as the generated code given: the seam for the source generator, not for use by hand.
    /// Nothing is parsed or built when the image was written by this version of the engine for these settings;
    /// otherwise the image is ignored and the pattern is parsed, with the same answers.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The limit of time on a match.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <param name="bounds">The generated code for the program that finds a match's bounds, or null.</param>
    /// <param name="captures">The generated code for the program that tracks every group, or null.</param>
    /// <param name="compiled">The image of the compiled regular expression, or empty.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion, GeneratedRunner? bounds, GeneratedRunner? captures, ReadOnlySpan<byte> compiled)
        : this(pattern, flags, matchTimeout, edition, unicodeVersion, bounds, captures, compiled, RegexBuildOptions.None)
    {
    }

    private Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion, GeneratedRunner? bounds, GeneratedRunner? captures, ReadOnlySpan<byte> compiled, RegexBuildOptions buildOptions)
    {
        this.captureRunner = captures;
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        ValidateFlags(flags);
        ValidateMatchTimeout(matchTimeout);
        ValidateEdition(flags, edition);
        ValidateBuildOptions(buildOptions);
        this.UnicodeVersion = UnicodeTables.Resolve(unicodeVersion);
        this.Pattern = pattern;
        this.Flags = flags;
        this.MatchTimeout = matchTimeout;
        this.Edition = edition;
        this.unicode = (flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;

        // Every pattern has its timeout. One without a backreference takes time in proportion to its size times the
        // text, but a count is not size: (?:a?){2147483647} keeps a choice for each of its iterations.
        this.limit = matchTimeout == InfiniteMatchTimeout ? TimeSpan.Zero : matchTimeout;
        if (!compiled.IsEmpty && RegexImage.TryRead(compiled, flags, edition, this.UnicodeVersion, out RegexImage.Parts parts))
        {
            // Everything the first matches would build is in the image, so nothing is kept to build it from.
            this.root = null;
            this.linear = parts.Linear;
            this.GroupCount = parts.GroupCount;
            this.GroupNames = parts.GroupNames;
            this.program = parts.Bounds;
            this.captureProgram = parts.Captures;
            this.captureProgram.Runner = captures;
            this.dfa = parts.Dfa;
            this.dfaTried = true;
            this.ReadFromImage = true;
        }
        else
        {
            (EcmaRegexParser parser, EcmaRegexNode root, Program program) = RegexImage.Compile(pattern, flags, edition, unicodeVersion);
            this.root = root;
            this.linear = !parser.HasBackreferences;
            this.GroupCount = parser.GroupCount;
            this.GroupNames = parser.GroupNames;
            this.program = program;
        }

        this.program.Runner = bounds;
        if ((buildOptions & RegexBuildOptions.Eager) != 0)
        {
            this.BuildEverything();
        }
    }

    /// <summary>Gets the pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets a value indicating whether the regular expression was read from an image rather than compiled from its pattern (see <see cref="RegexImage"/>).</summary>
    internal bool ReadFromImage { get; }

    /// <summary>Gets the flags.</summary>
    public RegexFlags Flags { get; }

    /// <summary>Gets the edition of ECMA-262 whose grammar the pattern was read with.</summary>
    public RegexEdition Edition { get; }

    /// <summary>
    /// Gets the version of Unicode whose character database the pattern was read with, resolved: never
    /// <see cref="RegexUnicodeVersion.Default"/> or <see cref="RegexUnicodeVersion.Latest"/>.
    /// </summary>
    public RegexUnicodeVersion UnicodeVersion { get; }

    /// <summary>
    /// Gets the match timeout of every instance made without one of its own: the <c>REGEX_DEFAULT_MATCH_TIMEOUT</c>
    /// data of the <see cref="AppContext"/>, which <c>System.Text.RegularExpressions</c> reads too, as a
    /// <see cref="TimeSpan"/>, or <see cref="InfiniteMatchTimeout"/> when it is not set. It is read once, when it is
    /// first needed, so a program sets it when it starts (with <c>AppContext.SetData</c>, or
    /// <c>AppDomain.CurrentDomain.SetData</c> on .NET Framework, or in its runtimeconfig.json).
    /// </summary>
    /// <exception cref="InvalidCastException">The data is not a <see cref="TimeSpan"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The data is not a valid match timeout.</exception>
    public static TimeSpan DefaultMatchTimeout
    {
        get
        {
            long ticks = Volatile.Read(ref defaultMatchTimeoutTicks);
            if (ticks == long.MinValue)
            {
                ticks = ReadDefaultMatchTimeout(AppContext.GetData(DefaultMatchTimeoutKey)).Ticks;
                Volatile.Write(ref defaultMatchTimeoutTicks, ticks);
            }

            return TimeSpan.FromTicks(ticks);
        }
    }

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
        => TryCreate(pattern, flags, DefaultMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, out regex, out error);

    /// <summary>Creates a regular expression with a match timeout without throwing for a pattern that is not valid.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <param name="regex">The regular expression, or null.</param>
    /// <param name="error">Why the pattern was refused, or null.</param>
    /// <returns><see langword="true"/> when the pattern is valid in the grammar of its flags and not over a limit of size.</returns>
    public static bool TryCreate(string pattern, RegexFlags flags, TimeSpan matchTimeout, out Regex? regex, out string? error)
        => TryCreate(pattern, flags, matchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, out regex, out error);

    /// <summary>
    /// Creates a regular expression with a match timeout, the edition of the grammar and the version of Unicode,
    /// without throwing for a pattern that is not valid.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <param name="regex">The regular expression, or null.</param>
    /// <param name="error">Why the pattern was refused, or null.</param>
    /// <returns><see langword="true"/> when the pattern is valid in the grammar of its flags and edition and not over a limit of size.</returns>
    public static bool TryCreate(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion, out Regex? regex, out string? error)
        => TryCreate(pattern, flags, matchTimeout, edition, unicodeVersion, RegexBuildOptions.None, out regex, out error);

    /// <summary>
    /// Creates a regular expression with a match timeout, the edition of the grammar, the version of Unicode and when
    /// what its matches use is built, without throwing for a pattern that is not valid.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeout">The time a match may take before it is abandoned with <see cref="RegexMatchTimeoutException"/>, or <see cref="InfiniteMatchTimeout"/>.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <param name="buildOptions">When what the matches use is built (see <see cref="RegexBuildOptions"/>).</param>
    /// <param name="regex">The regular expression, or null.</param>
    /// <param name="error">Why the pattern was refused, or null.</param>
    /// <returns><see langword="true"/> when the pattern is valid in the grammar of its flags and edition and not over a limit of size.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The build options are not <see cref="RegexBuildOptions"/>.</exception>
    public static bool TryCreate(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion unicodeVersion, RegexBuildOptions buildOptions, out Regex? regex, out string? error)
    {
        try
        {
            regex = new Regex(pattern, flags, matchTimeout, edition, unicodeVersion, buildOptions);
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

    /// <summary>Whether a pattern is a valid ECMA-262 pattern in the grammar of the flags and the edition, under a version of Unicode.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <returns><see langword="true"/> when it is. A pattern over a limit of size is not reported as valid.</returns>
    public static bool IsValid(string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
        => TryCreate(pattern, flags, InfiniteMatchTimeout, edition, unicodeVersion, out _, out _);

    /// <summary>
    /// Whether a pattern is valid in the grammar of ECMA-262 for the flags, decided with nothing allocated. It asks
    /// the grammar only: unlike <see cref="IsValid(string, RegexFlags)"/>, which compiles the pattern, it does not apply
    /// this engine's limits of size, so a pattern too large to compile is valid here.
    /// </summary>
    /// <param name="pattern">The pattern, as UTF-16.</param>
    /// <param name="flags">The flags: <see cref="RegexFlags.Unicode"/> or <see cref="RegexFlags.UnicodeSets"/> choose the
    /// grammar, and the others do not change it.</param>
    /// <returns><see langword="true"/> when it is; <see langword="false"/> too when the flags are not valid together.</returns>
    public static bool IsValidPattern(ReadOnlySpan<char> pattern, RegexFlags flags)
        => IsValidPattern(pattern, flags, RegexEdition.Current, RegexUnicodeVersion.Default);

    /// <summary>
    /// Whether a pattern is valid in the grammar of ECMA-262 for the flags, of an edition and under a version of
    /// Unicode, decided with nothing allocated (see <see cref="IsValidPattern(ReadOnlySpan{char}, RegexFlags)"/>).
    /// </summary>
    /// <param name="pattern">The pattern, as UTF-16.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <returns><see langword="true"/> when it is; <see langword="false"/> too when the flags, the edition or the
    /// version are not valid together.</returns>
    public static bool IsValidPattern(ReadOnlySpan<char> pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        return AreValidSettings(flags, edition, unicodeVersion)
            && EcmaRegexValidator.IsValid(pattern, (flags & RegexFlags.Unicode) != 0, (flags & RegexFlags.UnicodeSets) != 0, edition, unicodeVersion);
    }

    /// <summary>
    /// Whether a UTF-8 pattern is valid in the grammar of ECMA-262 for the flags, decided with nothing allocated and
    /// with no transcoding (see <see cref="IsValidPattern(ReadOnlySpan{char}, RegexFlags)"/>). Bytes that are not
    /// UTF-8 are not a pattern.
    /// </summary>
    /// <param name="utf8Pattern">The pattern, as UTF-8.</param>
    /// <param name="flags">The flags.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValidPattern(ReadOnlySpan<byte> utf8Pattern, RegexFlags flags)
        => IsValidPattern(utf8Pattern, flags, RegexEdition.Current, RegexUnicodeVersion.Default);

    /// <summary>
    /// Whether a UTF-8 pattern is valid in the grammar of ECMA-262 for the flags, of an edition and under a version
    /// of Unicode, decided with nothing allocated and with no transcoding. Bytes that are not UTF-8 are not a pattern.
    /// </summary>
    /// <param name="utf8Pattern">The pattern, as UTF-8.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="edition">The edition of ECMA-262 whose grammar the pattern is read with.</param>
    /// <param name="unicodeVersion">The version of Unicode whose character database the pattern is read with.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValidPattern(ReadOnlySpan<byte> utf8Pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        return AreValidSettings(flags, edition, unicodeVersion)
            && EcmaRegexValidator.IsValid(utf8Pattern, (flags & RegexFlags.Unicode) != 0, (flags & RegexFlags.UnicodeSets) != 0, edition, unicodeVersion);
    }

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
    /// <param name="utf8">The text, which must be UTF-8.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    /// <exception cref="ArgumentException">The text is not UTF-8.</exception>
    public bool IsMatch(ReadOnlySpan<byte> utf8)
    {
        Utf8Check.Check(utf8, nameof(utf8));
        return this.IsMatchUnchecked(utf8);
    }

    /// <summary>Whether the pattern matches anywhere in a UTF-8 text.</summary>
    /// <remarks>The text is not checked to be UTF-8: this is for text its caller knows to be UTF-8, such as the strings
    /// of parsed JSON, or a text checked once and matched many times. Bytes that are not UTF-8 are read as U+FFFD one
    /// at a time; the member without <c>Unchecked</c> refuses them.</remarks>
    /// <param name="utf8">The text. Bytes that are not UTF-8 are read as U+FFFD one at a time.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatchUnchecked(ReadOnlySpan<byte> utf8) => this.IsMatchUnchecked(utf8, 0);

    /// <summary>Whether the pattern matches anywhere in a UTF-8 text from a position.</summary>
    /// <param name="utf8">The text, which must be UTF-8.</param>
    /// <param name="startAt">The position, in bytes, at the start of a character.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    /// <exception cref="ArgumentException">The text is not UTF-8.</exception>
    public bool IsMatch(ReadOnlySpan<byte> utf8, int startAt)
    {
        Utf8Check.Check(utf8, nameof(utf8));
        return this.IsMatchUnchecked(utf8, startAt);
    }

    /// <summary>Whether the pattern matches anywhere in a UTF-8 text from a position.</summary>
    /// <remarks>The text is not checked to be UTF-8: this is for text its caller knows to be UTF-8, such as the strings
    /// of parsed JSON, or a text checked once and matched many times. Bytes that are not UTF-8 are read as U+FFFD one
    /// at a time; the member without <c>Unchecked</c> refuses them.</remarks>
    /// <param name="utf8">The text. Bytes that are not UTF-8 are read as U+FFFD one at a time.</param>
    /// <param name="startAt">The position, in bytes, at the start of a character.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public bool IsMatchUnchecked(ReadOnlySpan<byte> utf8, int startAt)
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

    }

    /// <summary>
    /// Whether flags, an edition and a version of Unicode go together, as <see cref="ValidateFlags"/> and
    /// <see cref="ValidateEdition"/> decide it, with nothing thrown.
    /// </summary>
    private static bool AreValidSettings(RegexFlags flags, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        const RegexFlags Known = RegexFlags.HasIndices | RegexFlags.Global | RegexFlags.IgnoreCase | RegexFlags.Multiline | RegexFlags.DotAll | RegexFlags.Unicode | RegexFlags.UnicodeSets | RegexFlags.Sticky;
        return (flags & ~Known) == 0
            && !((flags & RegexFlags.Unicode) != 0 && (flags & RegexFlags.UnicodeSets) != 0)
            && edition is RegexEdition.Current or RegexEdition.Es2020
            && !(edition == RegexEdition.Es2020 && (flags & (RegexFlags.HasIndices | RegexFlags.UnicodeSets)) != 0)
            && unicodeVersion is RegexUnicodeVersion.Default or RegexUnicodeVersion.V13 or RegexUnicodeVersion.V17 or RegexUnicodeVersion.V18 or RegexUnicodeVersion.Latest;
    }

    /// <summary>Refuses an edition that is not one, and the flags ECMAScript 2020 does not have: <c>d</c> (2022) and <c>v</c> (2024).</summary>
    private static void ValidateBuildOptions(RegexBuildOptions buildOptions)
    {
        if ((buildOptions & ~RegexBuildOptions.Eager) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buildOptions), buildOptions, "The build options are not RegexBuildOptions.");
        }
    }

    private static void ValidateEdition(RegexFlags flags, RegexEdition edition)
    {
        if (edition is not RegexEdition.Current and not RegexEdition.Es2020)
        {
            throw new ArgumentOutOfRangeException(nameof(edition));
        }

        if (edition == RegexEdition.Es2020 && (flags & (RegexFlags.HasIndices | RegexFlags.UnicodeSets)) != 0)
        {
            throw new RegexParseException("Invalid regular expression flags", 0);
        }
    }

    /// <summary>The default match timeout the <see cref="AppContext"/> data sets.</summary>
    /// <param name="data">The data, or null when it is not set.</param>
    /// <returns>The timeout, or <see cref="InfiniteMatchTimeout"/>.</returns>
    internal static TimeSpan ReadDefaultMatchTimeout(object? data)
    {
        if (data is null)
        {
            return InfiniteMatchTimeout;
        }

        if (data is not TimeSpan timeout)
        {
            throw new InvalidCastException($"The AppContext data '{DefaultMatchTimeoutKey}' is a {data.GetType()}, not a TimeSpan.");
        }

        if (timeout != InfiniteMatchTimeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(DefaultMatchTimeoutKey, timeout, "The AppContext data is not a valid match timeout.");
        }

        return timeout;
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

    /// <summary>The programs of the pattern, the one that finds a match's bounds and the one that tracks every group, for the tests.</summary>
    /// <returns>The two.</returns>
    internal (Program Bounds, Program Captures) Programs() => (this.program, this.CaptureProgram());

    private RegexMatchTimeoutException Abandoned(MatchStop stop) => new(this.Pattern, this.MatchTimeout, stop == MatchStop.Memory);

    /// <summary>Builds what the first matches would: the automaton over ASCII, the program that tracks every group, and the search automata of both programs.</summary>
    private void BuildEverything()
    {
        this.Automaton();
        this.program.Automata();
        this.CaptureProgram().Automata();
    }

    /// <summary>The program that tracks every group, for the members that report captures.</summary>
    private Program CaptureProgram()
    {
        Program? cp = this.captureProgram;
        if (cp is null)
        {
            // Simplify changes the tree in place, and did so when the first program was compiled; compiling the
            // same tree again gives the same program apart from the groups it tracks.
            cp = DeepStack.Run(this.Pattern, (this.Flags & RegexFlags.UnicodeSets) != 0, () => Compiler.Compile(this.root!, this.unicode, this.GroupCount, trackAll: true));
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
                Dfa? built = DeepStack.Run(this.Pattern, (this.Flags & RegexFlags.UnicodeSets) != 0, () => Dfa.Build(this.root!));
                Interlocked.CompareExchange(ref this.dfa, built, null);
            }

            this.dfaTried = true;
        }

        return this.dfa;
    }
}
