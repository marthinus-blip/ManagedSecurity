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
- `S=00`: **Standard Mode** (AES-GCM, 12B IV, 16B MAC) - Optimized for high-throughput packets.
- `S=01`: **High-Security Mode** (AES-GCM-SIV, 16B IV, 32B MAC) - Nonce-misuse resistant, dual-layer authentication (GCM+HMAC).
- `S=02`: **Streaming Mode** (Sequenced AES-GCM, 8B Seq, 12B IV, 16B MAC) - Anti-replay protection for continuous data streams.

**Variable-Length Encoding:**
- Payloads up to **2047 bytes**: No extension bytes required
- Payloads up to **262 KB**: 1 extension byte
- Payloads up to **33 MB**: 2 extension bytes
- Key indices follow the same extensible pattern

**Message Structure:**
- **S=00/01**: `[Header (4B)] + [Extensions (0-N)] + [IV (12/16B)] + [MAC (16/32B)] + [Payload (L)]`
- **S=02**: `[Header (4B)] + [Extensions (0-N)] + [Seq# (8B)] + [IV (12B)] + [MAC (16B)] + [Payload (L)]`

### ManagedSecurity.Core
Cryptographic engine implementing AES-GCM with header-authenticated encryption.

**Features:**
- `IKeyProvider` abstraction for flexible key management
- `Cipher` class with `Encrypt()` / `Decrypt()` operations (Supports S=0, S=1, S=2)
- `ManagedSecurityStream` for secure file/network streaming with anti-replay protection
- Associated Authenticated Data (AAD) protection for headers and sequence numbers
- Automatic IV generation using cryptographically secure RNG

### ManagedSecurity.Test
Comprehensive test suite with NativeAOT validation.

**Test Coverage:**
- Header parsing and serialization (10 tests)
- Cipher round-trip encryption/decryption (S=0, S=1)
- Stream sequencing and replay protection (S=2)
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
#   total: 14
#   succeeded: 14
```

## 📝 License

GPL-3.0

## 🤝 Contributing

Contributions are welcome! Please ensure:
1. All tests pass with NativeAOT compilation
2. Code follows existing patterns (zero-allocation, Span-based APIs)
3. New features include comprehensive test coverage

## 🔗 Related Projects

- [System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography)
- [Microsoft.AspNetCore.DataProtection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/)

## [ultralytics](https://docs.ultralytics.com/quickstart/)

### 1. Installation
Install the **Ultralytics** package, which contains the YOLO26 engine and CLI tools.

```bash
# Update pip and install the core package
pip install -U pip
pip install ultralytics
```
#### error: externally-managed-environment
This error happens because your operating system (common in recent versions of Ubuntu, Debian, and macOS via Homebrew) is protecting its system-wide Python installation from being broken by pip. Under PEP 668, you are now discouraged from modifying the global environment directly. 
1. The Recommended Way: Use a Virtual Environment 
If you are working on a project, you should create an isolated environment. In a virtual environment, pip is not restricted, and you can upgrade it freely.
```bash
sudo apt install python3.12-venv
```
Create environment: 
```bash
python3 -m venv my_ultralytics
source my_ultralytics/bin/activate
```
### 2. Download Pre-Trained Weights
You don't need a separate download link; calling the model name in Python or CLI will automatically fetch the latest .pt (PyTorch) weights from the official release.
```python
from ultralytics import YOLO

# This command downloads 'yolo26n.pt' to your current directory
model = YOLO("yolo26n.pt")
```

### 3. Export to Native Binary
To compile the weights into a high-performance native format (like TensorRT for NVIDIA GPUs or OpenVINO for Intel CPUs), use the export command.
Option A: Using Python (Recommended)
```python
from ultralytics import YOLO

# Load the model
model = YOLO("yolo26n.pt")

# Export to TensorRT (.engine) for NVIDIA GPUs
model.export(format="engine", device=0)

# OR Export to OpenVINO (.xml/.bin) for Intel CPUs
# model.export(format="openvino")
```
Option B: Using CLI
```bash
# Export to TensorRT
yolo export model=yolo26n.pt format=engine device=0

# Export to NCNN (Best for Android/ARM/Raspberry Pi)
yolo export model=yolo26n.pt format=ncnn
```
### 4. Summary of Common Formats
Target	Format Argument	Output File/Folder
NVIDIA GPU	format="engine"	yolo26n.engine
Intel CPU	format="openvino"	yolo26n_openvino_model/
Android/ARM	format="ncnn"	yolo26n_ncnn_model/
iOS/Mac	format="coreml"	yolo26n.mlpackage