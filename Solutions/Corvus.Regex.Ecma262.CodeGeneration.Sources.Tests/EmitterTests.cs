// <copyright file="EmitterTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Emit = Corvus.Ecma262.RegularExpressions.CodeGeneration;
using Lib = Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.CodeGeneration.Tests;

/// <summary>
/// <c>RegexEmitter.TryEmit</c>, from the package's copy of the emitter: what it writes compiles against the library
/// and answers as the library's interpreter does, and what it refuses it refuses as <c>Regex.TryCreate</c> does.
/// </summary>
[TestClass]
public class EmitterTests
{
    // The settings as the data rows give them (the package's enums are internal, so a test method cannot take them).
    private const int Unicode = (int)Emit.RegexFlags.Unicode;
    private const int UnicodeSets = (int)Emit.RegexFlags.UnicodeSets;
    private const int Current = (int)Emit.RegexEdition.Current;
    private const int Es2020 = (int)Emit.RegexEdition.Es2020;

    private static readonly (string Pattern, Emit.RegexFlags Flags)[] Patterns =
    [
        ("^[a-z][a-z0-9_]{0,29}$", Emit.RegexFlags.Unicode),
        ("Sherlock|Holmes|Watson", Emit.RegexFlags.Unicode),
        ("(\\d{4})-(\\d{2})-(\\d{2})", Emit.RegexFlags.Unicode),
        ("(?<word>\\w+)\\s+\\k<word>", Emit.RegexFlags.Unicode),
        ("(?<=\\$)\\d+(?:\\.\\d\\d)?", Emit.RegexFlags.Unicode),
        ("^\\p{Lu}\\p{Ll}+$", Emit.RegexFlags.Unicode),
        ("sherlock", Emit.RegexFlags.Unicode | Emit.RegexFlags.IgnoreCase),
        ("^.{1,4}$", Emit.RegexFlags.Unicode),
        ("[^\\s\\S]|x*", Emit.RegexFlags.Unicode),
        ("a{2,}b", Emit.RegexFlags.None),
        ("[\\p{L}--[a-z]]+", Emit.RegexFlags.UnicodeSets),
    ];

    private static readonly string[] Inputs =
    [
        string.Empty,
        "abc",
        "Sherlock Holmes and Dr Watson",
        "On 2026-10-29 the the price was $12.50",
        "Ångström",
        "ſHERLOCK",
        "a😀b",
        "aaab aab ab",
        "ÀÉÎ xyz",
        "user_name1",
    ];

    [TestMethod]
    public void WhatItWritesAnswersAsTheInterpreterDoes()
    {
        StringBuilder host = new();
        host.AppendLine("public static class Host").AppendLine("{");
        for (int k = 0; k < Patterns.Length; k++)
        {
            (string pattern, Emit.RegexFlags flags) = Patterns[k];
            Assert.IsTrue(Emit.RegexEmitter.TryEmit(pattern, flags, Emit.RegexEdition.Current, Emit.RegexUnicodeVersion.Default, "H" + k, out string? source, out string? error), pattern + ": " + error);
            Assert.IsNull(error);
            host.Append("    public static global::Corvus.Ecma262.RegularExpressions.Regex R").Append(k).Append(" => H").Append(k).AppendLine(".Instance;");
            host.AppendLine(source);
        }

        host.AppendLine("}");
        Type type = Compile(host.ToString()).GetType("Host")!;
        for (int k = 0; k < Patterns.Length; k++)
        {
            (string pattern, Emit.RegexFlags flags) = Patterns[k];
            var generated = (Lib.Regex)type.GetProperty("R" + k)!.GetValue(null)!;
            Lib.Regex interpreted = new(pattern, (Lib.RegexFlags)(int)flags);
            Assert.AreEqual(pattern, generated.Pattern);
            Assert.AreEqual(interpreted.Flags, generated.Flags);
            foreach (string input in Inputs)
            {
                Assert.AreEqual(Describe(interpreted.Match(input)), Describe(generated.Match(input)), $"/{pattern}/ over \"{input}\"");
                Assert.AreEqual(interpreted.IsMatch(Encoding.UTF8.GetBytes(input)), generated.IsMatch(Encoding.UTF8.GetBytes(input)), $"/{pattern}/ over UTF-8 \"{input}\"");
            }
        }
    }

    [TestMethod]
    public void TheEditionTheVersionAndTheTimeoutAreCarried()
    {
        Assert.IsTrue(Emit.RegexEmitter.TryEmit("\\p{L}+", Emit.RegexFlags.Unicode, 1234, Emit.RegexEdition.Es2020, Emit.RegexUnicodeVersion.V13, "H", out string? source, out _));
        Type type = Compile("public static class Host { public static global::Corvus.Ecma262.RegularExpressions.Regex R => H.Instance;\n" + source + "\n}").GetType("Host")!;
        var regex = (Lib.Regex)type.GetProperty("R")!.GetValue(null)!;
        Assert.AreEqual(Lib.RegexEdition.Es2020, regex.Edition);
        Assert.AreEqual(Lib.RegexUnicodeVersion.V13, regex.UnicodeVersion);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1234), regex.MatchTimeout);
    }

    [TestMethod]
    public void TheDefaultTimeoutIsTheProcesssAndMinusOneIsNone()
    {
        Assert.IsTrue(Emit.RegexEmitter.TryEmit("a", Emit.RegexFlags.None, Emit.RegexEdition.Current, Emit.RegexUnicodeVersion.Default, "D", out string? byDefault, out _));
        Assert.IsTrue(Emit.RegexEmitter.TryEmit("a", Emit.RegexFlags.None, -1, Emit.RegexEdition.Current, Emit.RegexUnicodeVersion.Default, "N", out string? none, out _));
        Type type = Compile("public static class Host { public static global::Corvus.Ecma262.RegularExpressions.Regex D1 => D.Instance; public static global::Corvus.Ecma262.RegularExpressions.Regex N1 => N.Instance;\n" + byDefault + "\n" + none + "\n}").GetType("Host")!;
        Assert.AreEqual(Lib.Regex.DefaultMatchTimeout, ((Lib.Regex)type.GetProperty("D1")!.GetValue(null)!).MatchTimeout);
        Assert.AreEqual(Lib.Regex.InfiniteMatchTimeout, ((Lib.Regex)type.GetProperty("N1")!.GetValue(null)!).MatchTimeout);
    }

    [TestMethod]
    [DataRow("(", Unicode, Current)]
    [DataRow("a{2,1}", Unicode, Current)]
    [DataRow("\\-", Unicode, Current)]
    [DataRow("(?i:a)", Unicode, Es2020)]
    public void APatternThatDoesNotCompileIsRefusedWithTryCreatesMessage(string pattern, int flagValue, int editionValue)
    {
        var flags = (Emit.RegexFlags)flagValue;
        var edition = (Emit.RegexEdition)editionValue;
        Assert.IsFalse(Emit.RegexEmitter.TryEmit(pattern, flags, edition, Emit.RegexUnicodeVersion.Default, "H", out string? source, out string? error));
        Assert.IsNull(source);
        Assert.IsFalse(Lib.Regex.TryCreate(pattern, (Lib.RegexFlags)(int)flags, Lib.Regex.DefaultMatchTimeout, (Lib.RegexEdition)(int)edition, Lib.RegexUnicodeVersion.Default, out _, out string? expected));
        Assert.AreEqual(expected, error);
    }

    [TestMethod]
    public void TheModifierGroupOfTodaysGrammarCompiles()
    {
        Assert.IsTrue(Emit.RegexEmitter.TryEmit("(?i:a)", Emit.RegexFlags.Unicode, Emit.RegexEdition.Current, Emit.RegexUnicodeVersion.Default, "H", out _, out _));
    }

    [TestMethod]
    [DataRow(Unicode | UnicodeSets, Current, 0, null, "the u and v flags cannot both be given")]
    [DataRow(UnicodeSets, Es2020, 0, null, "ECMAScript 2020 has no d or v flag")]
    [DataRow(0, 7, 0, null, "the edition 7 is not a RegexEdition")]
    [DataRow(0, Current, 99, null, "the version 99 is not a RegexUnicodeVersion")]
    [DataRow(0, Current, 0, -2, "the timeout -2 is not -1 or more")]
    public void SettingsThatDoNotGoTogetherAreRefused(int flagValue, int editionValue, int version, int? timeout, string expected)
    {
        var flags = (Emit.RegexFlags)flagValue;
        var edition = (Emit.RegexEdition)editionValue;
        Assert.IsFalse(Emit.RegexEmitter.TryEmit("a", flags, timeout, edition, (Emit.RegexUnicodeVersion)version, "H", out string? source, out string? error));
        Assert.IsNull(source);
        Assert.AreEqual(expected, error);
    }

    [TestMethod]
    [DataRow("^[a-z]+$", Unicode, Current)]
    [DataRow("(?i:a)", Unicode, Current)]
    [DataRow("(?i:a)", Unicode, Es2020)]
    [DataRow("(", Unicode, Current)]
    [DataRow("\\-", 0, Current)]
    [DataRow("\\-", Unicode, Current)]
    public void ValidationAgreesWithTryCreate(string pattern, int flagValue, int editionValue)
    {
        bool valid = Emit.RegexEmitter.TryValidate(pattern, (Emit.RegexFlags)flagValue, (Emit.RegexEdition)editionValue, Emit.RegexUnicodeVersion.Default, out string? error, out bool tooLarge);
        bool expected = Lib.Regex.TryCreate(pattern, (Lib.RegexFlags)flagValue, Lib.Regex.DefaultMatchTimeout, (Lib.RegexEdition)editionValue, Lib.RegexUnicodeVersion.Default, out _, out string? expectedError);
        Assert.AreEqual(expected, valid);
        Assert.AreEqual(expectedError, error);
        Assert.IsFalse(tooLarge);
    }

    [TestMethod]
    public void APatternOverALimitOfSizeIsTooLargeNotInvalid()
    {
        string pattern = new string('(', 201) + new string(')', 201);
        Assert.IsFalse(Emit.RegexEmitter.TryValidate(pattern, Emit.RegexFlags.Unicode, Emit.RegexEdition.Current, Emit.RegexUnicodeVersion.Default, out string? error, out bool tooLarge));
        Assert.IsTrue(tooLarge);
        Lib.RegexPatternTooLargeException e = Assert.ThrowsExactly<Lib.RegexPatternTooLargeException>(() => new Lib.Regex(pattern, Lib.RegexFlags.Unicode));
        Assert.AreEqual(e.Message, error);
    }

    [TestMethod]
    public void AnyPatternTextSurvivesTheLiteral()
    {
        // Quotes, backslashes, controls, the line and paragraph separators, a byte order mark, a lone surrogate and
        // a pair: each written into the generated code and read back as the pattern.
        string pattern = new(
        [
            '"', '\\', '\\', (char)0x00, (char)0x07, (char)0x08, (char)0x0C, (char)0x0A, (char)0x0D, (char)0x09, (char)0x0B,
            (char)0x01, (char)0x7F, (char)0x85, (char)0x2028, (char)0x2029, (char)0xFEFF, (char)0xD800, (char)0xE9,
            (char)0xD83D, (char)0xDE00, 'x',
        ]);
        Assert.IsTrue(Emit.RegexEmitter.TryEmit(pattern, Emit.RegexFlags.None, Emit.RegexEdition.Current, Emit.RegexUnicodeVersion.Default, "H", out string? source, out string? error), error);
        Type type = Compile("public static class Host { public static global::Corvus.Ecma262.RegularExpressions.Regex R => H.Instance;\n" + source + "\n}").GetType("Host")!;
        Assert.AreEqual(pattern, ((Lib.Regex)type.GetProperty("R")!.GetValue(null)!).Pattern);
    }

    private static string Describe(Lib.Match m)
    {
        if (!m.Success)
        {
            return "no match";
        }

        StringBuilder sb = new();
        for (int g = 0; g < m.Groups.Count; g++)
        {
            Lib.Group group = m.Groups[g];
            sb.Append(group.Success ? $"{group.Index}+{group.Length} " : "- ");
        }

        return sb.ToString();
    }

    private static Assembly Compile(string source)
    {
        List<MetadataReference> references = [];
        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "mscorlib.dll" or "netstandard.dll")
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(Lib.Regex).Assembly.Location));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Emitted" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
}
