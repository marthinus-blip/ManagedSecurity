using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Core;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration.Engine;

/// <summary>
/// The "Inquisitor" (Narrow Phase) behavior.
/// Responsible for deep inference on target streams, instantiated dynamically 
/// when a Guardian triggers a high-confidence alert.
/// </summary>
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class InquisitorBehavior : IAgentBehavior
{
    public static event Action<ManagedSecurity.Common.Models.InferenceTelemetryEvent>? OnTelemetryEmitted;
    public static event Action<ManagedSecurity.Discovery.DiscoveryResult, ManagedSecurity.Discovery.MachineVisionRoute>? OnRouteEscalated;
    public string Name => "Inquisitor";
    private static readonly Microsoft.Extensions.Logging.ILogger _logger = ManagedSecurity.Common.Logging.SentinelLogger.CreateLogger<InquisitorBehavior>();
    private readonly string _agentId;
    private readonly OrchestrationConfig _config;
    private readonly Cipher _cipher;
    private bool _isRunning;
    private readonly ConcurrentDictionary<string, DiscoveryResult> _activeTargets = new();
    private readonly IYoloInferenceEngine _yoloEngine;
    
    public bool IsNative => _yoloEngine.IsNative;
    public string EngineVersion => _yoloEngine.EngineVersion;

    public InquisitorBehavior(string agentId, OrchestrationConfig config, Cipher cipher, IYoloInferenceEngine? yoloEngine = null)
    {
        _agentId = agentId;
        _config = config;
        _cipher = cipher;
        _yoloEngine = yoloEngine ?? new Yolo26InferenceEngine(config);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[INQUISITOR] {_agentId} started and standing by for Narrow Phase escalations.");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Attaches to an active stream when a Guardian alert escalates tracking.
    /// </summary>
    public void AcceptTarget(DiscoveryResult target)
    {
        if (_activeTargets.TryAdd(target.Url, target))
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[INQUISITOR] Locking onto target {target.IpAddress} for deep inference.");
            _ = RunInferenceLoopAsync(target);
        }
    }

    public void ReleaseTarget(string cameraUrl)
    {
        if (_activeTargets.TryRemove(cameraUrl, out _))
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[INQUISITOR] Target {cameraUrl} released. Returning to standby.");
        }
    }

    private async Task RunInferenceLoopAsync(DiscoveryResult target)
    {
        try
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[INQUISITOR-ENGINE] Starting YOLO26 hot path sequence for {target.IpAddress}...");
            
            while (_isRunning && _activeTargets.ContainsKey(target.Url))
            {
                IMachineVisionFeedStrategy feedStrategy;
                if (target.MvRoute == ManagedSecurity.Discovery.MachineVisionRoute.LightPlain || target.MvRoute == ManagedSecurity.Discovery.MachineVisionRoute.None)
                {
                    feedStrategy = new PollingSnapshotFeedStrategy(target, TimeSpan.FromMilliseconds(33), _cipher);
                }
                else
                {
                    ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[INQUISITOR-ENGINE] Heavy Plain Machine Vision route active for {target.IpAddress}. Spinning up unified GStreamer/Sentinel decoder natively...");
                    feedStrategy = new HeavyPlainFeedStrategy(target, _config, _cipher);
                }

                try
                {
                    using (feedStrategy)
                    {
                        while (_isRunning && _activeTargets.ContainsKey(target.Url))
                        {
                            using var frame = await feedStrategy.GetNextFrameAsync(CancellationToken.None);
                            if (frame == null || frame.Data.IsEmpty) continue;

                            ManagedSecurity.Common.Logging.SentinelLogger.Debug(_logger, $"[INQUISITOR-ENGINE] Received frame: {frame.Data.Length} bytes ({frame.Format}).");

                            var hits = await _yoloEngine.DetectAsync(frame, CancellationToken.None);
                            var boxes = new ManagedSecurity.Common.Models.BoundingBox[hits.Length];

                            if (hits.Length > 0)
                            {
                                ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[INQUISITOR-ENGINE] YOLO DETECTED: {hits.Length} valid targets on {target.IpAddress} with max conf {hits[0].Confidence:F2}");
                                
                                for (int i = 0; i < hits.Length; i++)
                                {
                                    var h = hits[i];
                                    ManagedSecurity.Common.Logging.SentinelLogger.Debug(_logger, $"Taget {i}: ClassId={h.ClassId}, Conf={h.Confidence:F2}, X={h.X:F3}, Y={h.Y:F3}, W={h.Width:F3}, H={h.Height:F3}");
                                    boxes[i] = new ManagedSecurity.Common.Models.BoundingBox { X = h.X, Y = h.Y, Width = h.Width, Height = h.Height, Confidence = h.Confidence, ClassId = h.ClassId, Label = "Person" };
                                }
                            }

                            OnTelemetryEmitted?.Invoke(new ManagedSecurity.Common.Models.InferenceTelemetryEvent
                            {
                                CameraId = target.Id,
                                TimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                Detections = boxes,
                                IsNative = _yoloEngine.IsNative,
                                EngineVersion = _yoloEngine.EngineVersion
                            });
                        }
                    }
                }
                catch (NotSupportedException ex)
                {
                    ManagedSecurity.Common.Logging.SentinelLogger.Warning(_logger, $"[INQUISITOR-ENGINE] Zero-Trust Route Escalation Triggered ({target.IpAddress}): {ex.Message}");
                    target.MvRoute = ManagedSecurity.Discovery.MachineVisionRoute.HeavyPlain;
                    OnRouteEscalated?.Invoke(target, target.MvRoute);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            ManagedSecurity.Common.Logging.SentinelLogger.ErrorPlain(_logger, $"[INQUISITOR-ENGINE] Inference fault on {target.IpAddress}: {ex.Message}");
            ReleaseTarget(target.Url);
        }
    }
}
