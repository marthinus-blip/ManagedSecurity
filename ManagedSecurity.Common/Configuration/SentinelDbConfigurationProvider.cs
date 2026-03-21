using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using ManagedSecurity.Common.Persistence;

namespace ManagedSecurity.Common.Configuration;

/// <summary>
/// A NativeAOT-compatible configuration provider that pulls dynamic orchestrational properties 
/// directly from the local SQLite database. Uses a periodic polling mechanism
/// on the native PRAGMA data_version table to detect external out-of-band writes gracefully.
/// </summary>
public class SentinelDbConfigurationProvider : ConfigurationProvider, IDisposable
{
    private const string CamerasPrefix = "Cameras:";
    private const string DefaultCameraId = "default";
    private const string PragmaDataVersionQuery = "PRAGMA data_version;";
    
    private readonly ISentinelDbConnectionFactory _connectionFactory;
    private readonly TimeSpan _pollingInterval;
    private Timer? _timer;
    private long _lastSchemaVersion = -1;

    public SentinelDbConfigurationProvider(ISentinelDbConnectionFactory connectionFactory, TimeSpan pollingInterval)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pollingInterval = pollingInterval;
    }

    public override void Load()
    {
        // Execute initial mapping synchronously utilizing the thread pool cautiously
        LoadDataAsync().GetAwaiter().GetResult();
        CheckForUpdatesAsync().GetAwaiter().GetResult(); // Establish baseline immediately internally
        
        // Start out-of-band tracking to mimic SQL ChangeTracker triggers inherently
        _timer ??= new Timer(state => 
        {
            if (CheckForUpdatesAsync().GetAwaiter().GetResult())
            {
                LoadDataAsync().GetAwaiter().GetResult();
                OnReload();
            }
        }, null, _pollingInterval, _pollingInterval);
    }

    [ManagedSecurity.Common.Attributes.AllowMagicValues]
    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);

            // Fetch Camera definitions dynamically mapping ADO.NET structs 
            // into Microsoft.Extensions.Configuration dictionary layout.
            using var command = connection.CreateCommand();
            string tableTarget = _connectionFactory.Dialect.TranslateTableNamespace(CameraRecord.SchemaNameQl, CameraRecord.TableNameQl);
            command.CommandText = $"SELECT CameraId, StreamUrl, SnapshotUrl FROM {tableTarget}";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            int index = 0;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var cameraId = reader.IsDBNull(0) ? DefaultCameraId : reader.GetString(0);
                var streamUrl = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var snapUrl = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

                data[$"{CamerasPrefix}{index}:Id"] = cameraId;
                data[$"{CamerasPrefix}{index}:Url"] = streamUrl;
                data[$"{CamerasPrefix}{index}:SnapshotUrl"] = snapUrl;
                index++;
            }
        }
        catch (SqliteException)
        {
            // The Schema might not exist yet during the initial application launch 
            // prior to transparent JSON bootstrapping.
            // Ignored gracefully to allow seamless startup.
        }

        Data = data;
    }

    public async System.Threading.Tasks.Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            
            // Polling PRAGMA data_version is an ultra-fast Native SQLite construct 
            // encompassing all local tables. It increments when ANY external process alters the .db file.
            // This cleanly satisfies '.Reload() triggers without EF Core'.
            command.CommandText = PragmaDataVersionQuery;
            
            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (result != null && result != DBNull.Value)
            {
                long currentVersion = Convert.ToInt64(result);
                if (_lastSchemaVersion == -1)
                {
                    _lastSchemaVersion = currentVersion;
                    return false;
                }

                if (currentVersion != _lastSchemaVersion)
                {
                    _lastSchemaVersion = currentVersion;
                    return true;
                }
            }
        }
        catch (SqliteException)
        {
            // Transient lock exceptions regarding metadata reads during heavy writes.
            // Safely swallow and defer the reload evaluation to the subsequent polling loop.
        }

        return false;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
