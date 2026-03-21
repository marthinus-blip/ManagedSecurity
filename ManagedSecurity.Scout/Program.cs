using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ManagedSecurity.Scout;

public class Program
{
    private const string OsSentinelService = "SentinelEdgeScout";

    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseSystemd()
            .UseWindowsService(options => 
            {
                options.ServiceName = OsSentinelService;
            })
            .ConfigureLogging(logging => 
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<AgentJobProcessor, DiagnosticJobProcessor>();
                services.AddHostedService<ScoutWorker>();
            })
            .Build();

        await host.RunAsync();
    }
}
