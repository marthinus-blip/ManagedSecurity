# ManagedSecurity

A high-performance, NativeAOT-compatible cryptographic library for .NET 8+ featuring a custom binary protocol for secure message encapsulation.

## 🎯 Overview

ManagedSecurity provides a zero-allocation, architecture-agnostic binary format for encrypted data with integrated authentication. The library is designed from the ground up for **NativeAOT compilation**, ensuring maximum performance and minimal runtime overhead.

### Key Features

- **"Perfect 32" Binary Header Protocol**: Extensible 32-bit header format with variable-length encoding
- **Zero-Copy Architecture**: Span-based APIs eliminate unnecessary allocations
- **AES-GCM Encryption**: Industry-standard authenticated encryption with associated data (AEAD)
- **Header Authentication**: Cryptographic binding between header metadata and ciphertext prevents tampering
- **Big-Endian Encoding**: Cross-platform compatibility (x64, ARM64, etc.)
- **NativeAOT Ready**: Full compatibility with ahead-of-time compilation

## 📦 Architecture

### ManagedSecurity.Common
Core binary protocol implementation featuring the "Perfect 32" header format.

**Header Layout (32 bits):**
```
┌─────────┬─────────┬─────────┬─────────┬─────────┬─────────┐
│ Magic   │ Version │ Switch  │Reserved │ Length  │ Key ID  │
│ (3 bit) │ (2 bit) │ (2 bit) │ (1 bit) │ (12 bit)│ (12 bit)│
└─────────┴─────────┴─────────┴─────────┴─────────┴─────────┘
```

**Security Profiles (Switch Bit):**
- `S=00`: AES-GCM Mode (96-bit IV, 128-bit MAC) - Optimized for standard use
- `S=01`: High-Security Mode (128-bit IV, 256-bit MAC) - Reserved for future algorithms

**Variable-Length Encoding:**
- Payloads up to **2047 bytes**: No extension bytes required
- Payloads up to **262 KB**: 1 extension byte
- Payloads up to **33 MB**: 2 extension bytes
- Key indices follow the same extensible pattern

**Message Structure:**
```
[Header (4B)] + [Extensions (0-N)] + [IV (12/16B)] + [MAC (16/32B)] + [Payload (L)]
```

### ManagedSecurity.Core
Cryptographic engine implementing AES-GCM with header-authenticated encryption.

**Features:**
- `IKeyProvider` abstraction for flexible key management
- `Cipher` class with `Encrypt()` / `Decrypt()` operations
- Associated Authenticated Data (AAD) protection for headers
- Automatic IV generation using cryptographically secure RNG

### ManagedSecurity.Test
Comprehensive test suite with NativeAOT validation.

**Test Coverage:**
- Header parsing and serialization (9 tests)
- Cipher round-trip encryption/decryption (2 tests)
- Tamper detection and authentication verification
- Data protection API integration

## 🚀 Quick Start

### Installation

```bash
# Add packages from local feed (or NuGet when published)
dotnet add package ManagedSecurity.Common
dotnet add package ManagedSecurity.Core
```

### Basic Usage

```csharp
using ManagedSecurity.Core;
using ManagedSecurity.Common;

// 1. Implement a key provider
public class SimpleKeyProvider : IKeyProvider
{
    private readonly byte[] _key = new byte[32]; // 256-bit AES key
    
    public SimpleKeyProvider()
    {
        RandomNumberGenerator.Fill(_key);
    }
    
    public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
}

// 2. Encrypt data
var keyProvider = new SimpleKeyProvider();
var cipher = new Cipher(keyProvider);

byte[] plaintext = Encoding.UTF8.GetBytes("Secret Message");
byte[] encrypted = cipher.Encrypt(plaintext, keyIndex: 0);

// 3. Decrypt data
byte[] decrypted = cipher.Decrypt(encrypted);
string message = Encoding.UTF8.GetString(decrypted);
```

### Inspecting the Binary Format

```csharp
var header = new Bindings.Header(encrypted);

Console.WriteLine($"Payload Length: {header.PayloadLength}");
Console.WriteLine($"Key Index: {header.KeyIndex}");
Console.WriteLine($"IV Length: {header.IvLength} bytes");
Console.WriteLine($"MAC Length: {header.MacLength} bytes");
Console.WriteLine($"Total Message Size: {header.TotalLength} bytes");
```

## 🛠️ Building from Source

### Prerequisites
- .NET 8 SDK or later
- Linux: `zlib` development headers

### Linux NativeAOT Build

```bash
# 1. Setup zlib symlink (Linux only)
mkdir -p local_lib
ln -s /usr/lib/x86_64-linux-gnu/libz.so.1 local_lib/libz.so

# 2. Build and pack libraries
dotnet build ManagedSecurity.Common -c Release
dotnet build ManagedSecurity.Core -c Release
dotnet pack ManagedSecurity.Common -c Release --no-build -o ./local-packages
dotnet pack ManagedSecurity.Core -c Release --no-build -o ./local-packages

# 3. Publish test suite as NativeAOT binary
LIBRARY_PATH=$LIBRARY_PATH:$(pwd)/local_lib \
dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj \
  -c Release -r linux-x64 --self-contained true /p:PublishAot=true

# 4. Run tests
./run-tests-all.sh
```

### Windows NativeAOT Build

```powershell
dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj `
  -c Release -r win-x64 --self-contained true /p:PublishAot=true
```

## 🔒 Security Considerations

### Header Authentication (AAD)
The library uses **Associated Authenticated Data** to cryptographically bind the header to the ciphertext. Any modification to:
- Magic number
- Version
- Security profile
- Payload length
- Key index

...will cause decryption to fail with `AuthenticationTagMismatchException`.

### Endianness
All multi-byte values use **Big-Endian** (network byte order) encoding, ensuring cross-platform compatibility.

### Key Management
The `IKeyProvider` interface allows you to implement:
- Hardware Security Module (HSM) integration
- Azure Key Vault / AWS KMS backends
- In-memory key rotation
- Per-user or per-tenant key isolation

## 📊 Performance

- **Zero-allocation parsing**: Header reads use `ReadOnlySpan<byte>` without heap allocations
- **NativeAOT compilation**: ~10ms startup time vs. ~200ms for JIT
- **Minimal overhead**: 4-byte header + 12-byte IV + 16-byte MAC = 32 bytes for small messages

## 🧪 Testing

The project uses `MSTest.Sdk` with `Microsoft.Testing.Platform` for NativeAOT-compatible test execution.

```bash
# Run all tests (11 total)
./run-tests-all.sh

# Expected output:
# Test run summary: Passed!
#   total: 11
#   succeeded: 11
```

## 📝 License

MIT License - See LICENSE file for details.

## 🤝 Contributing

Contributions are welcome! Please ensure:
1. All tests pass with NativeAOT compilation
2. Code follows existing patterns (zero-allocation, Span-based APIs)
3. New features include comprehensive test coverage

## 🔗 Related Projects

- [System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography)
- [Microsoft.AspNetCore.DataProtection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/)