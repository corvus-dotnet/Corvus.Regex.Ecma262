// <copyright file="GeneratorTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections.Immutable;
using Corvus.Ecma262.RegularExpressions.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Corvus.Ecma262.RegularExpressions.SourceGenerator.Tests;

/// <summary>
/// The generator run by the driver over small programs: what it emits compiles and has the shape asked for, and
/// what it refuses it refuses with the right diagnostic.
/// </summary>
[TestClass]
public class GeneratorTests
{
    [TestMethod]
    public void APatternWithLineBreaksOrALoneSurrogateStillCompiles()
    {
        // The pattern's text is in the summary of what is generated: a line break there would end the comment, and
        // a lone surrogate cannot be written at all.
        (Compilation output, ImmutableArray<Diagnostic> diagnostics, _) = Run("""
            using Corvus.Ecma262.RegularExpressions;
            namespace Sample
            {
                public static partial class Patterns
                {
                    [GeneratedRegex("a\nb\rc\u2028d\u0085e\uD800f")]
                    public static partial Regex Lines();
                }
            }
            """);
        Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics));
        AssertCompiles(output);
    }

    [TestMethod]
    public void AStaticPartialMethodIsGivenABody()
    {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics, string generated) = Run("""
            using Corvus.Ecma262.RegularExpressions;
            namespace Sample
            {
                public static partial class Patterns
                {
                    [GeneratedRegex(@"\d+", "gu")]
                    public static partial Regex Digits();

                    [GeneratedRegex(@"[a-z]+", RegexFlags.IgnoreCase | RegexFlags.Unicode, 250)]
                    internal static partial Regex Word();

                    [GeneratedRegex("x", "", -1)]
                    internal static partial Regex Untimed();
                }
            }
            """);
        Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics));
        AssertCompiles(output);
        StringAssert.Contains(generated, "public static partial global::Corvus.Ecma262.RegularExpressions.Regex Digits() => CorvusRegex_Digits.Instance;");
        // No timeout in the attribute is the default match timeout, and -1 is none.
        StringAssert.Contains(generated, "new(\"\\\\d+\", (global::Corvus.Ecma262.RegularExpressions.RegexFlags)34, global::Corvus.Ecma262.RegularExpressions.Regex.DefaultMatchTimeout, new Bounds(), new Captures());");
        StringAssert.Contains(generated, "new(\"x\", (global::Corvus.Ecma262.RegularExpressions.RegexFlags)0, global::Corvus.Ecma262.RegularExpressions.Regex.InfiniteMatchTimeout, new Bounds(), new Captures());");
        StringAssert.Contains(generated, "(global::Corvus.Ecma262.RegularExpressions.RegexFlags)36, global::System.TimeSpan.FromMilliseconds(250), new Bounds(), new Captures());");
    }

    [TestMethod]
    public void AStaticPartialPropertyInANestedGenericTypeIsGivenABody()
    {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics, string generated) = Run("""
            using Corvus.Ecma262.RegularExpressions;
            public partial class Outer<T>
            {
                private partial struct Inner
                {
                    [GeneratedRegex("^a|b$")]
                    private static partial Regex Edges { get; }
                }
            }
            """);
        Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics));
        AssertCompiles(output);
        StringAssert.Contains(generated, "partial class Outer<T>");
        StringAssert.Contains(generated, "partial struct Inner");
        StringAssert.Contains(generated, "private static partial global::Corvus.Ecma262.RegularExpressions.Regex Edges => CorvusRegex_Edges.Instance;");
    }

    [TestMethod]
    [DataRow("(", "u", "CRE0001", "Unterminated group")]
    [DataRow("a{2,1}", "u", "CRE0001", "Numbers out of order")]
    [DataRow(@"\p{Lettr}", "u", "CRE0001", "Invalid property name")]
    [DataRow("a", "gx", "CRE0003", "'x' is not a flag")]
    [DataRow("a", "gg", "CRE0003", "given twice")]
    [DataRow("a", "uv", "CRE0003", "cannot both")]
    [DataRow("[a--]", "v", "CRE0001", "Invalid set operation")]
    public void APatternOrFlagsThatAreNotValidAreABuildError(string pattern, string flags, string id, string message)
    {
        (_, ImmutableArray<Diagnostic> diagnostics, string generated) = Run($$"""
            using Corvus.Ecma262.RegularExpressions;
            public static partial class Patterns
            {
                [GeneratedRegex({{Literal(pattern)}}, "{{flags}}")]
                public static partial Regex Bad();
            }
            """);
        Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics));
        Assert.AreEqual(id, diagnostics[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        StringAssert.Contains(diagnostics[0].GetMessage(), message);
        Assert.AreEqual(string.Empty, generated, "nothing is generated for a member with an error");
    }

    [TestMethod]
    public void TheEditionAndVersionOfUnicodeArePassedToTheRegex()
    {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics, string generated) = Run("""
            using Corvus.Ecma262.RegularExpressions;
            public static partial class Patterns
            {
                [GeneratedRegex(@"\p{sc=Grek}", "u", Edition = RegexEdition.Es2020, UnicodeVersion = RegexUnicodeVersion.V13)]
                public static partial Regex Greek();

                [GeneratedRegex(@"(.)\1", "iu", UnicodeVersion = RegexUnicodeVersion.V18)]
                public static partial Regex Twice();

                [GeneratedRegex("x", "")]
                public static partial Regex Plain();
            }
            """);
        Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics));
        AssertCompiles(output);
        StringAssert.Contains(generated, "Regex.DefaultMatchTimeout, (global::Corvus.Ecma262.RegularExpressions.RegexEdition)1, (global::Corvus.Ecma262.RegularExpressions.RegexUnicodeVersion)13, new Bounds(), new Captures());");
        StringAssert.Contains(generated, "Regex.DefaultMatchTimeout, (global::Corvus.Ecma262.RegularExpressions.RegexEdition)0, (global::Corvus.Ecma262.RegularExpressions.RegexUnicodeVersion)18, new Bounds(), new Captures());");
        StringAssert.Contains(generated, "(global::Corvus.Ecma262.RegularExpressions.RegexUnicodeVersion)18");
        // The defaults are not written, so the code of a member without them is as it was.
        StringAssert.Contains(generated, "new(\"x\", (global::Corvus.Ecma262.RegularExpressions.RegexFlags)0, global::Corvus.Ecma262.RegularExpressions.Regex.DefaultMatchTimeout, new Bounds(), new Captures());");
    }

    [TestMethod]
    [DataRow("(?i:a)", "u", "Edition = RegexEdition.Es2020", "CRE0001", "Invalid group")]
    [DataRow(@"\p{Script=Yezidi}", "u", "Edition = RegexEdition.Es2020, UnicodeVersion = RegexUnicodeVersion.V13", "CRE0001", "Invalid property name")]
    [DataRow(@"\p{Script=Jurchen}", "u", "UnicodeVersion = RegexUnicodeVersion.V17", "CRE0001", "Invalid property name")]
    [DataRow("a", "v", "Edition = RegexEdition.Es2020", "CRE0003", "no d or v flag")]
    [DataRow("a", "d", "Edition = RegexEdition.Es2020", "CRE0003", "no d or v flag")]
    [DataRow("a", "", "Edition = (RegexEdition)9", "CRE0003", "not a RegexEdition")]
    [DataRow("a", "", "UnicodeVersion = (RegexUnicodeVersion)16", "CRE0003", "not a RegexUnicodeVersion")]
    public void APatternTheEditionOrVersionRefusesIsABuildError(string pattern, string flags, string settings, string id, string message)
    {
        (_, ImmutableArray<Diagnostic> diagnostics, string generated) = Run($$"""
            using Corvus.Ecma262.RegularExpressions;
            public static partial class Patterns
            {
                [GeneratedRegex({{Literal(pattern)}}, "{{flags}}", {{settings}})]
                public static partial Regex Bad();
            }
            """);
        Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics));
        Assert.AreEqual(id, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), message);
        Assert.AreEqual(string.Empty, generated);
    }

    [TestMethod]
    [DataRow("public static partial Regex M(int x);", "CRE0002", "has parameters")]
    [DataRow("public partial Regex M();", "CRE0002", "not static")]
    [DataRow("public static partial string M();", "CRE0002", "does not return")]
    [DataRow("public static partial Regex M { get; set; }", "CRE0002", "has a setter")]
    public void AMemberOfTheWrongShapeIsABuildError(string member, string id, string message)
    {
        (_, ImmutableArray<Diagnostic> diagnostics, _) = Run($$"""
            using Corvus.Ecma262.RegularExpressions;
            public static partial class Patterns
            {
                [GeneratedRegex("a")]
                {{member}}
            }
            """);
        Assert.IsTrue(diagnostics.Any(d => d.Id == id && d.GetMessage().Contains(message)), string.Join("\n", diagnostics));
    }

    [TestMethod]
    public void AContainingTypeThatIsNotPartialIsABuildError()
    {
        (_, ImmutableArray<Diagnostic> diagnostics, _) = Run("""
            using Corvus.Ecma262.RegularExpressions;
            public static class Patterns
            {
                [GeneratedRegex("a")]
                public static partial Regex M();
            }
            """);
        Assert.IsTrue(diagnostics.Any(d => d.Id == "CRE0004" && d.GetMessage().Contains("Patterns")), string.Join("\n", diagnostics));
    }

    private static string Literal(string s) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, quote: true);

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string source)
    {
        List<MetadataReference> references = [];
#if NETFRAMEWORK
        // The framework's assemblies are those the process has loaded whose names say they are the framework's:
        // mscorlib, System, System.Core, the netstandard facade the library is built against, and System.Memory,
        // which the touches below make sure of. Nothing else: the generator's own assembly carries a copy of the
        // engine, and with it referenced every engine type would exist twice.
        _ = typeof(System.Buffers.ArrayPool<byte>);
        _ = typeof(System.Runtime.InteropServices.MemoryMarshal);
        _ = System.Reflection.Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = assembly.GetName().Name;
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) && (name.StartsWith("System", StringComparison.Ordinal) || name is "mscorlib" or "netstandard"))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }
#else
        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "mscorlib.dll" or "netstandard.dll")
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
#endif

        references.Add(MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location));
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new RegexGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        return (output, diagnostics, generated);
    }

    private static void AssertCompiles(Compilation output)
    {
        ImmutableArray<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        Assert.AreEqual(0, errors.Length, string.Join("\n", errors));
    }
}
