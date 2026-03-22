using System;
using System.IO;
using System.Text.RegularExpressions;

string basePath = "/home/me/Repos/Dotnet/ManagedSecurity/ManagedSecurity.Orchestration";
string[] targets = {
    $"{basePath}/CommanderBehavior.cs",
    $"{basePath}/GuardianBehavior.cs",
    $"{basePath}/DomainBehavior.cs",
    $"{basePath}/Engine/PollingSnapshotFeedStrategy.cs",
    $"{basePath}/CameraStore.cs",
    $"{basePath}/SentinelAgent.cs",
    $"{basePath}/Arbitrator/ArbitratorConnectorBehavior.cs"
};

foreach (var file in targets)
{
    if (!File.Exists(file)) continue;
    string content = File.ReadAllText(file);
    string className = Path.GetFileNameWithoutExtension(file);

    // 1. Check if _logger is defined
    if (!content.Contains("ILogger _logger ="))
    {
        // Find public class ClassName
        var match = Regex.Match(content, @$"public class {className}.*{{");
        if (match.Success)
        {
            string loggerDeclaration = $"\r\n    private static readonly Microsoft.Extensions.Logging.ILogger _logger = ManagedSecurity.Common.Logging.SentinelLogger.CreateLogger<{className}>();";
            content = content.Insert(match.Index + match.Length, loggerDeclaration);
        }
    }

    // 2. Replace Console.WriteLine with SentinelLogger.Info(_logger, ...);
    string pattern = @"Console\.WriteLine\((.*?)\);";
    content = Regex.Replace(content, pattern, "ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $1);");

    File.WriteAllText(file, content);
}
