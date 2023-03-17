// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>A PipeReader decorator that allows to reset its decoratee to its initial state (from the caller's
/// perspective).</summary>
public sealed class ResettablePipeReaderDecorator : PipeReader
{
    /// <summary>Gets or sets a value indicating whether this decorator can be reset.</summary>
    /// <value><see langword="true"/> if this decorator can be reset; <see langword="false"/> otherwise.</value>
    public bool IsResettable
    {
        get => _isResettable;

        set
        {
            if (value)
            {
                throw new ArgumentException(
                    $"The {nameof(IsResettable)} property cannot be set to true.",
                    nameof(value));
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
            throw new InvalidOperationException("Cannot call AdvanceTo before reading the PipeReader.");

        // All successful calls to ReadAsync/TryRead set _sequence.
        Debug.Assert(_sequence is not null);

        // The examined given to _decoratee must be ever-increasing.
        if (_highestExamined is null)
        {
            // first AdvanceTo ever
            _highestExamined = examined;
        }
        else if (_sequence.Value.GetOffset(examined) > _sequence.Value.GetOffset(_highestExamined.Value))
        {
            _highestExamined = examined;
        }

        if (_isResettable)
        {
            ThrowIfCompleted();
            _consumed = consumed; // saved to slice the buffer returned by the next ReadAsync/ReadAtLeastAsync/TryRead
            _decoratee.AdvanceTo(_sequence.Value.Start, _highestExamined.Value);
        }
        else
        {
            // the first time around, consumed is necessarily equals to or greater than the _sequence.Value.Start passed
            // by the preceding call in the_isResettable=true block above
            _decoratee.AdvanceTo(consumed, _highestExamined.Value);
            _consumed = null; // don't slice the buffer returned by the next ReadAsync/ReadAtAtLeastAsync/TryRead
        }
    }

    /// <inheritdoc/>
    // This method can be called from another thread so we always forward it to the decoratee directly.
    // ReadAsync/ReadAtLeastAsync/TryRead will return IsCanceled as appropriate.
    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    /// <inheritdoc/>
    public override void Complete(Exception? exception = default)
    {
        if (_isResettable)
        {
            if (_isReadingInProgress)
            {
                Debug.Assert(_sequence is not null);
                AdvanceTo(_sequence.Value.Start);
            }

            if (!_isReaderCompleted)
            {
                // Only save the first call to Complete
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
    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

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

    /// <summary>Constructs a ResettablePipeReaderDecorator. This decorator avoids consuming the read data to allow
    /// restart reading from the beginning, once the buffered data exceeds the <paramref name="maxBufferSize"/> the
    /// decorator becomes non resettable and <see cref="IsResettable"/> returns <see langword="false" />.</summary>
    /// <param name="decoratee">The pipe reader being decorated.</param>
    /// <param name="maxBufferSize">The maximum size of buffered data in bytes.</param>
    public ResettablePipeReaderDecorator(PipeReader decoratee, int maxBufferSize)
    {
        _decoratee = decoratee;
        _maxBufferSize = maxBufferSize;
    }

    /// <summary>Resets this pipe reader.</summary>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IsResettable" /> is <see langword="false" />.
    /// </exception>
    public void Reset()
    {
        if (_isResettable)
        {
            if (_isReadingInProgress)
            {
                throw new InvalidOperationException(
                    "The ResettablePipeReaderDecorator cannot be reset while reading is in progress.");
            }

            _consumed = null;
            _isReaderCompleted = false;
            _readerCompleteException = null;
        }
        else
        {
            throw new InvalidOperationException("Cannot reset non-resettable ResettablePipeReaderDecorator.");
        }
    }

    /// <inheritdoc/>
    public override bool TryRead(out ReadResult result)
    {
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

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

    /// <inheritdoc/>
    protected override async ValueTask<ReadResult> ReadAtLeastAsyncCore(
        int minimumSize,
        CancellationToken cancellationToken = default)
    {
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

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

    private ReadResult ProcessReadResult(ReadResult readResult)
    {
        _sequence = readResult.Buffer;

        if (_consumed is SequencePosition consumed)
        {
            // Removed bytes marked as consumed
            readResult = new ReadResult(
                readResult.Buffer.Slice(consumed),
                readResult.IsCanceled,
                readResult.IsCompleted);
        }

        // When the application requests cancellation via CancelPendingRead, we don't retry.
        if (_isResettable && (_sequence.Value.Length > _maxBufferSize || readResult.IsCanceled))
        {
            _isResettable = false;
        }

        return readResult;
    }

    private void ThrowIfCompleted()
    {
        if (_isReaderCompleted)
        {
            _isResettable = false;
            throw new InvalidOperationException("The pipe reader is completed.");
        }
    }
}
