# Blueprint: Distributed Computer Vision & Orchestration

This document outlines the architectural strategy for integrating high-performance Computer Vision (CV) into the Sentinel ecosystem using a distributed "Command & Control" (C2) model.

## 🏛️ 1. Split-Phase Inference Strategy

To optimize for performance and power consumption, the CV workload is split into two distinct tiers:

### A. The "Guardian" Phase (Broad Phase)
*   **Goal**: Detect "Interesting" events with minimal overhead.
*   **Source**: HTTP Snapshots (JPEG) retrieved from cameras.
*   **Frequency**: Low (0.5 Hz - 2 Hz).
*   **Target**: Saliency detection, lightweight classification (Person, Vehicle, Motion).
*   **Cost**: extremely low. No E2EE decryption required.

### B. The "Inquisitor" Phase (Narrow Phase)
*   **Goal**: Perform deep analysis (Recognition, Tracking, OCR).
*   **Source**: High-Res Decrypted `ManagedSecurityStream`.
*   **Frequency**: High (Native Framerate, 15-60 FPS).
*   **Target**: Face ID, License Plates, Behavioral Analysis.
*   **Trigger**: Woken up only when the Guardian detects a high-confidence event.
*   **Cost**: High (CPU/GPU intensive).

## 📡 2. Orchestration Topology (General & Scouts)

The system operates in a distributed hive model to ensure massive scalability.

### The Controller (The General)
*   **Responsibility**: Central Scheduler and Resource Manager.
*   **Inventory**: Maintains a live registry of discovered cameras and active Workers.
*   **Scheduling**: Distributes "Watching Tasks" to Workers based on their current CPU/Memory load.
*   **Handoff Logic**: When a Worker reports a "Broad Hit," the General promotes the task to a Narrow-capable worker.

### The Workers (The Scouts)
*   **Responsibility**: Execution of assigned tasks.
*   **Broad Workers**: Lean nodes (Raspberry Pi, low-power VMs) running the Guardian phase.
*   **Narrow Workers**: GPU-accelerated nodes running deep inference.

## 🧠 3. Zero-Copy Memory Management

To avoid the "Memory Tax" during Narrow Phase analysis:

1.  **Frame Hooks**: `ManagedSecurityStream` will implement an `OnFrameDecrypted` hook that provides a `ReadOnlySpan<byte>` pointing directly at the internal crypto buffer.
2.  **Pointer-Based Inference**: Inference engines (TensorFlow Lite, ONNX Runtime) will be configured to "Peek" at these existing memory addresses without duplicating bits.
3.  **HTTP Snapshots**: Broad phase bypasses the crypto pipeline entirely by fetching JPEGs from the camera's HTTP endpoint.

## 🚀 4. Implementation Milestone Phases

1.  **Orchestration Core**: Implement the `ManagedSecurity.Orchestration` library with the General/Worker messaging protocol.
2.  **Guardian Implementation**: Deploy the first Scout that polls HTTP snapshots and performs simple pixel-change saliency.
3.  **Narrow Handoff**: Integrate the `ManagedSecurityStream` hooks to allow a GPU worker to "Attach" to a live stream upon a Guardian trigger.
