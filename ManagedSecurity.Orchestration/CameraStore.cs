using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration;

[ManagedSecurity.Common.Attributes.AllowMagicValues]
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

        var context = new CameraStoreJsonContext(_options);
        string json = JsonSerializer.Serialize(cameras, context.ListDiscoveryResult);
        await File.WriteAllTextAsync(_path, json);
        Console.WriteLine($"[STORE] Saved {cameras.Count} cameras to {_path}. Confirmation: {File.Exists(_path)}");
    }

    public async Task<List<DiscoveryResult>> LoadAsync()
    {
        if (!File.Exists(_path)) return new List<DiscoveryResult>();
        string json = await File.ReadAllTextAsync(_path);
        var context = new CameraStoreJsonContext(_options);
        return JsonSerializer.Deserialize(json, context.ListDiscoveryResult) ?? new List<DiscoveryResult>();
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<ManagedSecurity.Discovery.DiscoveryResult>))]
internal partial class CameraStoreJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
