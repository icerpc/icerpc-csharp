// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Represents a <see cref="PipeReader" /> decorator that allows to reset its decoratee to its initial state
/// (from the caller's perspective).</summary>
// The default CopyToAsync implementation is suitable for this reader implementation. It calls ReadAsync/AdvanceTo to
// read the data. This ensures that the decorated pipe reader buffered data is not consumed.
public sealed class ResettablePipeReaderDecorator : PipeReader
{
    /// <summary>Gets or sets a value indicating whether this decorator can be reset.</summary>
    /// <value><see langword="true"/> if this decorator can be reset; otherwise, <see langword="false"/>. Defaults to
    /// <see langword="true"/>.</value>
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

    /// <summary>Constructs a resettable pipe reader decorator. This decorator avoids consuming the read data to allow
    /// restart reading from the beginning. The decorator becomes non-resettable and <see cref="IsResettable"/> returns
    /// <see langword="false" /> if the buffered data exceeds the <paramref name="maxBufferSize"/>, if the reading fails
    /// or if it's canceled with <see cref="CancelPendingRead" />.</summary>
    /// <param name="decoratee">The pipe reader being decorated.</param>
    /// <param name="maxBufferSize">The maximum size of buffered data in bytes.</param>
    public ResettablePipeReaderDecorator(PipeReader decoratee, int maxBufferSize)
    {
        _decoratee = decoratee;
        _maxBufferSize = maxBufferSize;
    }

    /// <summary>Moves forward the pipeline's read cursor to after the consumed data. No data is consumed while
    /// <see cref="IsResettable"/> value is true.</summary>
    /// <param name="consumed">Marks the extent of the data that has been successfully processed.</param>
    /// <seealso cref="PipeReader.AdvanceTo(SequencePosition)"/>
    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    /// <summary>Moves forward the pipeline's read cursor to after the consumed data. No data is consumed while
    /// <see cref="IsResettable"/> value is true.</summary>
    /// <param name="consumed">Marks the extent of the data that has been successfully processed.</param>
    /// <param name="examined">Marks the extent of the data that has been read and examined.</param>
    /// <seealso cref="PipeReader.AdvanceTo(SequencePosition, SequencePosition)"/>
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

    /// <summary>Cancels the pending <see cref="ReadAsync(CancellationToken)"/> operation without causing it to throw
    /// and without completing the <see cref="PipeReader"/>. If there is no pending operation, this cancels the next
    /// operation.</summary>
    /// <seealso cref="PipeReader.CancelPendingRead"/>
    // This method can be called from another thread so we always forward it to the decoratee directly.
    // ReadAsync/ReadAtLeastAsync/TryRead will return IsCanceled as appropriate.
    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    /// <summary>Signals to the producer that the consumer is done reading.</summary>
    /// <param name="exception">Optional <see cref="Exception "/> indicating a failure that's causing the pipeline to
    /// complete.</param>
    /// <seealso cref="PipeReader.Complete(Exception?)"/>
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

    /// <summary>Asynchronously reads a sequence of bytes from the current <see cref="PipeReader"/>.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> representing the asynchronous read operation.</returns>
    /// <seealso cref="PipeReader.ReadAsync(CancellationToken)"/>
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
            _isResettable = false;
            throw;
        }
        return ProcessReadResult(readResult);
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
                    "The resettable pipe reader decorator cannot be reset while reading is in progress.");
            }

            _consumed = null;
            _isReaderCompleted = false;
            _readerCompleteException = null;
        }
        else
        {
            throw new InvalidOperationException("Cannot reset non-resettable pipe reader decorator.");
        }
    }

    /// <summary>Attempts to synchronously read data from the <see cref="PipeReader"/>.</summary>
    /// <param name="result">When this method returns <see langword="true"/>, this value is set to a
    /// <see cref="ReadResult"/> instance that represents the result of the read call; otherwise, this value is set to
    /// <see langword="default"/>.</param>
    /// <returns><see langword="true"/> if data was available, or if the call was canceled or the writer was completed;
    /// otherwise, <see langword="false"/>.</returns>
    /// <seealso cref="PipeReader.TryRead(out ReadResult)"/>.
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
            _isResettable = false;
            throw;
        }
    }

    /// <summary>Asynchronously reads a sequence of bytes from the current PipeReader.</summary>
    /// <param name="minimumSize">The minimum length that needs to be buffered in order for the call to return.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> representing the asynchronous read operation.</returns>
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
            // Remove bytes marked as consumed
            readResult = new ReadResult(
                readResult.Buffer.Slice(consumed),
                readResult.IsCanceled,
                readResult.IsCompleted);
        }

        // We don't retry when the buffered data exceeds the maximum buffer size or when the application requests
        // cancellation via CancelPendingRead.
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
