// Copyright (c) ZeroC, Inc.

namespace IceRpc.Tests.Common;

/// <summary>A property bag used to configure a <see cref="TransportOperations{T}" />. The enum template parameter is
/// expected to be a flags enumeration that can specify multiple operations.</summary>
public record class TransportOperationsOptions<T> where T : struct, Enum
{
    /// <summary>The operations configured to fail.</summary>
    public T Fail { get; set; }

    /// <summary>The exception raised by operations configured to fail.</summary>
    public Exception? FailureException { get; set; }

    /// <summary>The operations configured to block. An operation will unblock once it's configured to no longer
    /// block.</summary>
    public T Hold { get; set; }
}

/// <summary>A class to control the behavior of the operations from a transport interface.</summary>
public class TransportOperations<T> where T : struct, Enum
{
    /// <summary>The operations configured to fail.</summary>
    public T Fail { get; set; }

    /// <summary>The exception raised by operations configured to fail.</summary>
    public Exception FailureException { get; set; }

    /// <summary>The operations configured to block. An operation will unblock once it's configured to no longer
    /// block.</summary>
    public T Hold
    {
        get => _holdOperations;

        set
        {
            _holdOperations = value;

            foreach (T operation in Enum.GetValues<T>())
            {
                if (!_holdOperationsTcsMap.TryGetValue(operation, out TaskCompletionSource? tcs))
                {
                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _holdOperationsTcsMap.Add(operation, tcs);
                }

                if (!_holdOperations.HasFlag(operation))
                {
                    // The operation is no longer part of the set of operations that must be held. We complete its TCS
                    // to eventually unblock threads waiting for the operation to complete.
                    tcs.TrySetResult();
                }
                else if (tcs.Task.IsCompleted)
                {
                    // If the operation is part of the set of operations that must be held and its TCS is completed,
                    // we create a new TCS to ensure that the operation will block when called.
                    _holdOperationsTcsMap[operation] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }
    }

    private readonly Dictionary<T, TaskCompletionSource> _calledOperationsTcsMap = new();
    private T _holdOperations;
    private readonly Dictionary<T, TaskCompletionSource> _holdOperationsTcsMap = new();

    /// <summary>Gets a task which can be awaited to wait for the given operation to be called. The task must be
    /// obtained before the operation is called. It can't be obtained again until the operation is called.</summary>
    /// <param name="operation">The operation for which the task will complete when the operation is called.</param>
    /// <exception cref="InvalidOperationException">Thrown if the task has already been returned and the operation has
    /// not been called yet. The caller should only obtain a new task after the operation has been called.</exception>
    public Task GetCalledTask(T operation)
    {
        if (_calledOperationsTcsMap.TryGetValue(operation, out TaskCompletionSource? tcs) && !tcs.Task.IsCompleted)
        {
            throw new InvalidOperationException(
                $"Can't get twice a completed task for {operation} if the previous task is not completed.");
        }
        else
        {
            _calledOperationsTcsMap.Remove(operation);
        }

        tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _calledOperationsTcsMap.Add(operation, tcs);
        return tcs.Task;
    }

    internal TransportOperations(TransportOperationsOptions<T> options)
    {
        Hold = options.Hold;
        Fail = options.Fail;
        FailureException =
            options.FailureException ??
            new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
    }

    internal async Task CallAsync(T operation, Func<Task> callAsyncFunc, CancellationToken cancellationToken)
    {
        await CheckAsync(operation, cancellationToken);
        await callAsyncFunc();
        // Check again hold/failure in case the configuration was changed after the completion of the operation.
        await CheckAsync(operation, cancellationToken);
    }

    internal async Task<TResult> CallAsync<TResult>(
        T operation,
        Func<Task<TResult>> callAsyncFunc,
        CancellationToken cancellationToken)
    {
        await CheckAsync(operation, cancellationToken);
        TResult result = await callAsyncFunc();
        // Check again hold/failure in case the configuration was changed after the completion of the operation.
        await CheckAsync(operation, cancellationToken);
        return result;
    }

    internal async ValueTask CallAsync(
        T operation,
        Func<ValueTask> callAsyncFunc,
        CancellationToken cancellationToken) =>
        await CallAsync(operation, () => callAsyncFunc().AsTask(), cancellationToken);

    internal async ValueTask<TResult> CallAsync<TResult>(
        T operation,
        Func<ValueTask<TResult>> callAsyncFunc,
        CancellationToken cancellationToken) =>
        await CallAsync(operation, () => callAsyncFunc().AsTask(), cancellationToken);

    internal void CallDispose(T disposeOperation, IDisposable disposable)
    {
        CheckAsync(disposeOperation, CancellationToken.None).Wait();
        disposable.Dispose();
        Complete();
    }

    internal async ValueTask CallDisposeAsync(T disposeOperation, IAsyncDisposable disposable)
    {
        CheckAsync(disposeOperation, CancellationToken.None).Wait();
        await disposable.DisposeAsync();
        Complete();
    }

    /// <summary>Checks if the operation should fail and if it should be held. If the operation is configured to fail,
    /// <see cref="FailureException" /> is raised. It also marks the operation as called.</summary>
    internal Task CheckAsync(T operation, CancellationToken cancellationToken)
    {
        // Get the configuration before notifying the test that the operation was called. If the test updates the
        // configuration after the notification, we'll still use the configuration setup before the operation is called.
        TaskCompletionSource tcs = _holdOperationsTcsMap[operation];
        T fail = Fail;
        Exception failureException = FailureException;

        Called(operation);

        if (fail.HasFlag(operation))
        {
            throw failureException;
        }
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Completes the called task for the given operation.</summary>
    internal void Called(T operation)
    {
        if (_calledOperationsTcsMap.TryGetValue(operation, out TaskCompletionSource? tcs))
        {
            tcs.TrySetResult();
        }
    }

    internal void Complete()
    {
        foreach (TaskCompletionSource tcs in _holdOperationsTcsMap.Values)
        {
            tcs.TrySetResult();
        }
        foreach ((T operation, TaskCompletionSource tcs) in _calledOperationsTcsMap)
        {
            tcs.TrySetException(new Exception($"The operation {operation} was not called."));
        }
    }
}
