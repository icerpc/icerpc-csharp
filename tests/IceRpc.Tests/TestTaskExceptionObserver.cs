// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;

namespace IceRpc.Tests;

internal class TestTaskExceptionObserver : ITaskExceptionObserver
{
    internal Task<Exception> DispatchFailedException => _dispatchFailedTcs.Task;

    internal Task<Exception> DispatchRefusedException => _dispatchRefusedTcs.Task;

    internal Task<Exception> RequestPayloadContinuationFailedException => _requestFailedTcs.Task;

    private readonly TaskCompletionSource<Exception> _dispatchFailedTcs = new();
    private readonly TaskCompletionSource<Exception> _dispatchRefusedTcs = new();
    private readonly TaskCompletionSource<Exception> _requestFailedTcs = new();

    public void DispatchFailed(
        IncomingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception) => _dispatchFailedTcs.SetResult(exception);

    public void DispatchRefused(TransportConnectionInformation connectionInformation, Exception exception) =>
        _dispatchRefusedTcs.SetResult(exception);

    public void RequestPayloadContinuationFailed(
        OutgoingRequest request,
        TransportConnectionInformation connectionInformation,
        Exception exception) => _requestFailedTcs.SetResult(exception);
}
