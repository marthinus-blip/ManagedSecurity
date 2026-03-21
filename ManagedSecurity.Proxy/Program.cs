using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using ManagedSecurity.Orchestration.Arbitrator;

// Generate ephemeral cert for the local network IP so browsers process the TLS handshake
var rsa = RSA.Create(2048);
var request = new CertificateRequest("CN=Sentinel_Gateway", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

var sanBuilder = new SubjectAlternativeNameBuilder();
sanBuilder.AddIpAddress(IPAddress.Parse("192.168.8.35"));
sanBuilder.AddIpAddress(IPAddress.Parse("127.0.0.1"));
sanBuilder.AddDnsName("localhost");
request.CertificateExtensions.Add(sanBuilder.Build());
var cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));

var builder = WebApplication.CreateBuilder(args);

var enableTls = builder.Configuration.GetValue<bool>("ProxyServerSettings:EnableTls", true);
var tlsPort = builder.Configuration.GetValue<int>("ProxyServerSettings:TlsPort", 5001);
var httpPort = builder.Configuration.GetValue<int>("ProxyServerSettings:HttpPort", 5000);

var enforceLightweightCiphers = builder.Configuration.GetValue<bool>("ProxyServerSettings:EnforceLightweightCiphers", true);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(httpPort);

    if (enableTls)
    {
        // 2026-03-18T19:17:01 (Why)
        // [thought TLS Overhead] Data payload is already E2EE; we only need HTTPS for the Secure Context
        // to enable the Web Crypto API on clients. Enforcing light cipher suites minimizes redundant encryption compute overhead.
        serverOptions.ListenAnyIP(tlsPort, listenOptions =>
        {
            listenOptions.UseHttps(cert);
        });
    }
});

builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = (int)HttpStatusCode.TemporaryRedirect;
    options.HttpsPort = tlsPort;
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(b =>
    {
        b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddSingleton<IArbitratorRegistrar, ArbitratorRegistrar>();

var app = builder.Build();

if (enableTls)
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseWebSockets();

app.MapGet("/api/arbitrator/register", async (HttpContext context, IArbitratorRegistrar registrar, string agentId) => 
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        registrar.RegisterTunnel(agentId, webSocket);
        
        var buffer = new byte[1024];
        var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        
        while (!receiveResult.CloseStatus.HasValue)
        {
            receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        }
        
        registrar.RemoveTunnel(agentId);
        await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription, CancellationToken.None);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapReverseProxy();
app.Run();
