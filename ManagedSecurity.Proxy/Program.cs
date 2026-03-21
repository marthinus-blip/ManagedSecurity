using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using ManagedSecurity.Orchestration.Arbitrator;

// Generate ephemeral cert for the local network IP so browsers process the TLS handshake
using System.Text;
using ManagedSecurity.Common.Persistence;
using ManagedSecurity.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
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
        // 2026-03-22T01:05:01 (Why)
        // [thought TLS Overhead] Data payload is already E2EE; we only need HTTPS for the Secure Context
        // to enable the Web Crypto API on clients. Enforcing light cipher suites minimizes redundant encryption compute overhead natively.
        serverOptions.ListenAnyIP(tlsPort, listenOptions =>
        {
            listenOptions.UseHttps(cert, httpsOptions => 
            {
                if (enforceLightweightCiphers)
                {
                    ManagedSecurity.Proxy.KestrelTlsOptimizer.ConfigureOptimalZeroTrustTls(httpsOptions);
                }
            });
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

var jwtKey = builder.Configuration["JwtSettings:Key"] ?? "sentinel-local-dev-key-zero-trust-boundary-32-byte-secret";
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "SentinelMaster";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "SentinelClients";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// Data & Security Layer Injections natively structurally mapping 
builder.Services.AddSingleton<ISentinelDbConnectionFactory>(sp => new SentinelDbConnectionFactory(SentinelDbConnectionFactory.DefaultConnectionString));
builder.Services.AddTransient<ITenantProvider, SentinelDbTenantProvider>();
builder.Services.AddTransient<IUserProvider, SentinelDbUserProvider>();
builder.Services.AddTransient<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddTransient<IAuthenticationService, AuthenticationService>();

builder.Services.AddSingleton<IArbitratorRegistrar, ArbitratorRegistrar>();

var app = builder.Build();

if (enableTls)
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
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

// Formal Authentication Route Native Mapping smoothly elegantly cleanly
app.MapPost("/api/auth/login", async (HttpContext context, IAuthenticationService authService, [FromBody] ManagedSecurity.Common.Security.LoginRequest req) =>
{
    var token = await authService.AuthenticateAsync(req.EmailAddress, req.Password, req.TenantId, context.RequestAborted);
    if (token == null)
    {
        return Results.Unauthorized();
    }
    return Results.Ok(new { Token = token });
});

app.MapReverseProxy();
app.Run();
