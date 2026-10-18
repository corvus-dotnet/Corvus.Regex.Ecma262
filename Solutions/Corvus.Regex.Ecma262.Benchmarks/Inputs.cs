// <copyright file="Inputs.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.IO.Compression;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions.Benchmarks;

/// <summary>
/// The texts the industry benchmarks search: the same files dotnet/performance's regex benchmarks read, fetched
/// from that repository on first use into a cache under the user's profile. They are not vendored: their licences
/// are in dotnet/performance's THIRD-PARTY-NOTICES.TXT beside them, and the benchmarks here are comparable with
/// .NET's own only when they read the same bytes.
/// </summary>
internal static class Inputs
{
    private const string Source = "https://raw.githubusercontent.com/dotnet/performance/main/src/benchmarks/micro/libraries/System.Text.RegularExpressions/TestData/";

    private static readonly Dictionary<string, string> Texts = [];
    private static readonly Dictionary<string, byte[]> Bytes = [];

    /// <summary>The text of a file, as a string.</summary>
    /// <param name="name">The file, such as <c>sherlock.txt.gz</c>.</param>
    /// <returns>The text.</returns>
    public static string Text(string name)
    {
        lock (Texts)
        {
            if (!Texts.TryGetValue(name, out string? text))
            {
                text = Encoding.UTF8.GetString(Utf8(name));
                Texts[name] = text;
            }

            return text;
        }
    }

    /// <summary>The text of a file, as UTF-8.</summary>
    /// <param name="name">The file.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Utf8(string name)
    {
        lock (Bytes)
        {
            if (!Bytes.TryGetValue(name, out byte[]? bytes))
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "corvus-regex-benchmarks");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, name);
                if (!File.Exists(path))
                {
                    using HttpClient client = new();
                    byte[] gz = client.GetByteArrayAsync(Source + name).GetAwaiter().GetResult();
                    File.WriteAllBytes(path, gz);
                }

                using FileStream fs = File.OpenRead(path);
                using GZipStream unzip = new(fs, CompressionMode.Decompress);
                using MemoryStream ms = new();
                unzip.CopyTo(ms);
                bytes = ms.ToArray();

                // The files carry a byte order mark in places; dotnet/performance reads them through a StreamReader,
                // which drops it.
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    bytes = bytes[3..];
                }

                Bytes[name] = bytes;
            }

            return bytes;
        }
    }
}
