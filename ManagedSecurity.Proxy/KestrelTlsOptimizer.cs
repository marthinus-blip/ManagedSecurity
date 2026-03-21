using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Net.Security;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace ManagedSecurity.Proxy;

/// <summary>
/// Forces zero-trust E2EE boundary optimizations lowering physical hardware AES/ChaCha latency natively.
/// Ensures constrained nodes (Arm32/Arm64) do not computationally throttle over dense SSL/TLS wraps.
/// </summary>
public static class KestrelTlsOptimizer
{
    public static void ConfigureOptimalZeroTrustTls(HttpsConnectionAdapterOptions options)
    {
        options.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            options.OnAuthenticate = (context, sslOptions) =>
            {
                sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                {
                    // TLS 1.3
                    TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                    TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                    
                    // TLS 1.2 optimal fallbacks
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                    TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                    TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                    
                    TlsCipherSuite.TLS_AES_256_GCM_SHA384
                });
            };
        }
    }
}
