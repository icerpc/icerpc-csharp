// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Protocols
{
    public interface IProtocolConnection
    {
        ValueTask InitializeAsync(CancellationToken cancel);

        ValueTask PingAsync(CancellationToken cancel);

        ValueTask<IncomingRequest> ReceiveRequestFrameAsync(CancellationToken cancel);

        ValueTask<IncomingResponse> ReceiveResponseFrameAsync(OutgoingRequest request, CancellationToken cancel);

        ValueTask SendRequestFrameAsync(OutgoingRequest request, CancellationToken cancel);

        ValueTask SendResponseFrameAsync(IncomingRequest request, OutgoingResponse response, CancellationToken cancel);

        ValueTask ShutdownAsync(Exception exception, CancellationToken cancel);

        ValueTask WaitForShutdownAsync(CancellationToken cancel);
    }
}
