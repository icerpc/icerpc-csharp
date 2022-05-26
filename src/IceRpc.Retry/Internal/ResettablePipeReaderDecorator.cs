// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Retry.Internal;

/// <summary>A PipeReader decorator that allows to reset its decoratee to its initial state (from the caller's
/// perspective).</summary>
internal class ResettablePipeReaderDecorator : PipeReader
{
    /// <summary>Gets or sets whether this decorator can be reset.</summary>
    internal bool IsResettable
    {
        get => _isResettable;

        set
        {
            if (value)
            {
                throw new ArgumentException($"cannot set {nameof(IsResettable)} to true", nameof(value));
            }

            if (_isResettable)
            {
                // If Complete was called on this resettable decorator without an intervening Reset, we call Complete
                // on the decoratee.

                _isResettable = false;
                if (_isReaderCompleted)
                {
                    // We complete the decoratee with the saved exception (can be null).
                    Complete(_readerCompleteException);
                }
            }
        }
    }

    // The latest consumed given by caller; reset by Reset.
    private SequencePosition? _consumed;

    private readonly PipeReader _decoratee;

    // The highest examined given to _decoratee; not affected by Reset.
    private SequencePosition? _highestExamined;

    // True when the caller complete this reader; reset by Reset.
    private bool _isReaderCompleted;
    private bool _isReadingInProgress;
    private bool _isResettable = true;
    private readonly int _maxBufferSize;
    private Exception? _readerCompleteException;

    // The latest sequence returned by _decoratee; not affected by Reset.
    private ReadOnlySequence<byte>? _sequence;

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    /// <inheritdoc/>
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        _isReadingInProgress = _isReadingInProgress ? false :
            throw new InvalidOperationException("cannot call AdvanceTo before reading PipeReader");

        Debug.Assert(_sequence != null);

        // The examined given to _decoratee must be ever-increasing.
        if (_highestExamined == null) // first AdvanceTo ever
        {
            _highestExamined = examined;
        }
        else if (_sequence.Value.GetOffset(examined) > _sequence.Value.GetOffset(_highestExamined.Value))
        {
            _highestExamined = examined;
        }

        if (_isResettable)
        {
            ThrowIfCompleted();
            _consumed = consumed; // saved for the next ReadAsync/ReadAtLeastAsync/TryRead
            _decoratee.AdvanceTo(_sequence.Value.Start, _highestExamined.Value);
        }
        else
        {
            // consumed is always going to be equal or greater to the consumed we passed in the _isResettable block
            // above (since we always passed _sequence.Value.Start).
            _decoratee.AdvanceTo(consumed, _highestExamined.Value);
            _consumed = null;
        }
    }

    /// <inheritdoc/>
    // This method can be called from another thread so we always forward it to the decoratee directly.
    // ReadAsync/TryRead will return IsCanceled as appropriate.
    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    /// <inheritdoc/>
    public override void Complete(Exception? exception)
    {
        if (_isResettable)
        {
            if (_isReadingInProgress)
            {
                Debug.Assert(_sequence != null);
                AdvanceTo(_sequence.Value.End);
            }

            if (!_isReaderCompleted)
            {
                _isReaderCompleted = true;
                _readerCompleteException = exception;
            }
            // we naturally don't complete the decoratee, otherwise this decorator would no longer be resettable
        }
        else
        {
            _isReadingInProgress = false;
            _decoratee.Complete(exception);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("reading is already in progress");

        ThrowIfCompleted();

        ReadResult readResult;
        try
        {
            readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Complete exception from pipe writer source.
            _isResettable = false;
            throw;
        }
        return ProcessReadResult(readResult);
    }

    /// <inheritdoc/>
    public override bool TryRead(out ReadResult result)
    {
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("reading is already in progress");

        ThrowIfCompleted();
        try
        {
            if (_decoratee.TryRead(out result))
            {
                result = ProcessReadResult(result);
                return true;
            }
            else
            {
                return false;
            }
        }
        catch
        {
            // Complete exception from pipe writer source.
            _isResettable = false;
            throw;
        }
    }

    protected override async ValueTask<ReadResult> ReadAtLeastAsyncCore(
        int minimumSize,
        CancellationToken cancellationToken)
    {
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("reading is already in progress");

        ThrowIfCompleted();
        if (_consumed is SequencePosition consumed)
        {
            minimumSize += (int)_sequence!.Value.GetOffset(consumed);
        }

        ReadResult readResult;
        try
        {
            readResult = await _decoratee.ReadAtLeastAsync(minimumSize, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Complete exception from pipe writer source.
            _isResettable = false;
            throw;
        }
        return ProcessReadResult(readResult);
    }

    /// <summary>Constructs a ResettablePipeReaderDecorator.</summary>
    internal ResettablePipeReaderDecorator(PipeReader decoratee, int maxBufferSize)
    {
        _decoratee = decoratee;
        _maxBufferSize = maxBufferSize;
    }

    /// <summary>Resets this pipe reader.</summary>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IsResettable"/> is false.</exception>
    internal void Reset()
    {
        if (_isResettable)
        {
            if (_isReadingInProgress)
            {
                throw new InvalidOperationException(
                    "cannot reset ResettablePipeReaderDecorator while reading is in progress");
            }

            if (!_isReaderCompleted && _consumed != null)
            {
                throw new InvalidOperationException(
                    "cannot reset a non-completed partially consumed ResettablePipeReaderDecorator");
            }

            _consumed = null;
            _isReaderCompleted = false;
            _readerCompleteException = null;
        }
        else
        {
            throw new InvalidOperationException("cannot reset non-resettable ResettablePipeReaderDecorator");
        }
    }

    private ReadResult ProcessReadResult(ReadResult readResult)
    {
        _sequence = readResult.Buffer;

        // Remove bytes marked as consumed.
        if (_consumed is SequencePosition consumed)
        {
            // Removed bytes marked as consumed
            readResult = new ReadResult(
                readResult.Buffer.Slice(consumed),
                readResult.IsCanceled,
                readResult.IsCompleted);
        }

        if (_isResettable)
        {
            if (_sequence.Value.Length > _maxBufferSize)
            {
                _isResettable = false;
            }
        }
        return readResult;
    }

    private void ThrowIfCompleted()
    {
        if (_isReaderCompleted)
        {
            _isResettable = false;
            throw new InvalidOperationException("the pipe reader is completed");
        }
    }
}
