// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A helper dispatcher used by tests.</summary>
public sealed class TestDispatcher : IDispatcher, IDisposable
{
    /// <summary>A task that completes when the first operation dispatch completes, if the dispatch operation throws
    /// an exception this task complete with the same exception.</summary>
    public Task<Exception?> DispatchComplete => _completeTaskCompletionSource.Task;

    /// <summary>A task that completes once the dispatch has started, this task completes after
    /// <see cref="ResponsePayload"/> has been set.</summary>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_hold.CurrentCount > 0)
        {
            _hold.Release(_hold.CurrentCount);
        }
        _hold.Dispose();
    }

    /// <summary>Release the dispatch semaphore.</summary>
    /// <returns>The release semaphore previous count.</returns>
    public int ReleaseDispatch() => _hold.Release();

    /// <summary>Construct a test dispatcher.</summary>
    /// <param name="responsePayload">The response that <see cref="DispatchAsync(IncomingRequest, CancellationToken)"/>
    /// implementation writes to response's payloads.</param>
    /// <param name="holdDispatchCount">The number of requests that will try to acquire the dispatch semaphore, before
    /// completing the <see cref="DispatchComplete"/> task.</param>
    public TestDispatcher(byte[]? responsePayload = null, int holdDispatchCount = 0)
    {
        _responsePayload = responsePayload;
        _holdDispatchCount = holdDispatchCount;
    }
}
