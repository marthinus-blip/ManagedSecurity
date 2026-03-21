using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Scout;

/// <summary>
/// A formally instantiated BackgroundService bridging WSS natively cleanly exactly. [XEIG-OPT]
/// </summary>
public class ScoutWorker : BackgroundService
{
    private const string TunnelHeaderId = "Agent-Id";
    private const string TargetKey = "EdgeTarget";
    private const string AgentKey = "AgentId";
    private const int BufferAllocationSize = 65536;
    private const int HeartbeatDelayMs = 10000;
    private const int ReconnectDelayMs = 5000;
    private const int MaxPayloadSpanSize = 256;
    private const int MaxJobExecutionMinutes = 60;
    private const string ErrMissingConstraints = "Configuration constraints missing natively (Target/AgentId) .";
    private const string DefaultHeartbeatStatus = "NOMINAL_EDGE_STATE_AOT";
    private const int DefaultHeartbeatCpu = 15;
    private const float DefaultHeartbeatMem = 3.1f;
    
    // Core C2 Logs
    private const string LogBooting = "[SCOUT] Booting structural Edge daemon natively [ESC-OPT]...";
    private const string LogConnecting = "[SCOUT] Initiating C2 outbound WSS interceptor actively...";
    private const string LogConnected = "[SCOUT] Tunnel structurally established [FF-OPT].";
    private const string LogIntercepted = "[SCOUT] Intercepted exactly-once C2 explicitly [OpCode: 0x{0:X4} CorrelationId: {1}] [LS-OPT]";
    private const string LogEvaporated = "[SCOUT] WSS Tunnel evaporated inherently [ESC-OPT]: {0}";
    private const string LogAck = "[SCOUT] Ack processed safely cleanly [ESC-OPT].";
    private const string LogCameras = "[SCOUT] Discovered {0} physical CV streams routed inherently cleanly [FF-OPT]!";

    private readonly IConfiguration _config;
    private readonly ILogger<ScoutWorker> _logger;
    private readonly string _agentId;
    private readonly Uri _edgeUri;
    private readonly System.Collections.Generic.IEnumerable<AgentJobProcessor> _jobProcessors;
    private ClientWebSocket? _activeTunnel;

    public ScoutWorker(IConfiguration config, ILogger<ScoutWorker> logger, System.Collections.Generic.IEnumerable<AgentJobProcessor> jobProcessors)
    {
        _config = config;
        _logger = logger;
        
        var targetConfig = _config[TargetKey];
        var agentIdConfig = _config[AgentKey];
        
        if (string.IsNullOrEmpty(targetConfig) || string.IsNullOrEmpty(agentIdConfig))
        {
            throw new ArgumentException(ErrMissingConstraints);
        }
        
        _agentId = agentIdConfig;
        _edgeUri = new Uri(targetConfig);
        _jobProcessors = jobProcessors;

        foreach (var proc in _jobProcessors)
        {
            proc.TunnelCheckpointDispatcher = DispatchCheckpointAsync;
        }
    }

    private async Task DispatchCheckpointAsync(long jobId, string state, int extensionSeconds)
    {
        if (_activeTunnel?.State != WebSocketState.Open) return;

        var payload = new JobStateUpdatePayload { JobId = jobId, StatePayload = state, RequestedExtensionSeconds = extensionSeconds };
        byte[] buffer = SerializePayload((ushort)SystemOpCode.JobStateUpdate, payload, 0, out int writtenBytes);
        
        try
        {
            await _activeTunnel.SendAsync(new ArraySegment<byte>(buffer, 0, writtenBytes), WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(LogBooting);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var client = new ClientWebSocket();
            client.Options.SetRequestHeader(TunnelHeaderId, _agentId);
            
            try
            {
                _logger.LogInformation(LogConnecting);
                await client.ConnectAsync(_edgeUri, stoppingToken);
                _logger.LogInformation(LogConnected);
                _activeTunnel = client;
                
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                
                var receiveTask = ReceiveLoopAsync(client, linkedCts.Token);
                var heartbeatTask = HeartbeatLoopAsync(client, linkedCts.Token);
                
                await Task.WhenAny(receiveTask, heartbeatTask);
                linkedCts.Cancel();
                _activeTunnel = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(string.Format(LogEvaporated, ex.Message));
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ReconnectDelayMs, stoppingToken); 
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationToken token)
    {
        var buffer = new byte[BufferAllocationSize];
        
        while (client.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) break;

            if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
            {
                // Isolate the ref struct validation into a pure synchronous invocation cleanly explicitly.
                ProcessBinaryFrameSynchronously(buffer, result.Count);
            }
        }
    }

    private void ProcessBinaryFrameSynchronously(byte[] buffer, int count)
    {
        if (ArbitratorFrame.TryParse(new ReadOnlySpan<byte>(buffer, 0, count), out var frame))
        {
            if (frame.OpCode != (ushort)SystemOpCode.CommandAck)
            {
                _logger.LogInformation(string.Format(LogIntercepted, frame.OpCode, frame.CorrelationId));
            }
            
            if (frame.OpCode == (ushort)SystemOpCode.CameraList)
            {
                var cameras = MemoryPack.MemoryPackSerializer.Deserialize<CameraListPayload>(frame.Payload);
                _logger.LogInformation(string.Format(LogCameras, cameras.Cameras?.Length ?? 0));
            }
            else if (frame.OpCode == (ushort)SystemOpCode.JobSubmission)
            {
                var jobPayload = MemoryPack.MemoryPackSerializer.Deserialize<JobSubmissionPayload>(frame.Payload);
                _ = Task.Run(() => SafeExecuteJobAsync(jobPayload));
            }
            else if (frame.OpCode == (ushort)SystemOpCode.CommandAck)
            {
                // Mute verbose Acks to clear noise.
            }
        }
    }

    private async Task SafeExecuteJobAsync(JobSubmissionPayload submission)
    {
        var processor = System.Linq.Enumerable.FirstOrDefault(_jobProcessors, p => p.TargetJobType == submission.JobType);
        if (processor == null || _activeTunnel?.State != WebSocketState.Open) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(MaxJobExecutionMinutes));
            string? result = await processor.ExecuteJobAsync(submission.JobId, submission.Payload, submission.StatePayload, cts.Token);
            
            var completion = new JobCompletionPayload { JobId = submission.JobId, OutputPayload = result };
            byte[] completionBuffer = SerializePayload((ushort)SystemOpCode.JobCompletion, completion, 0, out int writtenLength);
            
            try
            {
                await _activeTunnel.SendAsync(new ArraySegment<byte>(completionBuffer, 0, writtenLength), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(completionBuffer);
            }
        }
        catch(Exception ex)
        {
            var failure = new JobFailurePayload { JobId = submission.JobId, ErrorMessage = ex.Message };
            byte[] failureBuffer = SerializePayload((ushort)SystemOpCode.JobFailure, failure, 0, out int writtenLength);
            
            try
            {
                if (_activeTunnel?.State == WebSocketState.Open)
                    await _activeTunnel.SendAsync(new ArraySegment<byte>(failureBuffer, 0, writtenLength), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(failureBuffer);
            }
        }
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket client, CancellationToken token)
    {
        uint correlationId = 0;
        
        while (client.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            correlationId++;
            
            byte[] multiplexBuffer = SerializeHeartbeatSafely(correlationId, out int writtenBytes);
            byte[] pollBuffer = SerializeEmptyFrameSynchronously((ushort)SystemOpCode.ActiveJobs, 0, out int writtenPollBytes);
            
            try
            {
                await client.SendAsync(new ArraySegment<byte>(multiplexBuffer, 0, writtenBytes), WebSocketMessageType.Binary, true, token);
                await client.SendAsync(new ArraySegment<byte>(pollBuffer, 0, writtenPollBytes), WebSocketMessageType.Binary, true, token);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(multiplexBuffer);
                ArrayPool<byte>.Shared.Return(pollBuffer);
            }
            
            await Task.Delay(HeartbeatDelayMs, token); 
        }
    }

    // A strictly synchronous memory envelope bounding the ref struct natively physically optimally.
    private byte[] SerializeHeartbeatSafely(uint correlationId, out int writtenBytes)
    {
        var heartbeat = new HeartbeatPayload
        {
            CpuLoad = DefaultHeartbeatCpu,
            MemoryUsage = DefaultHeartbeatMem,
            Status = DefaultHeartbeatStatus
        };
        
        var bufferWriter = new ArrayBufferWriter<byte>(MaxPayloadSpanSize);
        MemoryPack.MemoryPackSerializer.Serialize(bufferWriter, heartbeat);
        
        var payloadSpan = bufferWriter.WrittenSpan;
        int totalFrameLength = ArbitratorFrame.HeaderSize + payloadSpan.Length;
        
        byte[] multiplexBuffer = ArrayPool<byte>.Shared.Rent(totalFrameLength);
        
        var frame = new ArbitratorFrame(1, (ushort)SystemOpCode.Heartbeat, correlationId, payloadSpan);
        writtenBytes = frame.WriteTo(multiplexBuffer);
        
        return multiplexBuffer;
    }

    private byte[] SerializePayload<T>(ushort opCode, T payload, uint correlationId, out int writtenBytes) 
    {
        var bufferWriter = new ArrayBufferWriter<byte>(MaxPayloadSpanSize);
        MemoryPack.MemoryPackSerializer.Serialize(bufferWriter, payload);
        
        var payloadSpan = bufferWriter.WrittenSpan;
        int totalFrameLength = ArbitratorFrame.HeaderSize + payloadSpan.Length;
        
        byte[] multiplexBuffer = ArrayPool<byte>.Shared.Rent(totalFrameLength);
        
        var frame = new ArbitratorFrame(1, opCode, correlationId, payloadSpan);
        writtenBytes = frame.WriteTo(multiplexBuffer);
        
        return multiplexBuffer;
    }

    private byte[] SerializeEmptyFrameSynchronously(ushort opCode, uint correlationId, out int writtenBytes) 
    {
        byte[] multiplexBuffer = ArrayPool<byte>.Shared.Rent(ArbitratorFrame.HeaderSize);
        var frame = new ArbitratorFrame(1, opCode, correlationId, ReadOnlySpan<byte>.Empty);
        writtenBytes = frame.WriteTo(multiplexBuffer);
        return multiplexBuffer;
    }
}
