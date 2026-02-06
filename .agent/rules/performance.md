# Performance and Memory Safety Rules

## 1. Zero-Allocation Kernels
- All cryptographic transformations and header manipulations MUST happen in synchronous, non-async methods.
- This allows the use of `Span<byte>` and `ReadOnlySpan<byte>` for all operations.
- **Rule**: If a method needs to use a Span, it MUST NOT be marked `async`.

## 2. Async I/O Boundaries
- Public Stream APIs (Read/Write) MUST remain asynchronous to support high-latency environments (e.g., Remote Sentinel Camera feeds).
- Use `ValueTask<T>` for methods that may complete synchronously (like internal buffer reads).
- **Pattern**: 
    1. `await` data into a `byte[]` buffer.
    2. Call a synchronous `void Process(Span<byte> buffer)` method.
    3. `await` the results back to the stream.

## 3. High-Latency Scalability
- Ensure `CancellationToken` is propagated through all async I/O calls.
- Never block (no `.Result` or `.Wait()`) on the async path to avoid thread-pool starvation in the Sentinel Hub.
