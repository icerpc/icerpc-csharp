// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A payload pipe reader decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeReaderDecorator : PipeReader
{
    /// <summary>A task that completes when <see cref="PipeReader.Complete(Exception?)"/> is called.</summary>
    public Task<Exception?> Completed => _completed.Task;

    /// <summary>A task that completes when <see cref="PipeReader.ReadAsync"/> is called.</summary>
    public Task ReadCalled => _readCalledTcs.Task;

    /// <summary>Specifies if <see cref="PipeReader.ReadAsync" /> should block.</summary>
    public bool HoldRead
    {
        get => _holdRead;

        set
        {
            _holdRead = value;
            _holdReadTcs ??= new(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_holdRead)
            {
                _holdReadTcs.TrySetResult();
            }
            else if (_holdReadTcs.Task.IsCompleted)
            {
                _holdReadTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    /// <summary>Specifies if <see cref="PipeReader.ReadAsync" /> was canceled."</summary>
    public bool IsReadCanceled { get; private set; }

    private readonly TaskCompletionSource<Exception?> _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PipeReader _decoratee;
    private bool _holdRead;
    private TaskCompletionSource? _holdReadTcs;
    private readonly TaskCompletionSource _readCalledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    /// <inheritdoc/>
    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    /// <inheritdoc/>
    public override void Complete(Exception? exception = null)
    {
        _completed.TrySetResult(exception);
        _decoratee.Complete(exception);
    }

    /// <inheritdoc/>
    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        _readCalledTcs.TrySetResult();
        try
        {
            if (_holdRead)
            {
                await _holdReadTcs!.Task.WaitAsync(cancellationToken);
            }
            return await _decoratee.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
        {
            IsReadCanceled = true;
            throw;
        }
    }

    /// <inheritdoc/>
    public override bool TryRead(out ReadResult result)
    {
        _readCalledTcs.TrySetResult();
        if (_holdRead)
        {
            result = default;
            return false;
        }
        else
        {
            return _decoratee.TryRead(out result);
        }
    }

    /// <summary>Construct a payload pipe reader decorator.</summary>
    /// <param name="decoratee">The decoratee.</param>
    public PayloadPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
}
