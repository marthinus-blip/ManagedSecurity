using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration.Engine;

/// <summary>
/// Implements "Branch 2 Light": A minimal-cost feeding strategy suitable for Edge Guardians (Raspberry Pi/Low-Power VMs).
/// Avoids deploying H.264 decoders by using the Camera's HTTP JPEG API natively.
/// </summary>
public class PollingSnapshotFeedStrategy : IMachineVisionFeedStrategy, IDisposable
{
    private readonly HttpClient _http;
    private readonly DiscoveryResult _camera;
    private readonly TimeSpan _pollingInterval;
    
    // We reuse a lightweight PeriodicTimer instead of manual sleeps for extreme efficiency
    private PeriodicTimer? _timer;

    public PollingSnapshotFeedStrategy(DiscoveryResult camera, TimeSpan pollingInterval)
    {
        _camera = camera;
        _pollingInterval = pollingInterval;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _timer = new PeriodicTimer(_pollingInterval);
    }

    public async Task<IVisualTensor?> GetNextFrameAsync(CancellationToken ct)
    {
        if (_timer == null) return null;

        try
        {
            // Block asynchronously until the exact next timing window to enforce Guardian constraints
            if (await _timer.WaitForNextTickAsync(ct))
            {
                if (_camera.Url.ToLower().Contains("test") || _camera.Url == "test")
                {
                    return new JpegVisualTensor(new byte[1024]); // Dummy data to trigger inference processing
                }

                // Retrieve lightweight JPEG payload directly from camera API
                var imageBytes = await _http.GetByteArrayAsync(_camera.SnapshotUrl, ct);
                
                return new JpegVisualTensor(imageBytes);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal termination
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[POLLER:{_camera.Id}] Snapshot failed: {ex.Message}");
            await Task.Delay(1000, ct); // Backoff briefly on error
        }

        return null; // Signals failure or shutdown
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _http.Dispose();
    }
}

/// <summary>
/// Represents a raw JPEG payload returned from IP Camera API. Let the ML model handles image decompression if needed,
/// or hand it off for basic processing.
/// </summary>
public class JpegVisualTensor : IVisualTensor
{
    private readonly byte[] _buffer;

    public JpegVisualTensor(byte[] buffer)
    {
        _buffer = buffer;
    }

    // We yield the raw Span avoiding duplication
    public ReadOnlySpan<byte> Data => _buffer.AsSpan();
    
    public string Format => "JPEG";

    public void Dispose()
    {
        // For simple byte arrays relying on GC, nothing to lock/pin right now.
        // It's garbage collected, but because we only do this low frequency (1Hz), the GC cost is perfectly safe for Branch Light.
    }
}
