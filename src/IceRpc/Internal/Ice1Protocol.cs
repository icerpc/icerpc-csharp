// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>The Ice1 protocol class.</summary>
    internal sealed class Ice1Protocol : Protocol
    {
        /// <summary>The Ice1 encoding singleton.</summary>
        internal static Ice1Protocol Instance { get; } = new();

        public override bool IsSupported => true;

        internal override async ValueTask<IProtocolConnection> CreateConnectionAsync(
            INetworkConnection networkConnection,
            int incomingFrameMaxSize,
            ILoggerFactory loggerFactory,
            CancellationToken cancel)
        {
            IProtocolConnection protocolConnection = new Ice1ProtocolConnection(
                await networkConnection.GetSingleStreamConnectionAsync(cancel).ConfigureAwait(false),
                incomingFrameMaxSize,
                networkConnection.IsServer,
                networkConnection.IsDatagram ? networkConnection.DatagramMaxReceiveSize : null,
                loggerFactory.CreateLogger("IceRpc.Protocol"));
            await protocolConnection.InitializeAsync(cancel).ConfigureAwait(false);
            return protocolConnection;
        }

        internal override OutgoingResponse CreateResponseFromException(Exception exception, IncomingRequest request)
        {
            if (exception is OperationCanceledException)
            {
                exception = new DispatchException("dispatch canceled by peer");
            }
            return base.CreateResponseFromException(exception, request);
        }

        internal override OutgoingResponse CreateResponseFromRemoteException(
            RemoteException exception,
            IceEncoding payloadEncoding)
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);

            var features = new FeatureCollection();
            if (payloadEncoding == Encoding.Ice11 && exception.IsIce1SystemException())
            {
                ReplyStatus replyStatus = encoder.EncodeIce1SystemException(exception);
                // Set the reply status feature. It's used when the response header is encoded.
                features.Set(replyStatus);
            }
            else
            {
                encoder.EncodeException(exception);
                features.Set(ReplyStatus.UserException);
            }

            return new OutgoingResponse(this, ResultType.Failure)
            {
                Features = features,
                Payload = bufferWriter.Finish(),
                PayloadEncoding = payloadEncoding
            };
        }

        internal override IceEncoding? GetIceEncoding() => Encoding.Ice11;

        internal override bool HasFieldSupport() => false;

        private Ice1Protocol()
            : base(ProtocolCode.Ice1, Ice1Name)
        {
        }
    }
}
