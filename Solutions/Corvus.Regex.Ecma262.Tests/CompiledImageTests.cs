// <copyright file="CompiledImageTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections;
using System.Reflection;
using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The compiled regular expression as data (<see cref="RegexImage"/>): a regular expression read from its image holds
/// what one compiled from its pattern and built in full holds, field by field, and answers as it does; an image of
/// another engine or other settings, or a damaged one, is ignored and the pattern compiled; and
/// <see cref="RegexBuildOptions.Eager"/> builds everything in the constructor.
/// </summary>
[TestClass]
public class CompiledImageTests
{
    private const string B = "\\";

    private static readonly string[] Patterns =
    [
        string.Empty, "a", "^abc$", "abc", "a|b|c", "(a)(b)?", "(?:ab)+c", "a*?b", "[a-z]+", "[^a-z]", "^[A-Za-z]{2,}.[A-Za-z]{2,}",
        B + "d{4}-" + B + "d{2}-" + B + "d{2}", B + "bword" + B + "b", "(?<y>" + B + "d+)-" + B + "k<y>", "(a)" + B + "1", "(?=a)b|ab",
        "(?<=a)b", "(?<!a)b", "x(?!y)", "^(0|[1-9]" + B + "d*)" + B + ".(0|[1-9]" + B + "d*)$", "(base64key|awskms|gcpkms)://(.*)",
        "^[Ee][Ss]2015(" + B + ".([Cc][Oo][Ll][Ll][Ee][Cc][Tt][Ii][Oo][Nn]|[Cc][Oo][Rr][Ee]|[Pp][Rr][Oo][Xx][Yy]))?$",
        "^([d|D][e|E][f|F][a|A][u|U][l|L][t|T])|([n|N][o|O][n|N][e|E])|([s|S][m|M][a|A][l|L][l|L])$",
        "^([a-z_$][a-z0-9_$]{0,39}" + B + ".)*([a-zA-Z_$][a-zA-Z0-9_$]{0,39})$", "^((" + B + ".(?!" + B + ".)" + B + "/)?" + B + "w+" + B + "/?)+$",
        "Sherlock|Holmes|Watson|Irene|Adler", "[a-q][^u-z]{13}x", B + "w+" + B + "s+Holmes", "Holmes.{0,25}Watson|Watson.{0,25}Holmes",
        B + "p{L}+", B + "p{Script=Greek}", "[" + B + "p{L}--[a-z]]", "[" + B + "q{abc|d}x]", B + "p{RGI_Emoji}", "(?i:k)elvin", "a.c", "^.*$",
        "(?:a|ab)(?:c|bcd)(?:d*)", "a{2,5}?", "(a*)*b", "^(?:(?:25[0-5]|2[0-4]" + B + "d|1?" + B + "d?" + B + "d)" + B + ".){3}(?:25[0-5]|2[0-4]" + B + "d|1?" + B + "d?" + B + "d)$",
        "\U0001F600+", "[\U0001F600-\U0001F64F]", "caf\u00e9", B + "u{1F600}", "x*", "(?:)", "a|", "[]", "[^]",
    ];

    private static readonly string[] Texts =
    [
        string.Empty, "a", "abc", "xabcx", "ab", "aab", "2026-10-30", "a word here", "12-12", "12-13", "ab.cd", "1.2", "01.2",
        "awskms://key", "ES2015.Proxy", "es2015.core", "default", "NONE", "small", "a_b.c$d", "./x/y/", "Sherlock Holmes and Watson",
        "abcdefghijklmnopx", "Holmes said to Watson", "\u03b1\u03b2\u03b3", "kelvin", "\u212aelvin", "\U0001F600\U0001F601", "caf\u00e9", "aaaaaaaaab",
        "192.168.0.1", "256.1.1.1", "line\nbreak", "abcd",
    ];

    private static readonly RegexFlags[] Grammars = [RegexFlags.None, RegexFlags.Unicode, RegexFlags.UnicodeSets, RegexFlags.Unicode | RegexFlags.IgnoreCase, RegexFlags.Global | RegexFlags.Multiline | RegexFlags.DotAll];

    // State the image does not carry: the generated runners, the trees and maps the emitter reads, the searchers made
    // when first used, and the counters a search keeps as it learns.
    private static readonly HashSet<string> NotCompared =
    [
        "<Runner>k__BackingField", "<Root>k__BackingField", "<AutomatonRoot>k__BackingField", "<WindowPrefixes>k__BackingField",
        "<WindowRest>k__BackingField", "<TrackedGroups>k__BackingField", "<PointOf>k__BackingField", "checks", "rejects",
    ];

    [TestMethod]
    public void ARegexReadFromItsImageHoldsWhatACompiledOneHolds()
    {
        int read = 0;
        List<string> wrong = [];
        foreach ((string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion version) in Cases())
        {
            byte[] image = RegexImage.Write(pattern, flags, edition, version);
            Regex fromImage = new(pattern, flags, Regex.InfiniteMatchTimeout, edition, version, null, null, image);
            Regex compiled = new(pattern, flags, Regex.InfiniteMatchTimeout, edition, version, RegexBuildOptions.Eager);
            if (!fromImage.ReadFromImage)
            {
                wrong.Add($"/{pattern}/ {flags} {edition} {version}: the image was not read");
                continue;
            }

            read++;
            string? difference = Difference(State(compiled), State(fromImage), "regex", new HashSet<(object, object)>(PairComparer.Instance));
            if (difference is not null)
            {
                wrong.Add($"/{pattern}/ {flags} {edition} {version}: {difference}");
            }
        }

        Assert.AreEqual(0, wrong.Count, string.Join("\n", wrong.Take(30)));
        Assert.IsTrue(read > 500, $"only {read} images read");
    }

    [TestMethod]
    public void ARegexReadFromItsImageAnswersAsACompiledOne()
    {
        List<string> wrong = [];
        foreach ((string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion version) in Cases())
        {
            byte[] image = RegexImage.Write(pattern, flags, edition, version);
            Regex fromImage = new(pattern, flags, Regex.InfiniteMatchTimeout, edition, version, null, null, image);
            Regex compiled = new(pattern, flags, Regex.InfiniteMatchTimeout, edition, version);
            foreach (string text in Texts)
            {
                string expected = Answers(compiled, text);
                string actual = Answers(fromImage, text);
                if (expected != actual)
                {
                    wrong.Add($"/{pattern}/ {flags} {edition} {version} on \"{text}\": {expected} but {actual}");
                }
            }
        }

        Assert.AreEqual(0, wrong.Count, string.Join("\n", wrong.Take(30)));
    }

    [TestMethod]
    public void AnImageForOtherSettingsIsIgnored()
    {
        byte[] image = RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default);
        Assert.IsTrue(new Regex("a+b", RegexFlags.Unicode, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null, image).ReadFromImage);

        // Other flags, another edition, another version of Unicode, and another pattern under the same settings: each
        // regular expression is compiled from its own pattern and answers by it.
        Regex flags = new("a+b", RegexFlags.Unicode | RegexFlags.IgnoreCase, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null, image);
        Regex edition = new("a+b", RegexFlags.Unicode, Regex.InfiniteMatchTimeout, RegexEdition.Es2020, RegexUnicodeVersion.Default, null, null, image);
        Regex version = new("a+b", RegexFlags.Unicode, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.V13, null, null, image);
        Assert.IsFalse(flags.ReadFromImage);
        Assert.IsFalse(edition.ReadFromImage);
        Assert.IsFalse(version.ReadFromImage);
        Assert.IsTrue(flags.IsMatch("AAB"));
        Assert.IsTrue(edition.IsMatch("aab"));
    }

    [TestMethod]
    public void AnImageOfALayoutThisEngineDoesNotReadIsIgnored()
    {
        byte[] image = RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default);
        foreach (byte format in new byte[] { 0, RegexImage.MinimumFormat - 1, RegexImage.FormatVersion + 1, 0xFF })
        {
            byte[] layout = (byte[])image.Clone();
            layout[3] = format;
            Regex r = Read("a+b", layout);
            Assert.IsFalse(r.ReadFromImage, $"layout {format}");
            Assert.IsTrue(r.IsMatch("xaab"));
        }
    }

    [TestMethod]
    [DataRow("9096543f80969d8ba477181cc4a67bfeba3f76edbd27425d7b545f875daa3199", true)]
    [DataRow("8fe300ee5e122022c8e2ba87ff05861880c0986daa87d9ce086b402dcc17e98d", true)]
    [DataRow("8fe300ee5e122022c8e2ba87ff05861880c0986daa87d9ce086b402dcc17e98e", false)]
    [DataRow("", false)]
    public void AnImageOfLayoutOneIsReadWhenAReleaseWroteIt(string fingerprint, bool read)
    {
        // 1.0.10 and 1.0.11 wrote layout 1, recording a hash of the engine's sources: an image with either release's
        // hash holds what revision 1 of the compiler writes, and one with any other came from no release.
        foreach ((string pattern, RegexFlags flags, RegexEdition edition, RegexUnicodeVersion version) in Cases().Where((_, k) => k % 7 == 0))
        {
            byte[] image = RegexImage.WriteFormat1(pattern, flags, edition, version, fingerprint);
            Regex fromImage = new(pattern, flags, Regex.InfiniteMatchTimeout, edition, version, null, null, image);
            Assert.AreEqual(read, fromImage.ReadFromImage, $"/{pattern}/ {flags}");
            Regex compiled = new(pattern, flags, Regex.InfiniteMatchTimeout, edition, version);
            foreach (string text in Texts)
            {
                Assert.AreEqual(Answers(compiled, text), Answers(fromImage, text), $"/{pattern}/ {flags} on \"{text}\"");
            }
        }
    }

    [TestMethod]
    public void AnImageIsReadFromEveryTrustedRevisionAndNoOther()
    {
        // A compiler revision below the floor wrote output known to give wrong answers. One above this engine's own is
        // a later compiler's, whose output uses only what this engine knows, or the image would have said so.
        Assert.IsTrue(Read("a+b", RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default, RegexImage.MinimumTrustedRevision, [])).ReadFromImage);
        Assert.IsTrue(Read("a+b", RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default, RegexImage.CompilerRevision + 1, [])).ReadFromImage);
        Regex old = Read("a+b", RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default, RegexImage.MinimumTrustedRevision - 1, []));
        Assert.IsFalse(old.ReadFromImage);
        Assert.IsTrue(old.IsMatch("xaab"));
    }

    [TestMethod]
    public void ASectionThisEngineDoesNotKnowIsSkippedUnlessItIsRequired()
    {
        byte[] optional = RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default, RegexImage.CompilerRevision, [(99, false, [1, 2, 3])]);
        Assert.IsTrue(Read("a+b", optional).ReadFromImage);
        byte[] required = RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default, RegexImage.CompilerRevision, [(99, true, [1, 2, 3])]);
        Regex r = Read("a+b", required);
        Assert.IsFalse(r.ReadFromImage);
        Assert.IsTrue(r.IsMatch("xaab"));
        byte[] twice = RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default, RegexImage.CompilerRevision, [(RegexImage.SectionBounds, true, [])]);
        Assert.IsFalse(Read("a+b", twice).ReadFromImage);
    }

    [TestMethod]
    public void TheInstructionsKeepTheirNumbers()
    {
        // An image stores an instruction by its number. A number never changes or is reused once a release has
        // written it, and a new instruction takes the next one (docs/contributing/compatibility.md).
        string[] released =
        [
            "Char", "CharBack", "Set", "SetBack", "Lit", "Fail", "Bol", "Eol", "WordBoundary", "NotWordBoundary", "LineStart",
            "LineEnd", "WordBoundaryFold", "NotWordBoundaryFold", "Jmp", "Split", "SaveTmp", "Capture", "Clear", "Backref",
            "BackrefBack", "BackrefFold", "BackrefFoldBack", "BackrefUnits", "BackrefUnitsBack", "Look", "LookOk", "LookFail",
            "Rep", "RepLazy", "LoopInit", "Loop", "LoopIter", "Match",
        ];

        for (int k = 0; k < released.Length; k++)
        {
            Assert.AreEqual(k, (int)(Op)Enum.Parse(typeof(Op), released[k]), released[k]);
            Assert.IsTrue(Instructions.IsKnown(k), released[k]);
        }

        Op[] all = (Op[])Enum.GetValues(typeof(Op));
        Assert.AreEqual(all.Length, all.Select(op => (int)op).Distinct().Count(), "Two instructions share a number.");
        foreach (Op op in all.Where(op => Array.IndexOf(released, op.ToString()) < 0))
        {
            Assert.IsTrue((int)op >= released.Length, $"{op} takes a number below the released ones.");
        }

        Assert.IsFalse(Instructions.IsKnown(-1));
        Assert.IsFalse(Instructions.IsKnown(255));
    }

    [TestMethod]
    public void AnIgnoredImageIsReported()
    {
        using Listener listener = new();
        long before = RegexEventSource.Log.ImagesIgnored;
        byte[] image = RegexImage.Write("a+b", RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default);
        Regex settings = new("a+b", RegexFlags.Unicode | RegexFlags.IgnoreCase, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null, image);
        Assert.IsFalse(settings.ReadFromImage);
        Assert.IsTrue(Read("a+b", image).ReadFromImage);
        Assert.IsTrue(RegexEventSource.Log.ImagesIgnored >= before + 1);
        Assert.IsTrue(listener.Reasons.Contains("Settings"), string.Join(",", listener.Reasons));
    }

    [TestMethod]
    public void ADamagedImageIsIgnoredAndThePatternCompiled()
    {
        // Every byte changed in turn, and the image cut short at every length: nothing throws, and the regular
        // expression answers by its pattern whether or not the image was read.
        string pattern = "(?<y>" + B + "d+)-(a|bc)*" + B + "k<y>";
        byte[] image = RegexImage.Write(pattern, RegexFlags.Unicode, RegexEdition.Current, RegexUnicodeVersion.Default);
        Regex reference = new(pattern, RegexFlags.Unicode);
        string[] texts = ["12-abc12", "12-abc13", "x"];
        string expected = string.Join("|", texts.Select(t => Answers(reference, t)));
        for (int k = 0; k < image.Length; k++)
        {
            foreach (byte flip in new byte[] { 0x01, 0x80, 0xFF })
            {
                byte[] damaged = (byte[])image.Clone();
                damaged[k] ^= flip;
                Regex r = new(pattern, RegexFlags.Unicode, Regex.DefaultMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null, damaged);
                Assert.AreEqual(expected, string.Join("|", texts.Select(t => Answers(r, t))), $"byte {k} ^ {flip}");
            }
        }

        for (int length = 0; length < image.Length; length++)
        {
            Regex r = new(pattern, RegexFlags.Unicode, Regex.DefaultMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null, image.AsSpan(0, length));
            Assert.IsFalse(r.ReadFromImage, $"length {length}");
            Assert.AreEqual(expected, string.Join("|", texts.Select(t => Answers(r, t))), $"length {length}");
        }
    }

    [TestMethod]
    public void EagerBuildsEverythingInTheConstructor()
    {
        Regex lazy = new("a+b|cd", RegexFlags.Unicode, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default);
        Regex eager = new("a+b|cd", RegexFlags.Unicode, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, RegexBuildOptions.Eager);
        Assert.IsFalse((bool)Field(lazy, "dfaTried")!);
        Assert.IsNull(Field(lazy, "captureProgram"));
        Assert.IsTrue((bool)Field(eager, "dfaTried")!);
        Assert.IsNotNull(Field(eager, "dfa"));
        Program captures = (Program)Field(eager, "captureProgram")!;
        Assert.IsNotNull(Field(eager.Programs().Bounds, "automata"));
        Assert.IsNotNull(Field(captures, "automata"));
        Assert.IsTrue(eager.IsMatch("xaab"));

        Assert.IsTrue(Regex.TryCreate("a+", RegexFlags.None, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, RegexBuildOptions.Eager, out Regex? created, out _));
        Assert.IsTrue((bool)Field(created!, "dfaTried")!);
        Assert.IsFalse(Regex.TryCreate("a(", RegexFlags.None, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, RegexBuildOptions.Eager, out _, out string? error));
        Assert.IsNotNull(error);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Regex("a", RegexFlags.None, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, (RegexBuildOptions)2));
    }

    [TestMethod]
    public void AGeneratedRegexIsReadFromItsImage()
    {
        Assert.IsTrue(GeneratedImages.Dates().ReadFromImage);
        Assert.IsTrue(GeneratedImages.Dates().IsMatch("on 11/18/2019"));
    }

    private static Regex Read(string pattern, byte[] image)
        => new(pattern, RegexFlags.Unicode, Regex.InfiniteMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, null, null, image);

    private static IEnumerable<(string Pattern, RegexFlags Flags, RegexEdition Edition, RegexUnicodeVersion Version)> Cases()
    {
        foreach (string pattern in Patterns)
        {
            foreach (RegexFlags flags in Grammars)
            {
                foreach (RegexEdition edition in new[] { RegexEdition.Current, RegexEdition.Es2020 })
                {
                    foreach (RegexUnicodeVersion version in new[] { RegexUnicodeVersion.Default, RegexUnicodeVersion.V13 })
                    {
                        if (Regex.TryCreate(pattern, flags, Regex.InfiniteMatchTimeout, edition, version, out _, out _))
                        {
                            yield return (pattern, flags, edition, version);
                        }
                    }
                }
            }
        }
    }

    /// <summary>The answers a regular expression gives on a text, over both encodings: whether it matches, and its matches with their groups.</summary>
    private static string Answers(Regex r, string text)
    {
        StringBuilder sb = new();
        sb.Append(r.IsMatch(text)).Append(r.IsMatch(Encoding.UTF8.GetBytes(text)));
        foreach (Match m in r.Matches(text))
        {
            sb.Append('[').Append(m.Index).Append(',').Append(m.Length);
            foreach (Group g in m.Groups)
            {
                sb.Append(g.Success ? $"({g.Index},{g.Length})" : "(-)");
            }

            sb.Append(']');
        }

        sb.Append(r.Count(Encoding.UTF8.GetBytes(text)));
        return sb.ToString();
    }

    /// <summary>What a regular expression holds once everything is built: its fields, its programs, their automata.</summary>
    private static object State(Regex r)
    {
        (Program bounds, Program captures) = r.Programs();
        return new object?[] { Field(r, "dfa"), Field(r, "linear"), r.GroupCount, r.GetGroupNames(), bounds, bounds.Automata(), captures, captures.Automata() };
    }

    private static object? Field(object o, string name)
    {
        for (Type? t = o.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is FieldInfo f)
            {
                return f.GetValue(o);
            }
        }

        throw new InvalidOperationException($"{o.GetType().Name} has no field {name}");
    }

    /// <summary>Where two object graphs first differ, or null when they hold the same values.</summary>
    private static string? Difference(object? a, object? b, string path, HashSet<(object, object)> seen)
    {
        if (a is null || b is null)
        {
            return a is null && b is null ? null : $"{path}: {(a is null ? "null" : "set")} and {(b is null ? "null" : "set")}";
        }

        Type type = a.GetType();
        if (type != b.GetType())
        {
            return $"{path}: {type.Name} and {b.GetType().Name}";
        }

        if (type.IsPrimitive || type.IsEnum || a is string || a is decimal)
        {
            return a.Equals(b) ? null : $"{path}: {a} and {b}";
        }

        if (type.FullName!.StartsWith("System.Buffers.SearchValues", StringComparison.Ordinal) || type.FullName.StartsWith("System.Buffers.", StringComparison.Ordinal) && type.Name.Contains("SearchValues"))
        {
            return null;
        }

        if (!type.IsValueType && !seen.Add((a, b)))
        {
            return null;
        }

        if (a is IDictionary da)
        {
            IDictionary db = (IDictionary)b;
            if (da.Count != db.Count)
            {
                return $"{path}: {da.Count} and {db.Count} entries";
            }

            foreach (DictionaryEntry e in da)
            {
                if (!db.Contains(e.Key))
                {
                    return $"{path}: no {e.Key}";
                }

                if (Difference(e.Value, db[e.Key], $"{path}[{e.Key}]", seen) is string d)
                {
                    return d;
                }
            }

            return null;
        }

        if (a is IEnumerable ea && a is not string)
        {
            List<object?> la = ea.Cast<object?>().ToList();
            List<object?> lb = ((IEnumerable)b).Cast<object?>().ToList();
            if (la.Count != lb.Count)
            {
                return $"{path}: {la.Count} and {lb.Count} elements";
            }

            for (int k = 0; k < la.Count; k++)
            {
                if (Difference(la[k], lb[k], $"{path}[{k}]", seen) is string d)
                {
                    return d;
                }
            }

            if (a is Array)
            {
                return null;
            }
        }

        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (NotCompared.Contains(f.Name) || f.FieldType.Name.Contains("SearchValues"))
                {
                    continue;
                }

                if (Difference(f.GetValue(a), f.GetValue(b), $"{path}.{f.Name}", seen) is string d)
                {
                    return d;
                }
            }
        }

        return null;
    }

    /// <summary>Pairs of objects compared by reference, so that a graph with cycles is walked once.</summary>
    private sealed class PairComparer : IEqualityComparer<(object, object)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((object, object) x, (object, object) y) => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object, object) obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1) ^ (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item2) * 31);
    }

    /// <summary>Hears the library's events, for the reasons images were ignored.</summary>
    private sealed class Listener : System.Diagnostics.Tracing.EventListener
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Reasons { get; } = [];

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource eventSource)
        {
            if (eventSource.Name == "Corvus.Regex.Ecma262")
            {
                this.EnableEvents(eventSource, System.Diagnostics.Tracing.EventLevel.Warning);
            }
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs eventData)
        {
            if (eventData.EventId == 1 && eventData.Payload is { Count: 2 } payload && payload[1] is string reason)
            {
                this.Reasons.Add(reason);
            }
        }
    }
}

/// <summary>A pattern the source generator writes, with its image.</summary>
internal static partial class GeneratedImages
{
    [GeneratedRegex(@"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b", "u")]
    internal static partial Regex Dates();
}
