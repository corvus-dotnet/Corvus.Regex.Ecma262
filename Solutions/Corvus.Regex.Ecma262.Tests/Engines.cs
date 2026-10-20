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
    // The symbols the generated code is compiled with are those of the test's own target, so that the paths a
    // consumer on that target takes are the paths tested: the emitted search on net10.0, the plain paths on net481.
#if NETFRAMEWORK
    private static readonly string[] Symbols = ["NETFRAMEWORK", "NET481", "NET48_OR_GREATER", "NET481_OR_GREATER", "NET472_OR_GREATER", "NET462_OR_GREATER"];
#else
    private static readonly string[] Symbols = ["NET", "NET8_0_OR_GREATER", "NET9_0_OR_GREATER", "NET10_0", "NET10_0_OR_GREATER"];
#endif

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
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: Symbols))],
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
#if NETFRAMEWORK
        // The framework's assemblies are those the process has loaded: mscorlib, System, System.Core, the netstandard
        // facade the library is built against, and System.Memory, which the touches below make sure of.
        _ = typeof(System.Buffers.ArrayPool<byte>);
        _ = typeof(System.Runtime.InteropServices.MemoryMarshal);
        _ = System.Reflection.Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) && assembly != typeof(Regex).Assembly)
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
        return [.. references];
    }
}