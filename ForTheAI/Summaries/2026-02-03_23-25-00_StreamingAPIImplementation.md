# Summary: Streaming API & S=2 (Sequenced Profile) Implementation
**Timestamp:** 2026-02-03 23:25:00 (UTC+02:00)

## 🚀 Objective
Build a secure, efficient, and anti-replay protected streaming API for the ManagedSecurity library, capable of handling large data sets over TCP or files while maintaining zero-allocation principles.

## 🏗️ Technical Implementation

### 1. Protocol Evolution (`S=02`)
The "Perfect 32" header format was extended to support a new **Sequenced Profile**.
- **Layout**: `[Header (4B)] + [Extensions (0-N)] + [Seq# (8B)] + [IV (12B)] + [MAC (16B)] + [Payload (L)]`
- **Sequence Number**: A mandatory 64-bit Big-Endian integer immediately following the header extensions.
- **AAD Binding**: The sequence number is cryptographically bound into the Associated Authenticated Data (AAD) of the AES-GCM engine. This ensures that:
    - Frames cannot be reordered (Replay/Swap protection).
    - Frames cannot be injected from other streams.
    - Decryption fails if the sequence is tampered with.

### 2. `ManagedSecurityStream`
A `System.IO.Stream` implementation that automates the framing and security logic.
- **Framed Architecture**: Data is divided into discrete frames (default 64KB).
- **Master Header**: The stream starts with a unique `MSG` magic header (12 bytes) that includes versioning and chunk size metadata.
- **Anti-Swap Enforcement**: The `Read` path strictly validates that the 64-bit sequence number increments by exactly 1 for every frame.
- **Memory Efficiency**: Uses a single pre-allocated internal buffer to perform all I/O and cryptographic operations, satisfying the "Zero-Alloc" mandate.

### 3. `Cipher` Enhancements
- **Multi-Profile Support**: The `Cipher` class now supports `S=00` (Standard), `S=01` (SIV/High-Security), and `S=02` (Sequenced).
- **Span-Based Decryption**: Refined decryption logic to handle exact payload slicing, meeting the strict requirements of `System.Security.Cryptography.AesGcm`.

## 🧪 Verification Results
- **14/14 Tests Passed**: Including a complex round-trip stress test that validates multi-block encryption/decryption with strict sequencing.
- **Tampered Frame Detection**: Verified that swapping two encrypted frames causes an immediate `CryptographicException` due to AAD mismatch.

## 📚 Updated Documentation
- **Readme.md**: Updated to include the new binary layout and streaming mode.
- **Journal.md**: Logged the architectural shift to Sequenced Profiles.
