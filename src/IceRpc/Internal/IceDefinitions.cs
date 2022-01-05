// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Internal
{
    // Definitions for the ice protocol.
    internal static class IceDefinitions
    {
        // The encoding of the header for ice frames. It is nominally 1.0, but in practice it is identical to 1.1
        // for the subset of the encoding used by the ice headers.
        internal static readonly Encoding Encoding = Encoding.Slice11;

        // Size of an ice frame header:
        // Magic number (4 bytes)
        // Protocol bytes (4 bytes)
        // Frame type (Byte)
        // Compression status (Byte)
        // Frame size (Int - 4 bytes)
        internal const int HeaderSize = 14;

        // The magic number at the front of each frame.
        internal static readonly byte[] Magic = new byte[] { 0x49, 0x63, 0x65, 0x50 }; // 'I', 'c', 'e', 'P'

        // 4-bytes after magic that provide the protocol version (always 1.0 for an ice frame) and the
        // encoding of the frame header (always set to 1.0 with the an ice frame, even though we use 1.1).
        internal static readonly byte[] ProtocolBytes = new byte[] { 1, 0, 1, 0 };

        internal static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> CloseConnectionFrame =
            new ReadOnlyMemory<byte>[]
            {
                new byte[]
                {
                    Magic[0], Magic[1], Magic[2], Magic[3],
                    ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
                    (byte)IceFrameType.CloseConnection,
                    0, // Compression status.
                    HeaderSize, 0, 0, 0 // Frame size.
                }
            };

        internal static readonly byte[] FramePrologue = new byte[]
        {
            Magic[0], Magic[1], Magic[2], Magic[3],
            ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
        };

        internal static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> ValidateConnectionFrame =
            new ReadOnlyMemory<byte>[]
            {
                new byte[]
                {
                    Magic[0], Magic[1], Magic[2], Magic[3],
                    ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
                    (byte)IceFrameType.ValidateConnection,
                    0, // Compression status.
                    HeaderSize, 0, 0, 0 // Frame size.
                }
            };

        // Verify that the first 8 bytes correspond to Magic + ProtocolBytes
        internal static void CheckHeader(ReadOnlySpan<byte> header)
        {
            Debug.Assert(header.Length == 14);
            if (header[0] != Magic[0] || header[1] != Magic[1] || header[2] != Magic[2] || header[3] != Magic[3])
            {
                throw new InvalidDataException(
                    $"received incorrect magic bytes in header of ice frame: {BytesToString(header[0..4])}");
            }

            header = header[4..];

            if (header[0] != ProtocolBytes[0] || header[1] != ProtocolBytes[1])
            {
                throw new InvalidDataException(
                    $"received ice protocol frame with protocol set to {header[0]}.{header[1]}");
            }

            if (header[2] != ProtocolBytes[2] || header[3] != ProtocolBytes[3])
            {
                throw new InvalidDataException(
                    $"received ice protocol frame with protocol encoding set to {header[2]}.{header[3]}");
            }
        }

        private static string BytesToString(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray());
    }
}
