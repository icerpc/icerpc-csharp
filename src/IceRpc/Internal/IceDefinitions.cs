// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    // Definitions for the ice protocol.
    internal static class IceDefinitions
    {
        // The encoding of the header for ice frames. It is nominally 1.0, but in practice it is identical to 1.1
        // for the subset of the encoding used by the ice headers.
        internal static readonly Encoding Encoding = Encoding.Slice11;

        // Size of an ice frame prologue:
        // Magic number (4 bytes)
        // Protocol bytes (4 bytes)
        // Frame type (Byte)
        // Compression status (Byte)
        // Frame size (Int - 4 bytes)
        internal const int PrologueSize = 14;

        // The magic number at the front of each frame.
        internal static readonly byte[] Magic = new byte[] { 0x49, 0x63, 0x65, 0x50 }; // 'I', 'c', 'e', 'P'

        // 4-bytes after magic that provide the protocol version (always 1.0 for an ice frame) and the encoding of the
        // frame header (always set to 1.0 with the an ice frame, even though we use 1.1).
        internal static readonly byte[] ProtocolBytes = new byte[] { 1, 0, 1, 0 };

        internal static readonly IcePrologue CloseConnectionFrame = new(
            Magic[0], Magic[1], Magic[2], Magic[3],
            ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
            IceFrameType.CloseConnection,
            compressionStatus: 0,
            PrologueSize);

        internal static readonly byte[] FramePrologue = new byte[]
        {
            Magic[0], Magic[1], Magic[2], Magic[3],
            ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
        };

        internal static readonly IcePrologue ValidateConnectionFrame = new(
            Magic[0], Magic[1], Magic[2], Magic[3],
            ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
            IceFrameType.ValidateConnection,
            compressionStatus: 0,
            PrologueSize);

        // Verify that the first 8 bytes correspond to Magic + ProtocolBytes
        internal static void CheckPrologue(IcePrologue prologue)
        {
            if (prologue.Magic1 != Magic[0] ||
                prologue.Magic2 != Magic[1] ||
                prologue.Magic3 != Magic[2] ||
                prologue.Magic4 != Magic[3])
            {
                byte[] magic = new byte[] { prologue.Magic1, prologue.Magic2, prologue.Magic3, prologue.Magic4 };
                throw new InvalidDataException(
                    $"received incorrect magic bytes in prologue of ice frame: {BytesToString(magic)}");
            }

            if (prologue.ProtocolMajor != ProtocolBytes[0] || prologue.ProtocolMinor != ProtocolBytes[1])
            {
                throw new InvalidDataException(
                    $"received ice prologue with protocol set to {prologue.ProtocolMajor}.{prologue.ProtocolMinor}");
            }

            if (prologue.EncodingMajor != ProtocolBytes[2] || prologue.EncodingMinor != ProtocolBytes[3])
            {
                throw new InvalidDataException(
                    @$"received ice prologue with protocol encoding set to {
                        prologue.EncodingMajor}.{prologue.EncodingMinor}");
            }
        }

        private static string BytesToString(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray());
    }
}
