// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame
    {
        private static readonly IActivator<Ice20Decoder> _activator20 =
            Ice20Decoder.GetActivator(typeof(RemoteException).Assembly);

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

        /// <summary>Constructs an incoming response.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to receive the response.</param>
        /// <param name="resultType">The <see cref="ResultType"/> of the response.</param>
        public IncomingResponse(Protocol protocol, ResultType resultType) :
            base(protocol) => ResultType = resultType;

        /// <summary>Create an outgoing response from this incoming response. The response is constructed to be
        /// forwarded using the given target protocol.</summary>
        /// <param name="targetProtocol">The protocol used to send to the outgoing response.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The outgoing response to be forwarded.</returns>
        public async ValueTask<OutgoingResponse> ToOutgoingResponseAsync(
            Protocol targetProtocol,
            CancellationToken cancel)
        {
            // TODO: temporary code, which assumes we use the Slice encoding

            IceEncoding payloadEncoding = (IceEncoding)PayloadEncoding;
            int payloadSize = await payloadEncoding.ReadPayloadSizeAsync(PayloadStream, cancel).ConfigureAwait(false);
            Memory<byte> payloadBuffer = default;

            if (payloadSize > 0)
            {
                payloadBuffer = new byte[payloadSize];
                await PayloadStream.ReadUntilFullAsync(payloadBuffer, cancel).ConfigureAwait(false);
            }

            if (ResultType == ResultType.Failure && Protocol != targetProtocol)
            {
                if (Protocol == Protocol.Ice2 && PayloadEncoding == Encoding.Ice20)
                {
                    Debug.Assert(targetProtocol == Protocol.Ice1);

                    // ice1 system exceptions must be encoded with 1.1 when forwarded from ice2 to ice1, so we check if
                    // we have a system exception.

                    var decoder = new Ice20Decoder(payloadBuffer);

                    string typeId = decoder.DecodeString();
                    if (typeId.IsIce1SystemExceptionTypeId())
                    {
                        // We use the activator directly because we can't rewind to call decoder.DecodeException
                        var systemException = (RemoteException)_activator20.CreateInstance(typeId, decoder)!;

                        // skipTaggedParams is true to skip the remaining exception tagged members (most likely none);
                        // see Ice20Decoder.DecodeException.
                        decoder.CheckEndOfBuffer(skipTaggedParams: true);
                        return targetProtocol.CreateResponseFromRemoteException(systemException, Encoding.Ice11);
                    }
                }
                else if (Protocol == Protocol.Ice1 && PayloadEncoding == Encoding.Ice11)
                {
                    Debug.Assert(targetProtocol == Protocol.Ice2);

                    // ice1 system exceptions must be encoded with 2.0 when forwarded from ice1 to ice2, so we check if
                    // we have a system exception.

                    ReplyStatus replyStatus = Features.Get<ReplyStatus>();

                    if (replyStatus > ReplyStatus.UserException)
                    {
                        Debug.Assert(payloadSize > 0);

                        RemoteException systemException = new Ice11Decoder(payloadBuffer).
                            DecodeIce1SystemException(replyStatus);

                        return targetProtocol.CreateResponseFromRemoteException(systemException, Encoding.Ice20);
                    }
                }
            }

            // Normal situation:

            FeatureCollection features = FeatureCollection.Empty;
            if (ResultType == ResultType.Failure && targetProtocol == Protocol.Ice1)
            {
                features = new FeatureCollection();
                features.Set(Protocol == Protocol.Ice1 ? Features.Get<ReplyStatus>() : ReplyStatus.UserException);
            }

            return new OutgoingResponse(targetProtocol, ResultType)
            {
                Features = features,
                // Don't forward RetryPolicy
                FieldsDefaults = Fields.ToImmutableDictionary().Remove((int)FieldKey.RetryPolicy),
                Payload = new ReadOnlyMemory<byte>[] { payloadBuffer },
                PayloadEncoding = PayloadEncoding,
            };
        }
    }
}
