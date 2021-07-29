// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
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

        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload11 = ReadOnlyMemory<byte>.Empty;

        // The single byte corresponds to the compression format.
        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload20 = new byte[] { 0 };

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

        /// <summary>Returns the payload of an ice1 request frame for an operation with no argument.</summary>
        /// <param name="encoding">The encoding of this empty args payload.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetEmptyArgsPayload(Encoding encoding) =>
            GetVoidReturnValuePayload(encoding);

        /// <summary>Returns the payload of an ice1 response frame for an operation returning void.</summary>
        /// <param name="encoding">The encoding of this void return.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetVoidReturnValuePayload(Encoding encoding)
        {
            encoding.CheckSupportedIceEncoding();
            return encoding == Encoding.Ice11 ? _voidReturnValuePayload11 : _voidReturnValuePayload20;
        }

        /// <summary>Decodes an ice1 system exception.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="replyStatus">The reply status.</param>
        /// <returns>The exception read from the buffer.</returns>
        internal static RemoteException DecodeIce1SystemException(this IceDecoder decoder, ReplyStatus replyStatus)
        {
            Debug.Assert(decoder.Encoding == Encoding.Ice11);
            Debug.Assert(replyStatus > ReplyStatus.UserException);

            RemoteException systemException;

            switch (replyStatus)
            {
                case ReplyStatus.FacetNotExistException:
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:

                    var requestFailed = new Ice1RequestFailedExceptionData(decoder);

                    IList<string> facetPath = requestFailed.FacetPath;
                    if (facetPath.Count > 1)
                    {
                        throw new InvalidDataException($"read ice1 facet path with {facetPath.Count} elements");
                    }
                    string facet = facetPath.Count == 0 ? "" : requestFailed.FacetPath[0];

                    if (replyStatus == ReplyStatus.OperationNotExistException)
                    {
                        systemException = new OperationNotFoundException(
                            message: null,
                            new RemoteExceptionOrigin(requestFailed.Identity.ToPath(), requestFailed.Operation))
                        { Facet = facet };
                    }
                    else
                    {
                        systemException = new ServiceNotFoundException(
                            message: null,
                            new RemoteExceptionOrigin(requestFailed.Identity.ToPath(), requestFailed.Operation))
                        { Facet = facet };
                    }
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
        /// <param name="replyStatus">The reply status.</param>
        /// <param name="request">The request for which we write the exception.</param>
        /// <param name="message">The message carried by the exception.</param>
        /// <remarks>The reply status itself is part of the response header and is not written by this method.</remarks>
        internal static void EncodeIce1SystemException(
            this IceEncoder encoder,
            ReplyStatus replyStatus,
            IncomingRequest request,
            string message)
        {
            Debug.Assert(encoder.Encoding == Encoding.Ice11);

            switch (replyStatus)
            {
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:

                    Identity identity = request.Identity;
                    if (request.Protocol == Protocol.Ice2)
                    {
                        try
                        {
                            identity = Identity.FromPath(request.Path);
                        }
                        catch (FormatException)
                        {
                            // ignored, i.e. we'll marshal an empty identity
                        }
                    }
                    var requestFailed =
                        new Ice1RequestFailedExceptionData(identity, request.FacetPath, request.Operation);

                    requestFailed.Encode(encoder);
                    break;

                case ReplyStatus.UnknownLocalException:
                    encoder.EncodeString(message);
                    break;

                default:
                    Debug.Assert(false);
                    break;
            }
        }

        private static string BytesToString(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray());
    }
}
