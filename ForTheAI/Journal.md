# ManagedSecurity Project Journal

This file tracks major design decisions, security hardening, and architectural shifts to provide context for both human developers and AI assistants.

---

## 2026-02-02: Integrity, Zero-Alloc & Hardening

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

## 2026-02-03: Performance Validation & Zero-Alloc API

- **High-Security Mode (S=01)**: Implemented a Synthetic Initialization Vector (SIV) construction using AES-256-GCM.
  - **Nonce-Misuse Resistance**: The GCM IV is derived from the plaintext via HMAC-SHA256, preventing security collapse if the 16-byte random nonce is reused.
  - **Multi-Layer Authentication**: Uses both the 16-byte GCM tag and a separate 16-byte HMAC-SHA256 segment (totalling 32 bytes) for extremely robust integrity verification.
  - **HKDF Support**: Integrated HKDF-SHA256 for subkey derivation (ENC and MAC keys) from the master key.

### 🛠️ Tooling & DevEx
- **Benchmarking**: Implemented comparative benchmarks against raw `System.Security.Cryptography.AesGcm`. The benchmarks are designed to prove that the `ManagedSecurity` header abstraction (including variable-length varints) has negligible overhead and can operate with `0 B` allocations.
- **Size Calculation**: Exposed `Cipher.GetRequiredSize` to help consumers allocate exact buffers for the zero-allocation API.
