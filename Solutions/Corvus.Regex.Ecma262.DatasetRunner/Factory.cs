// <copyright file="Factory.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

extern alias Gen;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Corvus.Ecma262.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Makes the regular expressions the run checks: by the constructor, or in the --generated mode from the
/// straight-line code the source generator's emitter writes for each pattern, compiled with Roslyn in batches
/// and loaded, so that the whole data set is the gate for the generated code as it is for the interpreter.
/// </summary>
internal static class Factory
{
    private const int Batch = 300;

    private static readonly ConcurrentDictionary<(string Pattern, RegexFlags Flags), (GeneratedRunner Bounds, GeneratedRunner Captures)> Runners = new();
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    /// <summary>Gets the number of patterns with generated code.</summary>
    public static int Count => Runners.Count;

    /// <summary>A regular expression: with the generated code for its pattern when there is some, by the constructor otherwise.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="limit">The limit of time.</param>
    /// <returns>The regular expression.</returns>
    public static Regex Make(string pattern, RegexFlags flags, TimeSpan limit)
    {
        return Runners.TryGetValue((pattern, Key(flags)), out (GeneratedRunner Bounds, GeneratedRunner Captures) r)
            ? new Regex(pattern, flags, limit, r.Bounds, r.Captures)
            : new Regex(pattern, flags, limit);
    }

    /// <summary>Generates, compiles and loads the code for the patterns of a file's records, one in <paramref name="every"/>.</summary>
    /// <param name="lines">The records.</param>
    /// <param name="every">Take one pattern in this many.</param>
    /// <param name="unescape">Reads a JSON string with any lone surrogate.</param>
    /// <param name="limit">The limit of time, unused by the code but part of the key's shape.</param>
    public static void Prepare(string[] lines, int every, Func<string, string> unescape, TimeSpan limit)
    {
        _ = limit;
        Runners.Clear();
        List<(string Pattern, RegexFlags Flags)> keys = [];
        HashSet<(string, RegexFlags)> seen = [];
        int n = 0;
        foreach (string line in lines)
        {
            if (n++ % every != 0)
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement r = doc.RootElement;
            if (r.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean())
            {
                continue;
            }

            string pattern = unescape(r.GetProperty("pattern").GetRawText());
            string flags = r.GetProperty("flags").GetString()!;
            JsonElement valid = r.GetProperty("valid");
            List<(string Mode, bool IsValid)> modes = [("unicode", valid.GetProperty("unicode").GetBoolean()), ("legacy", valid.GetProperty("legacy").GetBoolean())];
            if (valid.TryGetProperty("unicode_sets", out JsonElement validSets))
            {
                modes.Add(("unicode_sets", validSets.GetBoolean()));
            }

            foreach ((string mode, bool isValid) in modes)
            {
                if (!isValid)
                {
                    continue;
                }

                RegexFlags f = Key(Regex.ParseFlags(mode == "unicode" ? flags + "u" : mode == "unicode_sets" ? flags + "v" : flags));
                if (seen.Add((pattern, f)))
                {
                    keys.Add((pattern, f));
                }
            }
        }

        List<(string Pattern, RegexFlags Flags)[]> batches = [];
        for (int k = 0; k < keys.Count; k += Batch)
        {
            batches.Add(keys.GetRange(k, Math.Min(Batch, keys.Count - k)).ToArray());
        }

        Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = 8 }, Compile);
    }

    /// <summary>The flags that shape the program: the i, m, s, u and v flags; g, y and d do not.</summary>
    private static RegexFlags Key(RegexFlags flags) => flags & (RegexFlags.IgnoreCase | RegexFlags.Multiline | RegexFlags.DotAll | RegexFlags.Unicode | RegexFlags.UnicodeSets);

    private static void Compile((string Pattern, RegexFlags Flags)[] batch)
    {
        StringBuilder sb = new();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0162, CS0164, CS0168, CS0219, CS8321, CS1522");
        sb.AppendLine("namespace G");
        sb.AppendLine("{");
        List<int> emitted = [];
        for (int k = 0; k < batch.Length; k++)
        {
            string runners;
            try
            {
                runners = Gen::Corvus.Ecma262.RegularExpressions.SourceGenerator.RegexGenerator.Runners(batch[k].Pattern, (int)batch[k].Flags);
            }
            catch (Exception)
            {
                // A pattern the generator's copy of the engine refuses: the run constructs it and reports as usual.
                continue;
            }

            emitted.Add(k);
            sb.Append("    internal static class R").Append(k).AppendLine();
            sb.AppendLine("    {");
            sb.AppendLine("        internal static readonly global::Corvus.Ecma262.RegularExpressions.GeneratedRunner B = new Bounds();");
            sb.AppendLine("        internal static readonly global::Corvus.Ecma262.RegularExpressions.GeneratedRunner C = new Captures();");
            foreach (string line in runners.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                sb.Append(trimmed.Length == 0 ? string.Empty : "    ").AppendLine(trimmed);
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        if (emitted.Count == 0)
        {
            return;
        }

        // CORVUS_REGEX_DUMP=<text>: the generated source of every batch holding a pattern with that text is saved.
        string? dumpText = Environment.GetEnvironmentVariable("CORVUS_REGEX_DUMP");
        if (!string.IsNullOrEmpty(dumpText) && Array.Exists(batch, b => b.Pattern.Contains(dumpText, StringComparison.Ordinal)))
        {
            string path = Path.Combine(Path.GetTempPath(), "corvus-regex-dump-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(path, sb.ToString());
            Console.Error.WriteLine("dumped " + path);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "G" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(sb.ToString(), new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: ["NET", "NET8_0_OR_GREATER", "NET9_0_OR_GREATER", "NET10_0", "NET10_0_OR_GREATER"]))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream ms = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(ms);
        if (!result.Success)
        {
            string errors = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5));
            string dump = Path.Combine(Path.GetTempPath(), "corvus-regex-generated-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(dump, sb.ToString());
            throw new InvalidOperationException("Generated code did not compile (saved at " + dump + "):\n" + errors);
        }

        Assembly assembly = Assembly.Load(ms.ToArray());
        foreach (int k in emitted)
        {
            Type type = assembly.GetType("G.R" + k)!;
            GeneratedRunner b = (GeneratedRunner)type.GetField("B", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            GeneratedRunner c = (GeneratedRunner)type.GetField("C", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Runners[batch[k]] = (b, c);
        }
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
        return references.ToImmutableArray();
    }
}
