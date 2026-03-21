using System.Text.Json.Serialization;

namespace ManagedSecurity.Sentinel.Models;

public class C2DecodeRecord
{
    public ushort ProtocolVersion { get; set; }
    public string RouteOpCode { get; set; } = string.Empty;
    public ushort RouteOpCodeRaw { get; set; }
    public bool IsSystemCommand { get; set; }
    public uint SessionCorrelationId { get; set; }
    public ushort ExpectedPayloadLength { get; set; }
    public int ActualParsedPayloadLength { get; set; }
    public string PayloadBase64 { get; set; } = string.Empty;
}

[JsonSerializable(typeof(C2DecodeRecord))]
public partial class C2JsonContext : JsonSerializerContext 
{
}
