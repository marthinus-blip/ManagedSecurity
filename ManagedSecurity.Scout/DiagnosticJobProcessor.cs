using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ManagedSecurity.Scout;

public sealed class DiagnosticJobProcessor : AgentJobProcessor
{
    private readonly ILogger<DiagnosticJobProcessor> _logger;
    public const string TypeIdentifier = "DiagnosticSweep";

    public DiagnosticJobProcessor(ILogger<DiagnosticJobProcessor> logger)
    {
        _logger = logger;
    }

    private const string LogCommencing = "[DIAGNOSTICS] Commencing sweep for job {0}. Payload: {1}";
    private const string LogPhase = "[DIAGNOSTICS] Executing phase {0}/{1}...";
    private const string LogCompleted = "[DIAGNOSTICS] Job {0} successfully completed natively.";
    private const string CodeSuccess = "SUCCESS_CODE_0";
    private const int TotalSegments = 3;
    private const int SimulatedIoDelayMs = 2000;
    
    public override string TargetJobType => TypeIdentifier;

    public override async Task<string?> ExecuteJobAsync(long jobId, string workloadPayload, string? genericStateContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation(string.Format(LogCommencing, jobId, workloadPayload));

        int startSegment = string.IsNullOrEmpty(genericStateContext) ? 0 : int.Parse(genericStateContext);

        for (int i = startSegment; i < TotalSegments; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(string.Format(LogPhase, i + 1, TotalSegments));
            await Task.Delay(SimulatedIoDelayMs, cancellationToken); 

            await CheckpointStateAsync(jobId, (i + 1).ToString(), DefaultCheckpointExtensionSeconds);
        }

        _logger.LogInformation(string.Format(LogCompleted, jobId));
        return CodeSuccess;
    }
}
