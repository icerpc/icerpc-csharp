// Copyright (c) ZeroC, Inc.

namespace IceRpc.Tests.Common;

/// <summary>A property bag used to configure a <see cref="TransportOperations{T}" />.</summary>
public record struct TransportOperationsOptions<T> where T: struct, Enum
{
    /// <summary>The operations configured to fail.</summary>
    public T Fail { get; set; }

    /// <summary>The exception raised by operations configured to fail.</summary>
    public Exception? FailureException { get; set; }

    /// <summary>The operations configured to block. An operation will unblock once it's configured to no longer
    /// block.</summary>
    public T Hold { get; set; }
}

/// <summary>A class to control the behavior of an operation from a transport interface.</summary>
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

            foreach (T operation in Enum.GetValues(typeof(T)))
            {
                if (!_holdOperationsTcsMap.TryGetValue(operation, out TaskCompletionSource? tcs))
                {
                    tcs = new TaskCompletionSource();
                    _holdOperationsTcsMap.Add(operation, tcs);
                }

                if (!_holdOperations.HasFlag(operation))
                {
                    tcs.TrySetResult();
                }
                else if (tcs.Task.IsCompleted)
                {
                    _holdOperationsTcsMap[operation] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }
    }

    private readonly Dictionary<T, TaskCompletionSource> _calledOperationsTcsMap = new();
    private T _holdOperations;
    private readonly Dictionary<T, TaskCompletionSource> _holdOperationsTcsMap = new();

    /// <summary>Returns a task which can be awaited to wait for the given operation to be called.</summary>
    public Task CalledTask(T operation) => _calledOperationsTcsMap[operation].Task;

    internal TransportOperations(T holdOperations, T failOperations, Exception? failureException = null)
    {
        Hold = holdOperations;
        Fail = failOperations;
        FailureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");

        foreach (T operation in Enum.GetValues(typeof(T)))
        {
            _calledOperationsTcsMap[operation] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal TransportOperations(TransportOperationsOptions<T> options)
    {
        Hold = options.Hold;
        Fail = options.Fail;
        FailureException =
            options.FailureException ??
            new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");

        foreach (T operation in Enum.GetValues(typeof(T)))
        {
            _calledOperationsTcsMap[operation] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Checks if the operation should fail and if it should be held. If the operation is configured to fail,
    /// <see cref="FailureException" /> is raised. It also marks the operation as called.</summary>
    internal Task CheckAsync(T operation, CancellationToken cancellationToken)
    {
        if (Fail.HasFlag(operation))
        {
            throw FailureException;
        }
        Called(operation);
        return _holdOperationsTcsMap[operation].Task.WaitAsync(cancellationToken);
    }

    /// <summary>Completes the called task for the given operation.</summary>
    internal void Called(T operation) => _calledOperationsTcsMap[operation].TrySetResult();

    internal void Complete()
    {
        foreach (TaskCompletionSource tcs in _holdOperationsTcsMap.Values)
        {
            tcs.TrySetResult();
        }
        foreach ((T operation, TaskCompletionSource tcs) in _calledOperationsTcsMap)
        {
            tcs.TrySetResult();
        }
    }
}
