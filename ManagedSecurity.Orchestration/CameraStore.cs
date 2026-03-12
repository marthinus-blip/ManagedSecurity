using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration;

public class CameraStore
{
    private readonly string _path;
    private JsonSerializerOptions _options = new() { WriteIndented = true };

    public void SetOptions(JsonSerializerOptions options)
    {
        _options = options;
    }

    public CameraStore(string path)
    {
        _path = path;
    }

    public async Task SaveAsync(List<DiscoveryResult> cameras)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(cameras, _options);
        await File.WriteAllTextAsync(_path, json);
        Console.WriteLine($"[STORE] Saved {cameras.Count} cameras to {_path}. Confirmation: {File.Exists(_path)}");
    }

    public async Task<List<DiscoveryResult>> LoadAsync()
    {
        if (!File.Exists(_path)) return new List<DiscoveryResult>();
        string json = await File.ReadAllTextAsync(_path);
        return JsonSerializer.Deserialize<List<DiscoveryResult>>(json, _options) ?? new List<DiscoveryResult>();
    }
}
