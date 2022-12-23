// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

public sealed class TestDispatcher : IDispatcher, IAsyncDisposable
{
    public Task DispatchComplete => _completeTaskCompletionSource.Task;
    public Task<IncomingRequest> DispatchStart => _startTaskCompletionSource.Task;

    /// <summary>A payload pipe reader decorator that represents the last response send by this dispatcher.
    /// </summary>
    public PayloadPipeReaderDecorator? ResponsePayload { get; set; }

    private readonly TaskCompletionSource _completeTaskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SemaphoreSlim _hold = new(0);
    private readonly TaskCompletionSource<IncomingRequest> _startTaskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly byte[]? _responsePayload;
    private readonly int _holdDispatchCount;
    private int _dispatchCount;

    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        PipeReader responsePayload;
        if (_responsePayload is null)
        {
            responsePayload = EmptyPipeReader.Instance;
        }
        else
        {
            var pipe = new Pipe();
            await pipe.Writer.WriteAsync(_responsePayload, cancellationToken);
            pipe.Writer.Complete();
            responsePayload = pipe.Reader;
        }
        ResponsePayload = new PayloadPipeReaderDecorator(responsePayload);

        _startTaskCompletionSource.TrySetResult(request);
        try
        {
            if (_dispatchCount++ < _holdDispatchCount)
            {
                await _hold.WaitAsync(cancellationToken);
            }

            _completeTaskCompletionSource.TrySetResult();

            return new OutgoingResponse(request)
            {
                Payload = ResponsePayload
            };
        }
        catch (Exception exception)
        {
            _completeTaskCompletionSource.TrySetException(exception);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hold.CurrentCount > 0)
        {
            _hold.Release(_hold.CurrentCount);
        }

        try
        {
            await _completeTaskCompletionSource.Task.ConfigureAwait(false);
        }
        catch
        {
            // Expected. Prevent unobserved task exception.
        }

        _hold.Dispose();
    }

    public int ReleaseDispatch() => _hold.Release();

    public TestDispatcher(byte[]? responsePayload = null, int holdDispatchCount = 1)
    {
        _responsePayload = responsePayload;
        _holdDispatchCount = holdDispatchCount;
    }
}
