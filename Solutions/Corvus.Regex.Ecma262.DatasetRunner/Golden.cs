// <copyright file="Golden.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using Corvus.Ecma262.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// The golden check (--golden): what a released version wrote, read by this library (docs/plan/image-compatibility.md).
/// The released version's GoldenWriter writes, for a fixed sample of the data, the image of each pattern and, for a
/// smaller sample, the whole holder its emitter generates. Here every image must be read, unless it is from a revision
/// below the trusted one, when it must be ignored. Every holder must compile against this library. Every regular
/// expression made from them must answer as V8 does. And an image of this engine's own revision must hold what this
/// engine writes now.
/// </summary>
internal static class Golden
{
    private static readonly Dictionary<(string Pattern, RegexFlags Flags), byte[]> Images = [];
    private static readonly Dictionary<(string Pattern, RegexFlags Flags), Func<Regex>> Holders = [];

    /// <summary>The images and holders read, or ignored as they had to be.</summary>
    public static long Read;

    /// <summary>Gets the failures: an image or a holder not read, output changed under an unchanged revision, or a holder that did not compile.</summary>
    public static ConcurrentQueue<string> Failures { get; } = new();

    /// <summary>Gets a value indicating whether the golden check is on.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Loads what a released version wrote, compiles its holders, and compares its images with this engine's output.</summary>
    /// <param name="directory">The GoldenWriter's output for one release.</param>
    /// <param name="references">The references to compile the holders against.</param>
    /// <param name="symbols">The preprocessor symbols of this build.</param>
    public static void Load(string directory, IEnumerable<MetadataReference> references, string[] symbols)
    {
        Enabled = true;
        int same = 0;
        foreach (string line in File.ReadAllLines(Path.Combine(directory, "images.tsv")))
        {
            string[] cells = line.Split('\t');
            RegexFlags flags = (RegexFlags)int.Parse(cells[0], CultureInfo.InvariantCulture);
            string pattern = Pattern(cells[1]);
            byte[] image = Convert.FromBase64String(cells[2]);
            Images[(pattern, flags)] = image;
            if (Changed(pattern, flags, image, out string? why))
            {
                Failures.Enqueue($"GOLDEN-OUTPUT-CHANGED\t/{pattern}/{flags}\t{why}");
            }
            else
            {
                same++;
            }
        }

        Console.WriteLine($"golden {Path.GetFileName(directory.TrimEnd('/', '\\'))}: {Images.Count:N0} images, {same:N0} as this engine writes them");
        LoadHolders(directory, references, symbols);
    }

    /// <summary>The regular expression the release wrote for a pattern, or null when it wrote none in the sample.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="limit">The limit of time.</param>
    /// <returns>The regular expression, or null.</returns>
    public static Regex? Make(string pattern, RegexFlags flags, TimeSpan limit)
    {
        if (!Images.TryGetValue((pattern, flags), out byte[]? image))
        {
            return null;
        }

        // An image from below the trusted revision must be ignored, and every other image read.
        bool trusted = RegexImage.RevisionOf(image) >= RegexImage.MinimumTrustedRevision;
        return Holders.TryGetValue((pattern, flags), out Func<Regex>? holder)
            ? Check(holder(), pattern, flags, "holder", trusted)
            : Check(new Regex(pattern, flags, limit, Factory.Edition, Factory.UnicodeVersion, null, null, image), pattern, flags, "image", trusted);
    }

    private static Regex Check(Regex regex, string pattern, RegexFlags flags, string what, bool trusted)
    {
        if (regex.ReadFromImage == trusted)
        {
            Interlocked.Increment(ref Read);
        }
        else
        {
            Failures.Enqueue(trusted ? $"GOLDEN-NOT-READ\t{what}\t/{pattern}/{flags}" : $"GOLDEN-UNTRUSTED-READ\t{what}\t/{pattern}/{flags}");
        }

        return regex;
    }

    /// <summary>
    /// Whether the release's image of a pattern holds other than what this engine writes, under this engine's own
    /// revision: changed output with no new revision, so no one has decided whether the floor must rise.
    /// </summary>
    private static bool Changed(string pattern, RegexFlags flags, byte[] image, out string? why)
    {
        why = null;
        RegexUnicodeVersion version = UnicodeTablesResolve();
        if (!RegexImage.TryRead(image, flags, Factory.Edition, version, out RegexImage.Parts old, out RegexImage.Ignored ignored))
        {
            // Not read at all: the run reports it when the pattern is matched.
            return false;
        }

        if (old.Revision != RegexImage.CompilerRevision)
        {
            return false;
        }

        byte[] now = RegexImage.Write(pattern, flags, Factory.Edition, Factory.UnicodeVersion);
        if (!RegexImage.TryRead(now, flags, Factory.Edition, version, out RegexImage.Parts current, out ignored))
        {
            why = "this engine's own image was not read (" + ignored + ")";
            return true;
        }

        if (!RegexImage.Canonical(old).AsSpan().SequenceEqual(RegexImage.Canonical(current)))
        {
            why = $"revision {old.Revision}: raise RegexImage.CompilerRevision, and MinimumTrustedRevision too if the change fixes wrong answers (docs/contributing/compatibility.md)";
            return true;
        }

        return false;
    }

    private static RegexUnicodeVersion UnicodeTablesResolve() => new Regex(string.Empty, RegexFlags.None, Regex.InfiniteMatchTimeout, Factory.Edition, Factory.UnicodeVersion).UnicodeVersion;

    private static void LoadHolders(string directory, IEnumerable<MetadataReference> references, string[] symbols)
    {
        string[] keys = File.ReadAllLines(Path.Combine(directory, "holders.tsv"));
        if (keys.Length == 0)
        {
            return;
        }

        StringBuilder index = new();
        index.AppendLine("namespace Golden");
        index.AppendLine("{");
        index.AppendLine("    internal static partial class Holders");
        index.AppendLine("    {");
        index.AppendLine("        internal static global::Corvus.Ecma262.RegularExpressions.Regex Get(string name) => name switch");
        index.AppendLine("        {");
        foreach (string key in keys)
        {
            string name = key.Split('\t')[0];
            index.Append("            \"").Append(name).Append("\" => ").Append(name).AppendLine(".Instance,");
        }

        index.AppendLine("            _ => throw new global::System.ArgumentException(name),");
        index.AppendLine("        };");
        index.AppendLine("    }");
        index.AppendLine("}");

        CSharpParseOptions options = new(LanguageVersion.Preview, preprocessorSymbols: symbols);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Golden" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory, "holders.cs")), options), CSharpSyntaxTree.ParseText(index.ToString(), options)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream ms = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(ms);
        if (!result.Success)
        {
            foreach (Diagnostic d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20))
            {
                Failures.Enqueue($"GOLDEN-HOLDER-DOES-NOT-COMPILE\t{d}");
            }

            return;
        }

        Assembly assembly = Assembly.Load(ms.ToArray());
        MethodInfo get = assembly.GetType("Golden.Holders")!.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (string key in keys)
        {
            string[] cells = key.Split('\t');
            string name = cells[0];
            Holders[(Pattern(cells[2]), (RegexFlags)int.Parse(cells[1], CultureInfo.InvariantCulture))] = () => (Regex)get.Invoke(null, [name])!;
        }

        Console.WriteLine($"golden: {keys.Length:N0} holders compiled against this library");
    }

    private static string Pattern(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        char[] chars = new char[bytes.Length / 2];
        for (int k = 0; k < chars.Length; k++)
        {
            chars[k] = (char)(bytes[2 * k] | (bytes[(2 * k) + 1] << 8));
        }

        return new string(chars);
    }
}
