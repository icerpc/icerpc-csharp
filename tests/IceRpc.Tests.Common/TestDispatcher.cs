// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

public sealed class TestDispatcher : IDispatcher, IDisposable
{
    public Task<Exception?> DispatchComplete => _completeTaskCompletionSource.Task;
    public Task<IncomingRequest> DispatchStart => _startTaskCompletionSource.Task;

    /// <summary>Gets a payload pipe reader decorator that represents the last response send by this dispatcher.
    /// </summary>
    public PayloadPipeReaderDecorator? ResponsePayload { get; private set; }

    private readonly TaskCompletionSource<Exception?> _completeTaskCompletionSource =
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

            _completeTaskCompletionSource.TrySetResult(null);

            return new OutgoingResponse(request)
            {
                Payload = ResponsePayload
            };
        }
        catch (Exception exception)
        {
            _completeTaskCompletionSource.TrySetResult(exception);
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

    public TestDispatcher(byte[]? responsePayload = null, int holdDispatchCount = 0)
    {
        _responsePayload = responsePayload;
        _holdDispatchCount = holdDispatchCount;
    }
}
