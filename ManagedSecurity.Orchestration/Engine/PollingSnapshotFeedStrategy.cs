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
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class PollingSnapshotFeedStrategy : IMachineVisionFeedStrategy, IDisposable
{
    private static readonly Microsoft.Extensions.Logging.ILogger _logger = ManagedSecurity.Common.Logging.SentinelLogger.CreateLogger<PollingSnapshotFeedStrategy>();
    private readonly HttpClient _http;
    private readonly DiscoveryResult _camera;
    private readonly TimeSpan _pollingInterval;
    private readonly ManagedSecurity.Core.Cipher? _cipher;
    
    // We reuse a lightweight PeriodicTimer instead of manual sleeps for extreme efficiency
    private PeriodicTimer? _timer;

    public PollingSnapshotFeedStrategy(DiscoveryResult nodeTarget, TimeSpan pollingInterval, ManagedSecurity.Core.Cipher? cipher = null)
    {
        _camera = nodeTarget;
        _pollingInterval = pollingInterval;
        _cipher = cipher;

        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true };

        // Dynamically parse RFC HTTP URI Basic/Digest Auth securely using Handler Credentials explicitly optimally.
        if (Uri.TryCreate(_camera.SnapshotUrl, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':');
            if (parts.Length == 2)
            {
                handler.Credentials = new System.Net.NetworkCredential(parts[0], parts[1]);
            }
            _camera.SnapshotUrl = new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString(); // Remove credentials securely
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
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
                // Retrieve lightweight payload directly from camera API. We use GetByteArrayAsync 
                // to universally bypass malformed HTTP/1.0 headers from legacy firmware like thttpd/2.25b.
                var imageBytes = await _http.GetByteArrayAsync(_camera.SnapshotUrl, ct);

                // Zero-Trust Route Escalation Check: Ensure we aren't illegally polling an HTML web viewer
                if (imageBytes.Length > 4)
                {
                    // Structurally inspect magic headers for HTML presence instead of relying on broken headers.
                    string magic = System.Text.Encoding.UTF8.GetString(imageBytes, 0, Math.Min(64, imageBytes.Length));
                    if (magic.Contains("<!DOC", StringComparison.OrdinalIgnoreCase) || 
                        magic.Contains("<html", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new NotSupportedException($"Device returned HTML structure natively. Expected raw JPEG magic bytes. Length: {imageBytes.Length}");
                    }
                }
                
                if (_cipher != null && imageBytes.Length > 0)
                {
                    try {
                        imageBytes = _cipher.Decrypt(imageBytes);
                    } catch {
                        // Suppress decrypt failures if it's already plain text
                    }
                }

                return new JpegVisualTensor(imageBytes);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal termination
        }
        catch (NotSupportedException ex)
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[POLLER:{_camera.Id}] Route incompatible: {ex.Message}");
            throw; // Escalate route failure to Inquisitor inherently
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Warning(_logger, $"[POLLER:{_camera.Id}] Firmware brutally aborted HTTP connection natively ({ex.Message}). Triggering Route Escalation...");
            throw new NotSupportedException($"Camera HTTP stack is structurally incompatible with modern polling: {ex.Message}");
        }
        catch (Exception ex)
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[POLLER:{_camera.Id}] Snapshot failed: {ex.Message} -> {ex.InnerException?.Message}");
            await Task.Delay(1000, ct); // Backoff briefly on temporary HTTP error
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
