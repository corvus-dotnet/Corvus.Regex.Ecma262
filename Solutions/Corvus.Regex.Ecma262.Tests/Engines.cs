// <copyright file="Engines.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

extern alias Gen;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The same pattern as the interpreter runs it and as the generator's code runs it (the emitter's source compiled
/// with Roslyn and loaded), so that a test of what the engine answers asserts both.
/// </summary>
internal static class Engines
{
    private static readonly ConcurrentDictionary<(string Pattern, string Flags), (GeneratedRunner Bounds, GeneratedRunner Captures)> Runners = new();
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    /// <summary>The pattern by both engines: the interpreter first, then the generated code.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The two.</returns>
    public static (Regex Interpreted, Regex Generated) Both(string pattern, string flags)
    {
        RegexFlags parsed = Regex.ParseFlags(flags);
        (GeneratedRunner bounds, GeneratedRunner captures) = Runners.GetOrAdd((pattern, flags), key => Compile(key.Pattern, parsed));
        return (new Regex(pattern, flags), new Regex(pattern, parsed, Regex.InfiniteMatchTimeout, bounds, captures));
    }

    private static (GeneratedRunner Bounds, GeneratedRunner Captures) Compile(string pattern, RegexFlags flags)
    {
        string runners = Gen::Corvus.Ecma262.RegularExpressions.SourceGenerator.RegexGenerator.Runners(pattern, (int)flags);
        string source = "#nullable enable\n#pragma warning disable CS0162, CS0164, CS0168, CS0219, CS8321, CS1522\nnamespace G\n{\n    internal static class R\n    {\n        internal static readonly global::Corvus.Ecma262.RegularExpressions.GeneratedRunner B = new Bounds();\n        internal static readonly global::Corvus.Ecma262.RegularExpressions.GeneratedRunner C = new Captures();\n" + runners + "    }\n}\n";
        CSharpCompilation compilation = CSharpCompilation.Create(
            "G" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream ms = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(ms);
        if (!result.Success)
        {
            throw new InvalidOperationException("The generated code for " + pattern + " did not compile:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5)) + "\n" + source);
        }

        Type type = Assembly.Load(ms.ToArray()).GetType("G.R")!;
        return (
            (GeneratedRunner)type.GetField("B", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!,
            (GeneratedRunner)type.GetField("C", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!);
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
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

        references.Add(MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location));
        return [.. references];
    }
}