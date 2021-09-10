// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
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
        private static readonly IActivator<Ice20Decoder> _activator20 =
            Ice20Decoder.GetActivator(typeof(RemoteException).Assembly);

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

        /// <summary>Creates an outgoing response with the exception. With the ice1 protocol, this method sets the
        /// <see cref="ReplyStatus"/> feature. This method also sets the <see cref="FieldKey.RetryPolicy"/> if an
        /// exception retry policy is set.</summary>
        internal static OutgoingResponse CreateResponseFromException(
            this Protocol protocol,
            Exception exception,
            IncomingRequest request)
        {
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

            RemoteException? remoteException = exception as RemoteException;
            if (remoteException == null || remoteException.ConvertToUnhandled)
            {
                remoteException = new UnhandledException(exception);
            }

            if (remoteException.Origin == RemoteExceptionOrigin.Unknown)
            {
                remoteException.Origin = new RemoteExceptionOrigin(request.Path, request.Operation);
            }

            return protocol.CreateResponseFromRemoteException(remoteException, request.GetIceEncoding());
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
            if (incomingResponse.ResultType == ResultType.Failure && incomingResponse.Protocol != targetProtocol)
            {
                if (incomingResponse.Protocol == Protocol.Ice2 && incomingResponse.PayloadEncoding == Encoding.Ice20)
                {
                    Debug.Assert(targetProtocol == Protocol.Ice1);

                    // ice1 system exceptions must be encoded with 1.1 when forwarded from ice2 to ice1, so we check if
                    // we have a system exception.

                    var decoder = new Ice20Decoder(incomingResponse.Payload);

                    string typeId = decoder.DecodeString();
                    if (typeId.IsIce1SystemExceptionTypeId())
                    {
                        var systemException = (RemoteException)_activator20.CreateInstance(typeId, decoder)!;
                        decoder.CheckEndOfBuffer(skipTaggedParams: false);
                        return targetProtocol.CreateResponseFromRemoteException(systemException, Encoding.Ice11);
                    }
                }
                else if (incomingResponse.Protocol == Protocol.Ice1 &&
                         incomingResponse.PayloadEncoding == Encoding.Ice11)
                {
                    Debug.Assert(targetProtocol == Protocol.Ice2);

                    // ice1 system exceptions must be encoded with 2.0 when forwarded from ice1 to ice2, so we check if
                    // we have a system exception.

                    ReplyStatus replyStatus = incomingResponse.Features.Get<ReplyStatus>();

                    if (replyStatus > ReplyStatus.UserException)
                    {
                        RemoteException systemException =
                            new Ice11Decoder(incomingResponse.Payload).DecodeIce1SystemException(replyStatus);

                        return targetProtocol.CreateResponseFromRemoteException(systemException, Encoding.Ice20);
                    }
                }
            }

            // Normal situation:

            FeatureCollection features = FeatureCollection.Empty;
            if (incomingResponse.ResultType == ResultType.Failure && targetProtocol == Protocol.Ice1)
            {
                features = new FeatureCollection();
                features.Set(incomingResponse.Protocol == Protocol.Ice1 ?
                    incomingResponse.Features.Get<ReplyStatus>() : ReplyStatus.UserException);
            }

            return new OutgoingResponse(targetProtocol, incomingResponse.ResultType)
            {
                Features = features,
                // Don't forward RetryPolicy
                FieldsDefaults = incomingResponse.Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                Payload = new ReadOnlyMemory<byte>[] { incomingResponse.Payload },
                PayloadEncoding = incomingResponse.PayloadEncoding,
            };
        }

        private static OutgoingResponse CreateResponseFromRemoteException(
            this Protocol protocol,
            RemoteException remoteException,
            IceEncoding payloadEncoding)
        {
            FeatureCollection features = FeatureCollection.Empty;
            var bufferWriter = new BufferWriter();

            if (protocol == Protocol.Ice1)
            {
                features = new FeatureCollection();

                IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);

                if (payloadEncoding == Encoding.Ice11 && remoteException.IsIce1SystemException())
                {
                    ReplyStatus replyStatus = encoder.EncodeIce1SystemException(remoteException);
                    // Set the reply status feature. It's used when the response header is encoded.
                    features.Set(replyStatus);
                }
                else
                {
                    encoder.EncodeException(remoteException);
                    features.Set(ReplyStatus.UserException);
                }
            }
            else
            {
                Debug.Assert(protocol == Protocol.Ice2);
                if (payloadEncoding == Encoding.Ice11 && remoteException.IsIce1SystemException())
                {
                    // We switch to the 2.0 encoding because the 1.1 encoding is lossy for system exceptions.
                    payloadEncoding = Encoding.Ice20;
                }

                IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
                encoder.EncodeException(remoteException);
            }

            var response = new OutgoingResponse(protocol, ResultType.Failure)
            {
                Features = features,
                Payload = bufferWriter.Finish(),
                PayloadEncoding = payloadEncoding
            };

            if (protocol == Protocol.Ice2 && remoteException.RetryPolicy.Retryable != Retryable.No)
            {
                var retryPolicy = remoteException.RetryPolicy;

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
    }
}
