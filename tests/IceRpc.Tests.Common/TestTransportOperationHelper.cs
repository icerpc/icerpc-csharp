// Copyright (c) ZeroC, Inc.

namespace IceRpc.Tests.Common;

public sealed class TestTransportOperationHelper<T> where T : struct, Enum
{
    public T Fail { get; set; }

    public Exception FailureException { get; set; }

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

    /// <summary>Returns the called task to await the given operation to be called.</summary>
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
