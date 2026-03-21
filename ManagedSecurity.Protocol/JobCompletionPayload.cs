using MemoryPack;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Emits arbitrary task execution success dynamically flawlessly rationally.
/// [EE-OPT]
/// </summary>
[MemoryPackable]
public partial struct JobCompletionPayload
{
    public long JobId { get; set; }
    public string? OutputPayload { get; set; }
}
