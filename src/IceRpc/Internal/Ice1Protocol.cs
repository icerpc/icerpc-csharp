// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The Ice1 protocol class.</summary>
    internal sealed class Ice1Protocol : Protocol
    {
        /// <summary>The Ice1 protocol singleton.</summary>
        internal static Ice1Protocol Instance { get; } = new();

        public override bool IsSupported => true;

        internal override IceEncoding? IceEncoding => Encoding.Ice11;

        internal override bool HasFieldSupport => false;

        internal override async ValueTask<IProtocolConnection> CreateConnectionAsync(
            INetworkConnection networkConnection,
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            var protocolConnection = new Ice1ProtocolConnection(
                await networkConnection.ConnectSingleStreamConnectionAsync(cancel).ConfigureAwait(false),
                incomingFrameMaxSize,
                networkConnection.IsDatagram ? networkConnection.DatagramMaxReceiveSize : null);
            await protocolConnection.InitializeAsync(isServer, cancel).ConfigureAwait(false);
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

        private Ice1Protocol()
            : base(ProtocolCode.Ice1, Ice1Name)
        {
        }
    }
}
