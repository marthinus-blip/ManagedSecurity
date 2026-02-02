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
