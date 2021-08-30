// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Diagnostics;

namespace IceRpc.Internal
{
    // Definitions for the ice1 protocol.
    internal static class Ice1Definitions
    {
        // The encoding of the header for ice1 frames. It is nominally 1.0, but in practice it is identical to 1.1
        // for the subset of the encoding used by the ice1 headers.
        internal static readonly Encoding Encoding = Encoding.Ice11;

        // Size of an ice1 frame header:
        // Magic number (4 bytes)
        // Protocol bytes (4 bytes)
        // Frame type (Byte)
        // Compression status (Byte)
        // Frame size (Int - 4 bytes)
        internal const int HeaderSize = 14;

        // The magic number at the front of each frame.
        internal static readonly byte[] Magic = new byte[] { 0x49, 0x63, 0x65, 0x50 }; // 'I', 'c', 'e', 'P'

        // 4-bytes after magic that provide the protocol version (always 1.0 for an ice1 frame) and the
        // encoding of the frame header (always set to 1.0 with the an ice1 frame, even though we use 1.1).
        internal static readonly byte[] ProtocolBytes = new byte[] { 1, 0, 1, 0 };

        internal static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> CloseConnectionFrame =
            new ReadOnlyMemory<byte>[]
            {
                new byte[]
                {
                    Magic[0], Magic[1], Magic[2], Magic[3],
                    ProtocolBytes[0], ProtocolBytes[1], ProtocolBytes[2], ProtocolBytes[3],
                    (byte)Ice1FrameType.CloseConnection,
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
                    (byte)Ice1FrameType.ValidateConnection,
                    0, // Compression status.
                    HeaderSize, 0, 0, 0 // Frame size.
                }
            };

        private static readonly HashSet<string> _systemExceptionTypeIds = new HashSet<string>
        {
            typeof(ServiceNotFoundException).GetIceTypeId()!,
            typeof(OperationNotFoundException).GetIceTypeId()!,
            typeof(UnhandledException).GetIceTypeId()!
        };

        // Verify that the first 8 bytes correspond to Magic + ProtocolBytes
        internal static void CheckHeader(ReadOnlySpan<byte> header)
        {
            Debug.Assert(header.Length == 14);
            if (header[0] != Magic[0] || header[1] != Magic[1] || header[2] != Magic[2] || header[3] != Magic[3])
            {
                throw new InvalidDataException(
                    $"received incorrect magic bytes in header of ice1 frame: {BytesToString(header.Slice(0, 4))}");
            }

            header = header[4..];

            if (header[0] != ProtocolBytes[0] || header[1] != ProtocolBytes[1])
            {
                throw new InvalidDataException(
                    $"received ice1 protocol frame with protocol set to {header[0]}.{header[1]}");
            }

            if (header[2] != ProtocolBytes[2] || header[3] != ProtocolBytes[3])
            {
                throw new InvalidDataException(
                    $"received ice1 protocol frame with protocol encoding set to {header[2]}.{header[3]}");
            }
        }

        /// <summary>Decodes an ice1 system exception.</summary>
        /// <param name="decoder">The decoder.</param>
        /// <param name="replyStatus">The reply status.</param>
        /// <returns>The exception decoded using the decoder.</returns>
        internal static RemoteException DecodeIce1SystemException(this IceDecoder decoder, ReplyStatus replyStatus)
        {
            Debug.Assert(decoder is Ice11Decoder);
            Debug.Assert(replyStatus > ReplyStatus.UserException);

            RemoteException systemException;

            switch (replyStatus)
            {
                case ReplyStatus.FacetNotExistException:
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:

                    var requestFailed = new Ice1RequestFailedExceptionData(decoder);

                    if (requestFailed.IdentityAndFacet.OptionalFacet.Count > 1)
                    {
                        throw new InvalidDataException("received ice1 optionalFacet with too many elements");
                    }

                    systemException = replyStatus == ReplyStatus.OperationNotExistException ?
                        new OperationNotFoundException() : new ServiceNotFoundException();

                    systemException.Origin = new RemoteExceptionOrigin(requestFailed.IdentityAndFacet.ToPath(),
                                                                       requestFailed.Operation);
                    break;

                default:
                    systemException = new UnhandledException(decoder.DecodeString());
                    break;
            }

            systemException.ConvertToUnhandled = true;
            return systemException;
        }

        /// <summary>Encodes an ice1 system exception.</summary>
        /// <param name="encoder">This Ice encoder.</param>
        /// <param name="exception">The exception.</param>
        internal static ReplyStatus EncodeIce1SystemException(this IceEncoder encoder, Exception exception)
        {
            ReplyStatus replyStatus = exception switch
            {
                ServiceNotFoundException => ReplyStatus.ObjectNotExistException,
                OperationNotFoundException => ReplyStatus.OperationNotExistException,
                _ => ReplyStatus.UnknownLocalException,
            };

            switch (replyStatus)
            {
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:
                    var remoteException = (RemoteException)exception;
                    IdentityAndFacet identityAndFacet;
                    try
                    {
                        identityAndFacet = IdentityAndFacet.FromPath(remoteException.Origin.Path);
                    }
                    catch
                    {
                        // ignored, i.e. we'll encode an empty identity + facet
                        identityAndFacet = new IdentityAndFacet(Identity.Empty, "");
                    }
                    var requestFailed = new Ice1RequestFailedExceptionData(
                        identityAndFacet,
                        remoteException.Origin.Operation);
                    requestFailed.Encode(encoder);
                    break;

                default:
                    encoder.EncodeString(exception.Message);
                    break;
            }
            return replyStatus;
        }

        internal static bool IsIce1SystemException(this RemoteException remoteException) =>
            remoteException is ServiceNotFoundException ||
            remoteException is OperationNotFoundException ||
            remoteException is UnhandledException;

        internal static bool IsIce1SystemExceptionTypeId(this string typeId) =>
            _systemExceptionTypeIds.Contains(typeId);

        private static string BytesToString(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray());
    }
}
