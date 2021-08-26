// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>Parser that creates protocol instances.</summary>
    internal static class ProtocolParser
    {
        /// <summary>Parses a protocol string in the stringified proxy format into a Protocol.</summary>
        /// <param name="str">The string to parse.</param>
        /// <returns>The parsed protocol, or throws an exception if the string cannot be parsed.</returns>
        internal static Protocol Parse(string str)
        {
            switch (str)
            {
                case "ice1":
                    return Protocol.Ice1;
                case "ice2":
                    return Protocol.Ice2;
                default:
                    if (str.EndsWith(".0", StringComparison.Ordinal))
                    {
                        str = str[0..^2];
                    }
                    if (byte.TryParse(str, out byte value))
                    {
                        return value > 0 ? (Protocol)value : throw new FormatException("invalid protocol 0");
                    }
                    else
                    {
                        throw new FormatException($"invalid protocol '{str}'");
                    }
            }
        }
    }

    /// <summary>Provides extensions methods for <see cref="Protocol"/>.</summary>
    internal static class ProtocolExtensions
    {
        /// <summary>Checks if this protocol is supported by the IceRPC runtime. If not supported, throws
        /// <see cref="NotSupportedException"/>.</summary>
        /// <param name="protocol">The protocol.</param>
        internal static void CheckSupported(this Protocol protocol)
        {
            if (protocol != Protocol.Ice1 && protocol != Protocol.Ice2)
            {
                throw new NotSupportedException(
                    @$"Ice protocol '{protocol.GetName()
                    }' is not supported by this IceRPC runtime ({typeof(Protocol).Assembly.GetName().Version})");
            }
        }

        /// <summary>Decodes an exception from the given response. The decoding of an exception is protocol and
        /// encoding specific. If the encoded exception contains a 1.1 payload, the exception needs is encoded
        /// either as a user or system exception. If the 1.1 encoded exception is received with the Ice1 protocol,
        /// this method gets the <see cref="ReplyStatus"/> feature to figure out if it should decode a user or
        /// system exception. If it's the received with the Ice2 protocol, the reply status is obtained from
        /// the <see cref="FieldKey.ReplyStatus"/>.</summary>
        internal static Exception DecodeResponseException(
            this Protocol protocol,
            IncomingResponse response,
            IceDecoder decoder)
        {
            RemoteException exception;
            if (protocol == Protocol.Ice1)
            {
                ReplyStatus replyStatus = response.Features.Get<ReplyStatus>();
                if (replyStatus == ReplyStatus.UserException)
                {
                    exception = decoder.DecodeException();
                }
                else
                {
                    exception = ((Ice11Decoder)decoder).DecodeIce1SystemException(replyStatus);
                }
            }
            else
            {
                if (response.Fields.GetValue((int)FieldKey.ReplyStatus,
                                             decoder => decoder.DecodeReplyStatus()) is ReplyStatus replyStatus)
                {
                    if (response.PayloadEncoding != Encoding.Ice11)
                    {
                        throw new InvalidDataException($"unexpected {nameof(FieldKey.ReplyStatus)} field");
                    }
                    exception = ((Ice11Decoder)decoder).DecodeIce1SystemException(replyStatus);
                }
                else
                {
                    exception = decoder.DecodeException();
                }
            }
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return exception;
        }

        /// <summary>Encodes an exception into the given response. The encoding of an exception is protocol and
        /// encoding specific. If the exception is encoded is a 1.1 payload, the exception needs to be encoded
        /// either as a user or system exception. If the 1.1 encoded exception is sent with the Ice1 protocol,
        /// this method also sets the <see cref="ReplyStatus"/> feature to allow figure it out when encoding
        /// the Ice1 response frame. If it's the sent with the Ice2 protocol, the reply status is sent with
        /// the <see cref="FieldKey.ReplyStatus"/>. This method also sets the <see
        /// cref="FieldKey.RetryPolicy"/> if an exception retry policy is set.</summary>
        internal static void EncodeResponseException(
            this Protocol protocol,
            IncomingRequest request,
            OutgoingResponse response,
            Exception exception)
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = request.PayloadEncoding.CreateIceEncoder(bufferWriter, classFormat: FormatType.Sliced);

            if (protocol == Protocol.Ice1)
            {
                if (exception is OperationCanceledException)
                {
                    exception = new DispatchException("dispatch canceled by peer");
                }
            }
            else
            {
                if (exception is OperationCanceledException)
                {
                    throw exception; // Rethrow to abort the stream
                }
            }

            var remoteException = exception as RemoteException;
            if (request.PayloadEncoding == Encoding.Ice11 &&
                (remoteException == null ||
                 remoteException is ServiceNotFoundException ||
                 remoteException is OperationNotFoundException ||
                 remoteException.ConvertToUnhandled))
            {
                ReplyStatus replyStatus = encoder.EncodeIce1SystemException(exception);
                if (protocol == Protocol.Ice1)
                {
                    if (response.Features.IsReadOnly)
                    {
                        response.Features = new();
                    }
                    // Set the reply status feature. This is used when the response is encoded.
                    response.Features.Set(replyStatus);
                }
                else
                {
                    response.Fields.Add(
                        (int)FieldKey.ReplyStatus,
                        fieldEncoder => fieldEncoder.EncodeByte((byte)replyStatus));
                }
            }
            else
            {
                if (protocol == Protocol.Ice1)
                {
                    if (response.Features.IsReadOnly)
                    {
                        response.Features = new();
                    }
                    // Set the reply status feature. This is used when the response is encoded.
                    response.Features.Set(ReplyStatus.UserException);
                }

                if (remoteException == null || remoteException.ConvertToUnhandled)
                {
                    remoteException = new UnhandledException(exception);
                }
                encoder.EncodeException(remoteException);
            }

            if (protocol == Protocol.Ice2 &&
                remoteException?.RetryPolicy is RetryPolicy retryPolicy &&
                retryPolicy.Retryable != Retryable.No)
            {
                response.Fields.Add(
                    (int)FieldKey.RetryPolicy,
                    fieldEncoder =>
                    {
                        fieldEncoder.EncodeRetryable(retryPolicy.Retryable);
                        if (retryPolicy.Retryable == Retryable.AfterDelay)
                        {
                            fieldEncoder.EncodeVarUInt((uint)retryPolicy.Delay.TotalMilliseconds);
                        }
                    });
            }

            response.Payload = bufferWriter.Finish();
        }

        internal static OutgoingRequest ToOutgoingRequest(
            this Protocol targetProtocol,
            IncomingRequest request,
            Connection? targetConnection = null,
            Proxy? targetProxy = null)
        {
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields;
            if (request.Protocol == Protocol.Ice2 && targetProtocol == Protocol.Ice2)
            {
                fields = request.Fields;
            }
            else
            {
                fields = ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }

            // TODO: forward stream parameters

            return new OutgoingRequest(
                targetProtocol,
                path: targetProxy?.Path ?? request.Path,
                operation: request.Operation)
            {
                AltEndpoints = targetProxy?.AltEndpoints ?? ImmutableList<Endpoint>.Empty,
                Connection = targetConnection ?? targetProxy?.Connection,
                Deadline = request.Deadline,
                Endpoint = targetProxy?.Endpoint,
                Features = request.Features,
                FieldsDefaults = fields,
                IsOneway = request.IsOneway,
                IsIdempotent = request.IsIdempotent,
                Proxy = targetProxy,
                PayloadEncoding = request.PayloadEncoding,
                Payload = new ReadOnlyMemory<byte>[] { request.Payload }
            };
        }

        internal static OutgoingResponse ToOutgoingResponse(this Protocol targetProtocol, IncomingResponse response)
        {
            if (targetProtocol == Protocol.Ice1)
            {
                var outgoingResponse = new OutgoingResponse(targetProtocol, response.ResultType)
                {
                    Features = new(),
                    Payload = new ReadOnlyMemory<byte>[] { response.Payload },
                    PayloadEncoding = response.PayloadEncoding,
                };
                if (response.Protocol == Protocol.Ice1)
                {
                    outgoingResponse.Features.Set(response.Features.Get<ReplyStatus>());
                }
                else
                {
                    if (response.Fields.GetValue(
                        (int)FieldKey.ReplyStatus, decoder => decoder.DecodeReplyStatus()) is ReplyStatus replyStatus)
                    {
                        outgoingResponse.Features.Set(replyStatus);
                    }
                    else
                    {
                        outgoingResponse.Features.Set(response.ResultType == ResultType.Success ?
                            ReplyStatus.OK : ReplyStatus.UserException);
                    }
                }
                return outgoingResponse;
            }
            else
            {
                var outgoingResponse = new OutgoingResponse(targetProtocol, response.ResultType)
                {
                    // Don't forward RetryPolicy
                    FieldsDefaults = response.Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                    Payload = new ReadOnlyMemory<byte>[] { response.Payload },
                    PayloadEncoding = response.PayloadEncoding,
                };
                if (response.Protocol == Protocol.Ice1 && response.PayloadEncoding == Encoding.Ice11)
                {
                    outgoingResponse.Fields.Add(
                        (int)FieldKey.ReplyStatus,
                        encoder => encoder.EncodeByte((byte)response.Features.Get<ReplyStatus>()));
                }
                return outgoingResponse;
            }
        }
    }
}
