// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The Ice2 protocol class.</summary>
    internal sealed class Ice2Protocol : Protocol
    {
        /// <summary>The Ice2 protocol singleton.</summary>
        internal static Ice2Protocol Instance { get; } = new();

        internal override IceEncoding? IceEncoding => Encoding.Ice20;

        internal override bool HasFieldSupport => true;

        internal IProtocolConnectionFactory<IMultiplexedNetworkConnection> ProtocolConnectionFactory { get; } =
            new Ice2ProtocolConnectionFactory();

        internal override OutgoingResponse CreateResponseFromException(Exception exception, IncomingRequest request)
        {
            if (exception is OperationCanceledException)
            {
                throw exception; // Rethrow to abort the stream.
            }
            return base.CreateResponseFromException(exception, request);
        }

        internal override OutgoingResponse CreateResponseFromRemoteException(
            RemoteException remoteException,
            IceEncoding payloadEncoding)
        {
            var bufferWriter = new BufferWriter();

            IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);

            ReplyStatus replyStatus = ReplyStatus.UserException;
            if (encoder is Ice11Encoder encoder11 && remoteException.IsIce1SystemException())
            {
                replyStatus = encoder11.EncodeIce1SystemException(remoteException);
            }
            else
            {
                encoder.EncodeException(remoteException);
            }

            var response = new OutgoingResponse(this, ResultType.Failure)
            {
                Payload = bufferWriter.Finish(),
                PayloadEncoding = payloadEncoding
            };

            if (remoteException.RetryPolicy != RetryPolicy.NoRetry)
            {
                RetryPolicy retryPolicy = remoteException.RetryPolicy;
                response.Fields.Add((int)FieldKey.RetryPolicy, encoder => retryPolicy.Encode(encoder));
            }

            // We add the reply status field for ice1 system exceptions encoded with 1.1
            if (replyStatus > ReplyStatus.UserException)
            {
                response.Fields.Add((int)FieldKey.ReplyStatus, encoder => encoder.EncodeReplyStatus(replyStatus));
            }

            return response;
        }

        private Ice2Protocol()
            : base(ProtocolCode.Ice2, Ice2Name)
        {
        }
    }
}
