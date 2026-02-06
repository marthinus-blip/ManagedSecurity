using System;
using System.Collections.Generic;

namespace ManagedSecurity.Common;

public class VaultEntry
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public int ChunkSize { get; set; }
    public int KeyIndex { get; set; }
    public ulong SeekTableOffset { get; set; }
    public string Metadata { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
}
