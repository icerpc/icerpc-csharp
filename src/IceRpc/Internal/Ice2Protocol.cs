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

        public override bool IsSupported => true;

        internal override IceEncoding? IceEncoding => Encoding.Ice20;

        internal override bool HasFieldSupport => true;

        internal override async ValueTask<(IProtocolConnection, NetworkConnectionInformation)> CreateConnectionAsync(
            INetworkConnection networkConnection,
            int incomingFrameMaxSize,
            bool isServer,
            CancellationToken cancel)
        {
            (INetworkStream? _,
                IMultiplexedNetworkStreamFactory? multiplexedNetworkStreamFactory,
                NetworkConnectionInformation information) =
                 await networkConnection.ConnectAsync(true, cancel).ConfigureAwait(false);
            if (multiplexedNetworkStreamFactory == null)
            {
                throw new InvalidOperationException(
                    @$"requested an {nameof(IMultiplexedNetworkStreamFactory)} from {
                        nameof(INetworkConnection.ConnectAsync)} but go a null {
                        nameof(IMultiplexedNetworkStreamFactory)}");
            }
            var protocolConnection = new Ice2ProtocolConnection(multiplexedNetworkStreamFactory, incomingFrameMaxSize);
            await protocolConnection.InitializeAsync(cancel).ConfigureAwait(false);
            return (protocolConnection, information);
        }

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
            if (payloadEncoding == Encoding.Ice11 && remoteException.IsIce1SystemException())
            {
                // We switch to the 2.0 encoding because the 1.1 encoding is lossy for system exceptions.
                payloadEncoding = Encoding.Ice20;
            }

            IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
            encoder.EncodeException(remoteException);

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

            return response;
        }

        private Ice2Protocol()
            : base(ProtocolCode.Ice2, Ice2Name)
        {
        }
    }
}
