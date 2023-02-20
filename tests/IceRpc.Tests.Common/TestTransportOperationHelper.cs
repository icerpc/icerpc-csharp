// Copyright (c) ZeroC, Inc.

namespace IceRpc.Tests.Common;

/// <summary>A template class to configure the behavior of operations from transport interfaces.</summary>
public sealed class TestTransportOperationHelper<T> where T : struct, Enum
{
    /// <summary>The transport operations configured to fail.</summary>
    public T Fail { get; set; }

    /// <summary>The exception raised by operations configured to fail.</summary>
    public Exception FailureException { get; set; }

    /// <summary>The transport operations configured to block. An operation will unblock once it's configured to no
    /// longer block.</summary>
    public T Hold
    {
        get => _holdOperations;

        set
        {
            _holdOperations = value;

            foreach (T operation in Enum.GetValues(typeof(T)))
            {
                if (!_holdOperationsTcs.TryGetValue(operation, out TaskCompletionSource? tcs))
                {
                    tcs = new TaskCompletionSource();
                    _holdOperationsTcs.Add(operation, tcs);
                }

                if (!_holdOperations.HasFlag(operation))
                {
                    tcs.TrySetResult();
                }
                else if (tcs.Task.IsCompleted)
                {
                    _holdOperationsTcs[operation] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }
    }

    private readonly Dictionary<T, TaskCompletionSource> _calledOperationsTcs = new();
    private T _holdOperations;
    private readonly Dictionary<T, TaskCompletionSource> _holdOperationsTcs = new();

    /// <summary>Returns a task which can be awaited to wait for the given operation to be called.</summary>
    public Task CalledTask(T operation) => _calledOperationsTcs[operation].Task;

    internal TestTransportOperationHelper(T holdOperations, T failOperations, Exception? failureException = null)
    {
        Hold = holdOperations;
        Fail = failOperations;
        FailureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");

        foreach (T operation in Enum.GetValues(typeof(T)))
        {
            _calledOperationsTcs[operation] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Marks the operation as called, checks if the operation should fail and it should be held.</summary>
    internal Task CheckAsync(T operation, CancellationToken cancellationToken)
    {
        if (Fail.HasFlag(operation))
        {
            throw FailureException;
        }
        Called(operation);
        return _holdOperationsTcs[operation].Task.WaitAsync(cancellationToken);
    }

    /// <summary>Completes the operation called task.</summary>
    internal void Called(T operation) => _calledOperationsTcs[operation].TrySetResult();

    internal void Complete()
    {
        foreach (TaskCompletionSource tcs in _holdOperationsTcs.Values)
        {
            tcs.TrySetResult();
        }
        foreach ((T operation, TaskCompletionSource tcs) in _calledOperationsTcs)
        {
            tcs.TrySetResult();
        }
    }
}
