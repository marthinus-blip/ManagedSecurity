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
    public string Name => "Inquisitor";
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
        Console.WriteLine($"[INQUISITOR] {_agentId} started and standing by for Narrow Phase escalations.");
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
            Console.WriteLine($"[INQUISITOR] Locking onto target {target.IpAddress} for deep inference.");
            _ = RunInferenceLoopAsync(target);
        }
    }

    public void ReleaseTarget(string cameraUrl)
    {
        if (_activeTargets.TryRemove(cameraUrl, out _))
        {
            Console.WriteLine($"[INQUISITOR] Target {cameraUrl} released. Returning to standby.");
        }
    }

    private async Task RunInferenceLoopAsync(DiscoveryResult target)
    {
        try
        {
            // [thought_narrow_phase_yolo]((2026-03-15T10:13:28) (Why: Narrow Phase requires full frame processing logic, decoupling network topology from CV logic))
            Console.WriteLine($"[INQUISITOR-ENGINE] Starting YOLO26 hot path sequence for {target.IpAddress}...");
            
            // In a real environment, the target object contains the proper interface for DecryptedStreamFeedStrategy
            // We use PollingSnapshotFeedStrategy here temporarily via the abstract IVisualTensor pipeline to demonstrate decoupling.
            using IMachineVisionFeedStrategy feedStrategy = new PollingSnapshotFeedStrategy(target, TimeSpan.FromMilliseconds(33), _cipher);

            while (_isRunning && _activeTargets.ContainsKey(target.Url))
            {
                using var frame = await feedStrategy.GetNextFrameAsync(CancellationToken.None);
                if (frame == null || frame.Data.IsEmpty) continue;

                Console.WriteLine($"[INQUISITOR-ENGINE] Received frame: {frame.Data.Length} bytes ({frame.Format}).");

                // Pass the frame directly zero-copy to the YOLO engine
                var hits = await _yoloEngine.DetectAsync(frame, CancellationToken.None);

                var boxes = new ManagedSecurity.Common.Models.BoundingBox[hits.Length];
                if (hits.Length > 0)
                {
                    Console.WriteLine($"[INQUISITOR-ENGINE] YOLO DETECTED: {hits.Length} valid targets on {target.IpAddress} with max conf {hits[0].Confidence}");
                    
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var h = hits[i];
                        Console.WriteLine($"[INQUISITOR-DEBUG] Taget {i}: ClassId={h.ClassId}, Conf={h.Confidence:F2}, X={h.X:F3}, Y={h.Y:F3}, W={h.Width:F3}, H={h.Height:F3}");
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
        catch (Exception ex)
        {
            Console.WriteLine($"[INQUISITOR-ENGINE] Inference fault on {target.IpAddress}: {ex.Message}");
            ReleaseTarget(target.Url);
        }
    }
}
