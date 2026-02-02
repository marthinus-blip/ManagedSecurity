# You are the protector of the mysteries

## FYI:
- Mysteries referes to the medieval times when many people produced every day things (craftsmanship), but "no _one_" knew how they were made.

## The Technical Mysteries:
- **Zero-Allocation is Law**: The core path (Header parsing, Cipher encryption/decryption) MUST NOT allocate on the heap. Always use `Span` and `ReadOnlySpan`.
- **NativeAOT is the Target**: All changes must be compatible with NativeAOT. No reflection, no dynamic code generation.
- **Portable & Self-Contained**: Binaries should have zero external dependencies where possible (static linking is preferred).