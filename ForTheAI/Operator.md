# Operator Profile & Project Vision
**Last Updated:** 2026-02-05 23:25:00 (UTC+02:00)


## 👤 Operator Identity
The Operator is a specialized software engineer (haha, you flatter me, but I don't think that is true) building high-performance security infrastructure. 
**Core Value:** Security through robust, authenticated protocols with zero runtime overhead.

## 🎯 Global Project Vision: "Sentinel Security"
The long-term goal is a **Personal Security Monitoring System** for camera feeds. 
This project, `ManagedSecurity`, is the cryptographic backbone of that system.

## 🛠️ Technical Preferences
- **Language/Stack**: .NET 8, C# 12, NativeAOT (Strict compliance).
- **Design Philosophy**: 
    - **Zero-Allocation**: Use `Span<T>` and `Memory<T>` everywhere.
    - **Custom Binary Protocols**: Prefer bit-packed headers ("Perfect 32") over JSON/Protobuf for speed.
    - **Cross-Platform**: Force Big-Endian for network/storage consistency.
    - **Media-Awareness**: Align encryption boundaries with media boundaries (H.264 I-Frames/NAL units). Implement **Seek Tables** in unencrypted metadata to enable high-speed recovery and jumping.
- **Dashboard Architecture**: **WASM-First/E2EE**. The Hub acts as a blind relay; decryption happens in the browser via WebAssembly to ensure absolute privacy for camera feeds.

## 🔐 Cryptographic Pillars
1. **S=00 (Standard)**: AES-256-GCM.
2. **S=01 (SIV)**: Nonce-misuse resistance for critical metadata.
3. **S=02 (Sequenced)**: Streaming profile with mandatory 8-byte sequence numbers for anti-swap protection.
4. **Discovery Headers**: Store authenticated but unencrypted metadata (CameraID, Timestamps) in "Master Headers" to allow indexing without keys.

## 📝 Workflow Context
- Use the `ForTheAI/` directory to store architectural summaries and project journals.
- The `Sentinel` ingestor is the primary consumer app; it implements "Rolling Vaults" for 24/7 video recording.
- **NEVER** assume a fresh session knows about the I-Frame or "Sentinel" goals without checking this file.
