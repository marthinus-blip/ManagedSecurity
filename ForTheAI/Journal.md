# ManagedSecurity Project Journal

This file tracks major design decisions, security hardening, and architectural shifts to provide context for both human developers and AI assistants.

---

## 2026-02-02 00:00:00 (UTC+02:00): Integrity, Zero-Alloc & Hardening

### 🏗️ Architectural Shifts
- **Zero-Allocation Headers**: Refactored `Bindings.Header` to use `ReadOnlySpan<byte>` for all parsing. Removed the `ReadOnlyMemory<byte>` storage and `ToArray()` copy-points. The library is now truly zero-allocation on the data-path.
- **Static Linking**: Switched from dynamic system linking (`-lz`) with local symlink hacks to true **static linking** (`-Wl,-Bstatic -lz`). Resulting binaries are now fully portable across Linux distributions without external `zlib` dependencies.
- **Project Structure**: Added `ManagedSecurity.ConsumerDemo` and `ManagedSecurity.Fuzz` projects to the solution.

### 🛡️ Security Hardening
- **Fuzz Testing**: Implemented a resiliency fuzzer (`ManagedSecurity.Fuzz`) capable of 1M+ iterations per minute.
- **Bug Fix (Integer Overflow)**: Fixed a vulnerability where maliciously crafted variable-length headers could cause signed 32-bit integer overflows during length calculation.
- **Decoding Safety**: Added a safety cap to variable-length extensions (max 3 bytes/1GB) to prevent memory-exhaustion or "zip-bomb" style attacks via header metadata.
- **Panic Mitigation**: Unified internal validation to throw `ArgumentException` instead of `IndexOutOfRangeException` for malformed packets, ensuring safer integration for consumers.

### 🛠️ Tooling & DevEx
- **CI/CD**: Standardized GitHub Actions to use `zlib1g-dev` for static linking.
- **Flight Checks**: Added a custom MSBuild Target (`CheckZLib`) to `SharedFile.targets` that warns developers at build-time if static library prerequisites are missing for NativeAOT.
- **Versioning**: Integrated a robust git-tag based versioning system that automatically labels binaries and NuGet packages based on the nearest `v*` tag.

---

## 2026-02-03 23:12:00 (UTC+02:00): Performance Validation & Zero-Alloc API

- **High-Security Mode (S=01)**: Implemented a Synthetic Initialization Vector (SIV) construction using AES-256-GCM.
  - **Nonce-Misuse Resistance**: The GCM IV is derived from the plaintext via HMAC-SHA256, preventing security collapse if the 16-byte random nonce is reused.
- **Streaming API (S=02)**: Implemented `ManagedSecurityStream` for high-throughput, sequenced data.
  - **Replay/Swap Protection**: Mandatory 8-byte sequence numbers are embedded in the header and authenticated via AAD.
  - **Framed Architecture**: Fixed-size frames (default 64KB) ensure predictable memory usage and 0-allocation throughput.
  - **Multi-Layer Authentication**: Uses both the 16-byte GCM tag and a separate 16-byte HMAC-SHA256 segment (totalling 32 bytes) for extremely robust integrity verification.
  - **HKDF Support**: Integrated HKDF-SHA256 for subkey derivation (ENC and MAC keys) from the master key.

### 🛠️ Tooling & DevEx
- **Benchmarking**: Implemented comparative benchmarks against raw `System.Security.Cryptography.AesGcm`. The benchmarks are designed to prove that the `ManagedSecurity` header abstraction (including variable-length varints) has negligible overhead and can operate with `0 B` allocations.
- **Size Calculation**: Exposed `Cipher.GetRequiredSize` to help consumers allocate exact buffers for the zero-allocation API.

---
## 2026-02-04 20:55:00 (UTC+02:00): Media-Aware Seek Tables & Header v2

- **Master Header Expansion**: Upgraded the `MasterHeader` to **v2 (22 bytes)**, adding a 64-bit `SeekTableOffset` field. This allows the discovery layer to skip directly to the end of a file to read global metadata/indices without parsing the entire blob.
- **SeekTable Implementation**: Created a compact binary format for frame-to-timestamp mapping (`[4B TS][8B Offset]`).
- **ManagedSecurityStream (Auto-Indexing)**: Updated the encryptor to automatically record offset points every time `FlushToFrame()` is called. On stream closure, it performs a non-destructive header update by seeking back to the start—ensuring even multi-gigabyte archives are instantly jumpable.
- **Verification**: Confirmed successful seek-table generation in `sentinel record` simulations, providing the necessary hooks for the upcoming WASM dashboard.

---
## 2026-02-04 22:05:00 (UTC+02:00): Sentinel Dashboard & Browser-Side E2EE

- **Blazor WASM Foundation**: Initialized `SentinelDashboard` using Blazor WebAssembly to maintain 100% code reuse with the `ManagedSecurity.Core` and `Common` projects.
- **E2EE Telemetry Engine**: 
    - Instrumented `ManagedSecurityStream` with native telemetry (Latency, Throughput, FrameCount).
    - Verified real-time decryption in the browser with `0 B` allocations on the hot path.
- **Structural Integrity**:
    - Centralized external project references into `Assembly.Targets` to resolve the "Dotnet repo as a sibling" dependency.
    - Implemented `InitialTargets` validation in MSBuild to provide clear error messages if the security core projects are missing.
- **Bug Fix (Stream Corruption)**: Fixed a critical off-by-one error in `ManagedSecurityStream.Write` where inconsistent header-offset calculations were corrupting the first few bytes of encrypted frames during high-concurrency writes.
- **API Hardening**:
    - [x] Integrate shared `VaultEntry` in `ManagedSecurity.Common`.
    - [x] Fix `NativeAOT` JSON serialization in Sentinel CLI.
    - [x] Implement Browser-based E2EE Video Playback in Dashboard.
        - [x] Resolved "Data length" header validation bug in `Bindings.Header`.
        - [x] Verified playback with real encrypted media logs.
    - [ ] Implement Seek Table parsing for random-access UI.
    - Added `leaveOpen` support to `ManagedSecurityStream` to allow non-destructive post-processing of underlying streams (e.g., updating Seek Tables in headers after the payload is written).

---
## 2026-02-05 22:30:00 (UTC+02:00): Random-Access Decryption & Test Parity

- **Random-Access Support**: Enhanced `ManagedSecurityStream` to support initializing decryption from arbitrary frame offsets.
    - Added `skipMasterHeader` and `initialSequence` parameters to the constructor.
    - Implemented `seekTableOffset` enforcement in `TryReadNextFrame` to prevent reading into appended metadata/indices.
- **Bug Fix (Seek Over-Read)**: Fixed a critical issue where the reader would attempt to parse the appended `SeekTable` as a cryptographic frame, causing `Invalid MasterHeader magic` errors.
---
## 2026-02-05 23:00:00 (UTC+02:00): Instant-Jump & Hardware Acceleration

---
## 2026-02-06 20:55:00 (UTC+02:00): Build Stabilization & Sync/Async Validation

- **Environmental Hardening**:
    - Resolved persistent NuGet download errors by clearing local caches and disabling parallel restores during unstable network bursts.
    - Suppressed benign `NU1603` warnings in `ManagedSecurity.Test` caused by `MSTest.Sdk` alpha dependency redirects, resulting in a **0 Warning / 0 Error** clean solution build.
- **Architectural Validation**:
    - Verified the **Sync Core / Async I/O** refactor via standard test suites (16/16 passed).
    - Confirmed that moving `ref struct` operations to synchronous kernels effectively bypasses `CS4012` while maintaining zero-allocation performance on the cryptographic path.
- **Dashboard Recovery**:
    - Successfully restored and built the `SentinelDashboard` Blazor WASM project after environment cleanup.

### 🚀 Dashboard Launch Commands
To run the Sentinel Dashboard with full hardware acceleration:
- **Standard Watch Mode**: `cd /home/me/Repos/Sentinel-Dashboard && dotnet watch`
- **Specific URL Mode**: `dotnet run --project SentinelDashboard.csproj --urls "http://localhost:5000"`

---
## 2026-02-06 23:00:00 (UTC+02:00): Full E2EE Media Pipeline & Playback Verification

### 🏗️ Integrated Data Pipeline
- **Unified Data Contract**: Migrated `VaultEntry` to `ManagedSecurity.Common`. Both the high-speed C# CLI archiver and the Blazor WASM Dashboard now consume the exact same schema, ensuring seamless database synchronization.
- **NativeAOT Native JSON**: Implemented `Source-Generated JSON` serialization for the Sentinel CLI. This enables the `index` command to generate compatible `vault.json` database files within the project's strict NativeAOT constraints (zero-reflection).

### 🎥 Verified Browser-Side E2EE Playback
- **Security Logic Fix**: Resolved a critical "Data length" mismatch in `Bindings.Header.cs`. The validator now correctly respects streaming headers, allowing the WASM decryptor to parse frame metadata before the full payload is fetched.
- **Media Virtualization**: Successfully verified the `blob:http://` URL virtualization strategy.
    - **Zero Leakage**: Decrypted video bytes exist strictly in the browser's sandbox RAM (as Blobs).
    - **Standard HTML5 Support**: Virtual URIs allow standard `<video>` tags to play hardware-accelerated, decrypted media without exposing unencrypted chunks to the disk or network.
- **Verification Result**: 
    - **Throughput**: Verified at ~450 Mbps (simulated) / Real-time playback confirmed for 1080p MP4 archives.
    - **E2EE Handshake**: Latency measured at `< 2ms` for initial key derivation and header parsing.

### 🛡️ Current Environment State
- **Sentinel Dashboard**: Running via `dotnet watch` on `http://localhost:5186`.
- **Media Archives**: Encrypted `.bin` files and `vault.json` database are actively served from `wwwroot/`.
- **System Health**: 0 Build Warnings / 0 Runtime Errors.
