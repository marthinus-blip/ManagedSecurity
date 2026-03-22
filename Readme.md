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

## ⚙️ Configuration Architecture

Sentinel splits its configuration into two distinct concerns to support multi-node distributed swarms:

### 1. `SentinelConfig` (Host/Environment Configuration)
Handles local execution boundaries for a specific physical node.
- **Scope:** Per-Host Deployment.
- **Responsibilities:** 
  - `GovernorPort`: The local port this specific agent binds to to provide access to Vault/API streams.
  - `VaultLocation` & `StorageQuotaGb`: Where to store recordings *on this device*.
  - `LogLevel`: The verbosity of the local console output.

### 2. `OrchestrationConfig` (Swarm/Behavior Configuration)
Defines how an agent behaves and communicates within the distributed swarm.
- **Scope:** Orchestration, computer vision constraints, and networked reporting.
- **Responsibilities:**
  - `CommanderBaseUrl`: The URI of the central Commander node that this Scout should report its telemetry to.
  - `HeartbeatInterval` & `WorkerTimeout`: Ping frequencies to keep the swarm topology map updated.
  - `YoloConfidenceThreshold`: The boundary limit for throwing alerts based on CV hits.

By isolating `CommanderBaseUrl` into `OrchestrationConfig`, Sentinel agents can be run in "Scout-only" mode on edge devices (e.g. Raspberry Pis) and dynamically target a remote Commander node without interfering with their own local host boundaries.

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
- **Docker Daemon:** Explicitly required for running `TestCategory="Integration"` environments (powered by `Testcontainers`) to mathematically simulate PostgreSQL RLS bounds natively without destroying the Host OS catalog.
  - Ensure Docker is running and you have permissions to access the Docker socket:
  ```bash
  sudo systemctl enable --now docker
  sudo chmod 666 /var/run/docker.sock
  ```


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

The project uses `MSTest.Sdk` with `Microsoft.Testing.Platform` for NativeAOT-compatible test execution. Because Sentinel executes heavily asynchronous, multi-node concurrent routines (e.g., SQLite WAL interactions, distributed polling, and Edge network simulated workloads), tests are structurally categorized to protect rigorous build pipelines.

### Test Categories

1. **Standard CI Executions (Default)**
   - Executes automatically via `dotnet test --filter "TestCategory!=Manual"`.
   - Contains mathematically rigid unit tests and decoupled abstractions that safely run in highly parallelized MSBuild thread-pools.
   - Example: Binary payload parsing, cryptographic stream assertions, and byte-shifting bounds natively.

2. **Integration Operations (`[TestCategory("Integration")]`)**
   - Must be invoked explicitly: `dotnet test --filter "TestCategory=Integration"`
   - Launches ephemeral Docker instances (via `Testcontainers.PostgreSql`) to physically validate the data layer and architectural behaviors (e.g., PostgreSQL Row-Level Security injections).
   - Contains tests explicitly marked with `[DoNotParallelize]` to prevent ephemeral Docker instance collision or volatile I/O conflicts locally.

### Commands
```bash
# Run all stable automated CI tests safely (Unit Tests operating purely in RAM)
dotnet test --filter "TestCategory!=Integration"

# Run physical Database/Docker Integration tests sequentially natively
dotnet test --filter "TestCategory=Integration"
```

### Docker Integration Test Setup
The Integration test suite mathematically verifies PostgreSQL Row-Level Security and Concurrent Database behaviors natively. To execute these tests, `Testcontainers.PostgreSql` requires explicit and direct access to your local Docker daemon. If you receive an error stating `Docker is either not running or misconfigured (Permission denied)`, you must explicitly configure your daemon permissions.

**1. Enable Native Daemon Access**
Ensure the Docker daemon is actively running and automatically initialized upon boot:
```bash
sudo systemctl enable --now docker
```

**2. Configure Socket Permissions**
To allow Testcontainers to securely provision ephemeral endpoints natively without `sudo`, you must apply the correct permissions to the Docker socket:
```bash
# Temporarily grant execution context optimally
sudo chmod 666 /var/run/docker.sock

# Or permanently map the user correctly (requires a reboot or logout)
sudo usermod -aG docker $USER
```

**3. Execute the Integration Test**
Once permissions are explicitly granted seamlessly, execute the tests natively safely:
```bash
cd ManagedSecurity.Test
dotnet test --filter "TestCategory=Integration"
```
**4. Execute a Manual "Phisical" Integration Test**
`dotnet run --project ManagedSecurity.Sentinel -- onvif-diag [IP_ADDRESS]`
E.g:
```bash
dotnet run --project ManagedSecurity.Sentinel -- onvif-diag 192.168.8
```



## 📝 License (Multi-License Structure)

The `ManagedSecurity` repository uses a **split per-project licensing model** to completely quarantine the viral nature of the GPL-3.0 machine vision integrations from the permissive core cryptography libraries.

* **MIT License**: `ManagedSecurity.Common` & `ManagedSecurity.Core`
  * These libraries implement the "Perfect 32" protocol and AES-GCM encryption. They contain **zero** dependencies on the YOLO engine and can be freely integrated into proprietary/commercial applications (like the Sentinel Dashboard ecosystem) without GPL contagion.
* **GPL-3.0**: `ManagedSecurity.Orchestration`, `ManagedSecurity.Discovery`, & `ManagedSecurity.Sentinel`
  * These node/agent libraries dynamically link to `sentinel_yolo26_core.so` (Ultralytics YOLO26 C++ interop). To honor the GPL constraints of the upstream Ultralytics repository, the Native execution boundary and the edge agent binaries are strictly licensed under the GPL-3.0.

*Note: Consumer applications (like dashboards) should only ever link against the MIT-licensed `Core` and `Common` DLLs to decrypt streams over the network. Under no circumstances should a proprietary application reference the `Orchestration` or `Sentinel` class libraries directly if they wish to remain uninfected by the GPL.*

- [System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography)
- [Microsoft.AspNetCore.DataProtection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/)

## 🏃‍♂️ Building the Native YOLO Engine 

To compile the `sentinel_yolo26_core.so` interop library from source, you need `g++` and the downloaded ONNX Runtime binaries.

```bash
# Compile the shared library using g++ (with rpath for portable linking)
g++ -shared -fPIC -O3 \
    -I./onnxruntime-linux-x64/include \
    yolo26_interop.cpp \
    -L./onnxruntime-linux-x64/lib -lonnxruntime \
    -Wl,-rpath,'$ORIGIN' \
    -o sentinel_yolo26_core.so
```

### Advanced Dynamic Loader Strategy (Linux)
**Build Automation Sync:** Once compiled via `g++`, MSBuild natively consumes the object via `<None Include="..\sentinel_yolo26_core.so">` and explicitly synchronizes it alongside `libonnxruntime.so.1.17.1` dynamically directly into the `bin/` execution directories on every `dotnet build`. 

The underlying cross-platform YOLO inference module (`sentinel_yolo26_core.so`) is dynamically linked against ONNX Runtime (`libonnxruntime.so.1.17.1`). During execution, `.NET`'s `NativeLibrary.TryLoad` intercepts the load sequence by loading the ONNX Runtime library explicitly via `AppContext.BaseDirectory` *before* loading the primary interop library. Coupled with the `-Wl,-rpath,'$ORIGIN'` build flag, the Linux dynamic linker (`ld.so`) can seamlessly locate chained dependencies inside the local binary folder. This completely eliminates the need for any wrapper scripts modifying `LD_LIBRARY_PATH`, ensuring secure NativeAOT CPU execution loads out-of-the-box perfectly.

## 🏃‍♂️ Running the Sentinel System

The Sentinel ecosystem utilizes a distributed swarm microarchitecture. You will open three terminals to run the system:

### 1. The Sentinel Agent (Edge Worker)
Launch the agent to initialize the Machine Vision engine and the E2EE stream.
```bash
dotnet run --project ManagedSecurity.Sentinel agent 192.168.8 both "password"
```

### 2. The Sentinel Dashboard (Local UI)
Launch the Blazor web frontend, which natively acts as the Command/Control server.
```bash
dotnet run --project ../Sentinel-Dashboard
```

### 3. The Sentinel Gateway (YARP TLS Proxy)
The Dashboard decrypts the live E2EE stream securely via the Web Crypto API, which is strictly enforced natively by all browsers using TLS context. Start the custom configured gateway:
```bash
dotnet run --project ManagedSecurity.Proxy
```
Once all components are booted, open `https://localhost:8443` in your browser.

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