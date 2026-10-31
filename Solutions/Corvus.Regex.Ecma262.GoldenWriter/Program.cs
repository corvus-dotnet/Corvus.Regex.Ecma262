// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Writes, for a fixed sample of the ECMA-262 regular expression test data, what the released version this tool is
// built against writes: the holder its emitter generates for each pattern, under each grammar V8 found it valid in.
//
//   GoldenWriter <data directory> <output directory>
//
// The sample is every 50th record of each file of the data (records marked v8_deviation left out). For each,
// images.tsv holds the flags, the pattern and the image the holder carries; for every 1,000th record, holders.cs holds
// the holder itself and holders.tsv its key. The data-set runner's golden mode reads them with the current library.
// Only RegexEmitter.TryEmit is called, the member a code generator calls, so the tool builds against every release.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Corvus.Ecma262.RegularExpressions.CodeGeneration;

const int ImageEvery = 50;
const int HolderEvery = 1000;

string data = args[0];
string output = args[1];
Directory.CreateDirectory(output);
StringBuilder images = new();
StringBuilder holders = new();
StringBuilder holderKeys = new();
HashSet<(string, int)> seen = [];
int holderCount = 0;
int imageCount = 0;
holders.AppendLine("#nullable enable");
holders.AppendLine("#pragma warning disable");
holders.AppendLine("namespace Golden");
holders.AppendLine("{");
holders.AppendLine("    internal static partial class Holders");
holders.AppendLine("    {");
Regex compiled = new(@"Compiled => new byte\[\]\s*\{([^}]*)\}", RegexOptions.CultureInvariant);

foreach (string file in Directory.GetFiles(data, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
{
    if (Path.GetFileName(file) == "ill-formed-utf8.jsonl")
    {
        // Its records are bytes, not patterns over text.
        continue;
    }

    string[] lines = File.ReadAllLines(file);
    for (int index = 0; index < lines.Length; index += ImageEvery)
    {
        using JsonDocument doc = JsonDocument.Parse(lines[index]);
        JsonElement r = doc.RootElement;
        if ((r.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean()) || !r.TryGetProperty("valid", out JsonElement valid))
        {
            continue;
        }

        string pattern = Unescape(r.GetProperty("pattern").GetRawText());
        string flags = r.TryGetProperty("flags", out JsonElement f) ? f.GetString()! : string.Empty;
        List<string> modes = [];
        if (valid.GetProperty("unicode").GetBoolean())
        {
            modes.Add(flags + "u");
        }

        if (valid.GetProperty("legacy").GetBoolean())
        {
            modes.Add(flags);
        }

        if (valid.TryGetProperty("unicode_sets", out JsonElement sets) && sets.GetBoolean())
        {
            modes.Add(flags + "v");
        }

        foreach (string mode in modes)
        {
            int bits = Flags(mode);
            if (bits < 0 || !seen.Add((pattern, bits)))
            {
                continue;
            }

            string holder = "H" + holderCount.ToString(CultureInfo.InvariantCulture);
            if (!RegexEmitter.TryEmit(pattern, (RegexFlags)bits, RegexEdition.Current, RegexUnicodeVersion.Default, holder, out string? source, out _))
            {
                continue;
            }

            Match m = compiled.Match(source!);
            if (m.Success)
            {
                byte[] image = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(b => byte.Parse(b.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
                images.Append(bits.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Key(pattern)).Append('\t').Append(Convert.ToBase64String(image)).Append('\n');
                imageCount++;
            }

            if (index % HolderEvery == 0)
            {
                foreach (string line in source!.Split('\n'))
                {
                    string trimmed = line.TrimEnd('\r');
                    holders.Append(trimmed.Length == 0 ? string.Empty : "        ").Append(trimmed).Append('\n');
                }

                holderKeys.Append(holder).Append('\t').Append(bits.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Key(pattern)).Append('\n');
                holderCount++;
            }
        }
    }
}

holders.AppendLine("    }");
holders.AppendLine("}");
File.WriteAllText(Path.Combine(output, "images.tsv"), images.ToString());
File.WriteAllText(Path.Combine(output, "holders.cs"), holders.ToString());
File.WriteAllText(Path.Combine(output, "holders.tsv"), holderKeys.ToString());
Console.WriteLine($"{imageCount:N0} images and {holderCount:N0} holders written to {output}");
return 0;

// The pattern as base64 of its UTF-16 code units, which carries a lone surrogate.
static string Key(string pattern) => Convert.ToBase64String(MemoryMarshalBytes(pattern));

static byte[] MemoryMarshalBytes(string s)
{
    byte[] bytes = new byte[s.Length * 2];
    for (int k = 0; k < s.Length; k++)
    {
        bytes[2 * k] = (byte)s[k];
        bytes[(2 * k) + 1] = (byte)(s[k] >> 8);
    }

    return bytes;
}

// The flags as the library numbers them, or -1 for a letter it does not know.
static int Flags(string letters)
{
    int bits = 0;
    foreach (char c in letters)
    {
        int bit = c switch
        {
            'g' => (int)RegexFlags.Global,
            'i' => (int)RegexFlags.IgnoreCase,
            'm' => (int)RegexFlags.Multiline,
            's' => (int)RegexFlags.DotAll,
            'u' => (int)RegexFlags.Unicode,
            'v' => (int)RegexFlags.UnicodeSets,
            'y' => (int)RegexFlags.Sticky,
            'd' => (int)RegexFlags.HasIndices,
            _ => -1,
        };

        if (bit < 0 || (bits & bit) != 0)
        {
            return -1;
        }

        bits |= bit;
    }

    return bits;
}

// A JSON string literal, with any lone surrogate its \u escapes write (which a JSON reader refuses).
static string Unescape(string literal)
{
    StringBuilder sb = new(literal.Length);
    for (int k = 1; k < literal.Length - 1; k++)
    {
        char c = literal[k];
        if (c != '\\')
        {
            sb.Append(c);
            continue;
        }

        c = literal[++k];
        switch (c)
        {
            case 'u':
                sb.Append((char)Convert.ToInt32(literal.Substring(k + 1, 4), 16));
                k += 4;
                break;
            case 'n':
                sb.Append('\n');
                break;
            case 'r':
                sb.Append('\r');
                break;
            case 't':
                sb.Append('\t');
                break;
            case 'b':
                sb.Append('\b');
                break;
            case 'f':
                sb.Append('\f');
                break;
            default:
                sb.Append(c);
                break;
        }
    }

    return sb.ToString();
}
