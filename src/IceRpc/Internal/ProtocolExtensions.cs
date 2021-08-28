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

        /// <summary>Creates an outgoing response with the exception. If a 1.1 encoded exception is sent with the ice1
        /// protocol, this method also the <see cref="ReplyStatus"/> feature. This method also sets the
        /// <see cref="FieldKey.RetryPolicy"/> if an exception retry policy is set.</summary>
        internal static OutgoingResponse CreateResponseFromRemoteException(
            this Protocol protocol,
            RemoteException remoteException,
            Encoding payloadEncoding)
        {
            bool isIce1SystemException = payloadEncoding == Encoding.Ice11 &&
                (remoteException is ServiceNotFoundException ||
                 remoteException is OperationNotFoundException ||
                 remoteException is UnhandledException);

            if (protocol == Protocol.Ice2 && isIce1SystemException)
            {
                // An ice1 system exception is always encoded as a regular Ice 2.0 exception when sent over ice2.
                payloadEncoding = Encoding.Ice20;
                isIce1SystemException = false;
            }

            var bufferWriter = new BufferWriter();
            IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter, classFormat: FormatType.Sliced);

            FeatureCollection features = protocol == Protocol.Ice1 ? new FeatureCollection() : FeatureCollection.Empty;

            if (isIce1SystemException)
            {
                Debug.Assert(protocol == Protocol.Ice1);

                ReplyStatus replyStatus = encoder.EncodeIce1SystemException(remoteException);

                // Set the reply status feature. It's used when the response header is encoded.
                features.Set(replyStatus);
            }
            else
            {
                encoder.EncodeException(remoteException);

                if (protocol == Protocol.Ice1)
                {
                    features.Set(ReplyStatus.UserException);
                }
            }

            var response = new OutgoingResponse(protocol, ResultType.Failure)
            {
                Features = features,
                Payload = bufferWriter.Finish(),
                PayloadEncoding = payloadEncoding
            };

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

            return response;
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

        internal static OutgoingResponse ToOutgoingResponse(
            this Protocol targetProtocol,
            IncomingResponse incomingResponse)
        {
            if (incomingResponse.ResultType == ResultType.Success)
            {
                return new OutgoingResponse(targetProtocol, ResultType.Success)
                {
                    FieldsDefaults = incomingResponse.Fields.ToImmutableDictionary(),
                    Payload = new ReadOnlyMemory<byte>[] { incomingResponse.Payload },
                    PayloadEncoding = incomingResponse.PayloadEncoding
                };
            }
            else if (targetProtocol == Protocol.Ice1)
            {
                if (incomingResponse.Protocol == Protocol.Ice2 && incomingResponse.PayloadEncoding == Encoding.Ice20)
                {
                    // We may need to transcode an ice1 system exception
                    var decoder =
                        new Ice20Decoder(incomingResponse.Payload,
                                         activator: Ice20Decoder.GetActivator(typeof(RemoteException).Assembly));

                    string typeId = decoder.DecodeString();
                    if (typeId == typeof(ServiceNotFoundException).GetIceTypeId() ||
                        typeId == typeof(OperationNotFoundException).GetIceTypeId() ||
                        typeId == typeof(UnhandledException).GetIceTypeId())
                    {
                        decoder.Pos = 0;
                        RemoteException systemException = decoder.DecodeException();
                        return targetProtocol.CreateResponseFromRemoteException(systemException, Encoding.Ice11);
                    }
                }

                var features = new FeatureCollection();
                features.Set(incomingResponse.Protocol == Protocol.Ice1 ?
                    incomingResponse.Features.Get<ReplyStatus>() : ReplyStatus.UserException);

                return new OutgoingResponse(Protocol.Ice1, ResultType.Failure)
                {
                    Features = features,
                    Payload = new ReadOnlyMemory<byte>[] { incomingResponse.Payload },
                    PayloadEncoding = incomingResponse.PayloadEncoding,
                };
            }
            else
            {
                Debug.Assert(targetProtocol == Protocol.Ice2);

                if (incomingResponse.Protocol == Protocol.Ice1)
                {
                    // We may need to transcode an ice1 system exception
                    ReplyStatus replyStatus = incomingResponse.Features.Get<ReplyStatus>();

                    if (replyStatus > ReplyStatus.UserException)
                    {
                        RemoteException systemException = Ice1Definitions.DecodeIce1SystemException(
                            new Ice11Decoder(incomingResponse.Payload),
                            replyStatus);

                        return targetProtocol.CreateResponseFromRemoteException(systemException, Encoding.Ice20);
                    }
                }

                return new OutgoingResponse(targetProtocol, ResultType.Failure)
                {
                    // Don't forward RetryPolicy
                    FieldsDefaults = incomingResponse.Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                    Payload = new ReadOnlyMemory<byte>[] { incomingResponse.Payload },
                    PayloadEncoding = incomingResponse.PayloadEncoding,
                };
            }
        }
    }
}
