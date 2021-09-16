// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal static class SlicDefinitions
    {
        internal enum FrameType : byte
        {
            Initialize = 1,
            InitializeAck,
            Version,
            Stream,
            StreamLast,
            StreamReset,
            StreamConsumed,
            StreamStopSending,
            Close
        }

        // The header below is a sentinel header used to reserve space in the protocol frame to avoid
        // allocating again a byte buffer for the Slic header.
        internal static ReadOnlyMemory<byte> FrameHeader = new byte[]
        {
            0x05, // Frame type
            0x02, 0x04, 0x06, 0x08, // FrameSize (varuint)
            0x03, 0x05, 0x07, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, // Stream ID (varulong)
        };

        internal static readonly Encoding Encoding = Encoding.Ice20;
    }
}
