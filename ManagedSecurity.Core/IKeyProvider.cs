using System;

namespace ManagedSecurity.Core
{
    /// <summary>
    /// Abstraction for retrieving cryptographic keys by index.
    /// </summary>
    public interface IKeyProvider
    {
        /// <summary>
        /// Retrieves the key associated with the given index.
        /// </summary>
        /// <param name="keyIndex">The 12-bit key index from the message header.</param>
        /// <returns>The raw key bytes.</returns>
        ReadOnlyMemory<byte> GetKey(int keyIndex);
    }
}
