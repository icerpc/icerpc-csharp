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
        internal override IceEncoding? IceEncoding => Encoding.Ice11;

        internal override bool HasFieldSupport => false;

        internal IProtocolConnectionFactory<ISimpleNetworkConnection> ProtocolConnectionFactory { get; } =
            new Ice1ProtocolConnectionFactory();

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

            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encoder.EncodeException(exception);
            _ = encoder.EndFixedLengthSize(start);

            return new OutgoingResponse(this, ResultType.Failure)
            {
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
