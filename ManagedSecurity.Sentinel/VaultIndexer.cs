using System;
using System.IO;
using System.Text;
using System.Linq;
using ManagedSecurity.Common;


namespace ManagedSecurity.Sentinel;

public static class VaultIndexer
{
    public static IEnumerable<VaultEntry> ScanDirectory(string path)
    {
        if (!Directory.Exists(path)) yield break;

        var files = Directory.EnumerateFiles(path, "*.msg").Concat(Directory.EnumerateFiles(path, "*.bin"));
        foreach (var file in files)
        {
            var entry = TryGetEntry(file);
            if (entry != null) yield return entry;
        }

    }

    public static VaultEntry? TryGetEntry(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> headerData = stackalloc byte[MasterHeader.FixedSize];
            if (fs.Read(headerData) < MasterHeader.FixedSize) return null;

            var header = new MasterHeader(headerData);
            string metadata = string.Empty;

            if (header.MetadataLength > 0)
            {
                byte[] metaBuffer = new byte[header.MetadataLength];
                fs.Read(metaBuffer);
                metadata = Encoding.UTF8.GetString(metaBuffer);
            }

            var entry = new VaultEntry
            {
                FileName = Path.GetFileName(filePath),
                FullPath = filePath,
                ChunkSize = header.ChunkSize,
                KeyIndex = header.KeyIndex,
                SeekTableOffset = header.SeekTableOffset,
                Metadata = metadata
            };

            // Parse semi-colon tags: Key=Value;Key2=Value2
            foreach (var part in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eqIndex = part.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = part.Substring(0, eqIndex).Trim();
                    string val = part.Substring(eqIndex + 1).Trim();
                    entry.Tags[key] = val;
                }
            }

            return entry;
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<VaultEntry> Search(IEnumerable<VaultEntry> entries, string query)
    {
        // Simple case-insensitive search across FileName, Metadata, and Tags
        string q = query.ToLowerInvariant();
        foreach (var entry in entries)
        {
            if (entry.FileName.ToLowerInvariant().Contains(q) ||
                entry.Metadata.ToLowerInvariant().Contains(q))
            {
                yield return entry;
                continue;
            }

            foreach (var tag in entry.Tags)
            {
                if (tag.Key.ToLowerInvariant().Contains(q) || tag.Value.ToLowerInvariant().Contains(q))
                {
                    yield return entry;
                    break;
                }
            }
        }
    }
}
