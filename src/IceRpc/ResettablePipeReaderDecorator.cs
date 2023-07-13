// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Represents a <see cref="PipeReader" /> decorator that doesn't consume the data from the decoratee to allow
/// reading again this data from the beginning after being reset.</summary>
/// <remarks>The decorator becomes non-resettable if the decoratee's buffered data exceeds the maximum buffer size
/// provided to <see cref="ResettablePipeReaderDecorator(PipeReader, int)" /> or if the reading from the decoratee
/// fails with an exception other than <see cref="OperationCanceledException"/>.</remarks>
// The default CopyToAsync implementation is suitable for this reader implementation. It calls ReadAsync/AdvanceTo to
// read the data. This ensures that the decorated pipe reader buffered data is not consumed.
public sealed class ResettablePipeReaderDecorator : PipeReader
{
    /// <summary>Gets or sets a value indicating whether this decorator can be reset.</summary>
    /// <value><see langword="true"/> if this decorator can be reset; otherwise, <see langword="false"/>. Defaults to
    /// <see langword="true"/>.</value>
    /// <remarks>This property can only be set to <see langword="false" />. If <see cref="IsResettable"/> value is true
    /// and <see cref="Complete" /> was called on the decorator, the decoratee will be completed.</remarks>
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
                AdvanceReader(commitExamined: true);

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
    private long _consumed;
    private readonly PipeReader _decoratee;
    // The latest examined given by caller.
    private long _examined;

    // The highest examined given to _decoratee; not affected by Reset.
    private long _highestExamined;

    // True when the caller complete this reader; reset by Reset.
    private bool _isReaderCompleted;
    private bool _isReadingInProgress;
    private bool _isResettable = true;
    private readonly int _maxBufferSize;
    private Exception? _readerCompleteException;

    // The latest sequence returned by _decoratee; not affected by Reset.
    private ReadResult _readResult;

    /// <summary>Constructs a resettable pipe reader decorator.</summary>
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

        Debug.Assert(_examined == 0);

        if (_isResettable)
        {
            ThrowIfCompleted();
            _examined = _readResult.Buffer.GetOffset(examined);
            _consumed = _readResult.Buffer.GetOffset(consumed);
        }
        else
        {
            // the first time around, consumed is necessarily equals to or greater than the _sequence.Buffer.Start passed
            // by the preceding call in the_isResettable=true block above
            if (_readResult.Buffer.GetOffset(examined) <= _highestExamined)
            {
                examined = _readResult.Buffer.GetPosition(_highestExamined);
            }
            _decoratee.AdvanceTo(consumed, examined);
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
    /// <remarks>If <see cref="IsResettable"/> value is true, <see cref="Complete" /> is not called on the decoratee to
    /// allow reading again the data after a call to <see cref="Reset" />.</remarks>
    public override void Complete(Exception? exception = default)
    {
        if (_isResettable)
        {
            if (_isReadingInProgress)
            {
                AdvanceTo(_readResult.Buffer.Start);
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
        if (_readResult.IsCanceled)
        {
            _isReadingInProgress = false;
        }

        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

        ThrowIfCompleted();

        AdvanceReader(commitExamined: true);

        ReadResult readResult;
        try
        {
            readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _isReadingInProgress = false;
            throw;
        }
        catch
        {
            _isResettable = false;
            throw;
        }
        return ProcessReadResult(readResult);
    }

    /// <summary>Resets this pipe reader.</summary>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IsResettable" /> is <see langword="false" /> or
    /// if reading is in progress.</exception>
    public void Reset()
    {
        if (_isResettable)
        {
            if (_readResult.IsCanceled)
            {
                _isReadingInProgress = false;
            }

            if (_isReadingInProgress)
            {
                throw new InvalidOperationException(
                    "The resettable pipe reader decorator cannot be reset while reading is in progress.");
            }

            // Don't commit the caller's examined data on the decoratee to ensure that the ReadAsync call after Reset
            // returns synchronously the decoratee's buffered data.
            AdvanceReader(commitExamined: false);

            _consumed = 0;
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
        if (_readResult.IsCanceled)
        {
            _isReadingInProgress = false;
        }

        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

        ThrowIfCompleted();

        AdvanceReader(commitExamined: true);

        try
        {
            if (_decoratee.TryRead(out result))
            {
                result = ProcessReadResult(result);
                return true;
            }
            else
            {
                _isReadingInProgress = false;
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
        if (_readResult.IsCanceled)
        {
            _isReadingInProgress = false;
        }

        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

        ThrowIfCompleted();

        AdvanceReader(commitExamined: true);

        long size = _consumed + minimumSize;
        if (size > int.MaxValue)
        {
            // In theory this shouldn't happen if _maxBufferSize is set to a reasonable value.
            throw new InvalidOperationException("Can't buffer more data than int.MaxValue");
        }
        minimumSize = (int)size;

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

    private void AdvanceReader(bool commitExamined)
    {
        if (_isResettable && (_readResult.Buffer.Length > 0 || _readResult.IsCompleted))
        {
            // The examined given to _decoratee must be ever-increasing.
            if (commitExamined && _examined > _highestExamined)
            {
                _highestExamined = _examined;
            }

            _decoratee.AdvanceTo(_readResult.Buffer.Start, _readResult.Buffer.GetPosition(_highestExamined));
            _readResult = default;
            _examined = 0;
        }
    }

    private ReadResult ProcessReadResult(ReadResult readResult)
    {
        _readResult = readResult;

        if (_consumed > 0)
        {
            // Remove bytes marked as consumed
            readResult = new ReadResult(
                readResult.Buffer.Slice(_consumed),
                readResult.IsCanceled,
                readResult.IsCompleted);
        }

        // We don't retry when the buffered data exceeds the maximum buffer size.
        if (_isResettable && (_readResult.Buffer.Length > _maxBufferSize))
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
