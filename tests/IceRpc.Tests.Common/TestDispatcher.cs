// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

public sealed class TestDispatcher : IDispatcher, IDisposable
{
    public Task DispatchComplete => _completeTaskCompletionSource.Task;
    public Task<IConnectionContext> DispatchStart => _startTaskCompletionSource.Task;

    private readonly TaskCompletionSource _completeTaskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SemaphoreSlim _hold = new(0);
    private readonly TaskCompletionSource<IConnectionContext> _startTaskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        _startTaskCompletionSource.TrySetResult(request.ConnectionContext);
        try
        {
            await _hold.WaitAsync(cancellationToken);
            _completeTaskCompletionSource.TrySetResult();
            return new OutgoingResponse(request);
        }
        catch (Exception exception)
        {
            _completeTaskCompletionSource.TrySetException(exception);
            throw;
        }
    }

    public void Dispose()
    {
        if (_hold.CurrentCount > 0)
        {
            _hold.Release(_hold.CurrentCount);
        }
        _hold.Dispose();
    }

    public int ReleaseDispatch() => _hold.Release();
}
