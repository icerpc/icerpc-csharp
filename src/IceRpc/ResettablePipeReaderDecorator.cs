// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Represents a <see cref="PipeReader" /> decorator that doesn't consume the data from the decoratee to allow
/// reading again this data from the beginning after being reset.</summary>
/// <remarks><para>The decorator becomes non-resettable if the decoratee's buffered data exceeds the maximum buffer size
/// provided to <see cref="ResettablePipeReaderDecorator(PipeReader, int)" /> or if the reading from the decoratee fails
/// with an exception other than <see cref="OperationCanceledException"/>.</para>
/// <para>Calling <see cref="Complete" /> on the decorator doesn't complete the decoratee to allow reading again the
/// data after the decorator is reset. It's therefore important to make the decorator non-resettable by setting <see
/// cref="IsResettable" /> to <see langword="false" /> to complete the decoratee.</para></remarks>
// The default CopyToAsync implementation is suitable for this reader implementation. It calls ReadAsync/AdvanceTo to
// read the data. This ensures that the decorated pipe reader buffered data is not consumed.
public sealed class ResettablePipeReaderDecorator : PipeReader
{
    /// <summary>Gets or sets a value indicating whether this decorator can be reset.</summary>
    /// <value><see langword="true"/> if this decorator can be reset; otherwise, <see langword="false"/>. Defaults to
    /// <see langword="true"/>.</value>
    /// <remarks>This property can only be set to <see langword="false" />. If <see cref="IsResettable"/> is <see
    /// langword="true" /> and <see cref="Complete" /> was called, the decoratee is completed.</remarks>
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
                AdvanceDecoratee();

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
    // The latest examined given by caller.
    private SequencePosition? _examined;

    // The highest examined given to _decoratee; not affected by Reset.
    private SequencePosition? _highestExamined;

    // True when read returned a canceled read result.
    private bool _isCanceled;
    // True when the caller complete this reader; reset by Reset.
    private bool _isReaderCompleted;
    private bool _isReadingInProgress;
    private bool _isResettable = true;
    private readonly int _maxBufferSize;
    private Exception? _readerCompleteException;

    // The latest sequence returned by _decoratee; not affected by Reset.
    private ReadOnlySequence<byte> _sequence;

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
        // If reading returns a canceled read result, _isReadInProgress is set to false since it's not required to call
        // AdvanceTo. Calling AdvanceTo after getting a canceled read result is also valid so we don't check if reading
        // is in progress in this case.
        if (!_isCanceled && !_isReadingInProgress)
        {
            throw new InvalidOperationException("Cannot call AdvanceTo before reading the PipeReader.");
        }

        _isReadingInProgress = false;

        Debug.Assert(_examined is null);

        if (_isResettable)
        {
            ThrowIfCompleted();

            // Don't call _decoratee.AdvanceTo just yet. It will be called on the next ReadAsync/TryRead call. This
            // way, if Reset is called next, it won't mark the data as examined and the following ReadAsync/TryRead
            // call won't block. It will return the buffered data.
            _examined = examined;
            _consumed = consumed;
        }
        else
        {
            // The examined position given to _decoratee.AdvanceTo must be ever-increasing.
            if (_highestExamined is not null &&
                _sequence.GetOffset(examined) < _sequence.GetOffset(_highestExamined.Value))
            {
                examined = _highestExamined.Value;
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
    /// allow reading again the data after a call to <see cref="Reset" />. To complete the decoratee, <see
    /// cref="IsResettable" /> must be set to <see langword="false" />.</remarks>
    public override void Complete(Exception? exception = default)
    {
        if (_isResettable)
        {
            if (_isReadingInProgress)
            {
                AdvanceTo(_sequence.Start);
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

        AdvanceDecoratee();

        ReadResult readResult;
        try
        {
            readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                _isCanceled = true;
                _isReadingInProgress = false;
            }
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
            if (_isReadingInProgress)
            {
                throw new InvalidOperationException(
                    "The resettable pipe reader decorator cannot be reset while reading is in progress.");
            }

            if (_examined is not null)
            {
                // Don't commit the caller's examined data on the decoratee. This ensures that the next ReadAsync call
                // returns synchronously with the decoratee's buffered data (instead of blocking).
                _decoratee.AdvanceTo(_sequence.Start, _highestExamined ?? _sequence.Start);
                _examined = null;
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

        AdvanceDecoratee();

        try
        {
            if (_decoratee.TryRead(out result))
            {
                if (result.IsCanceled)
                {
                    _isCanceled = true;
                    _isReadingInProgress = false;
                }
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
        _isReadingInProgress = !_isReadingInProgress ? true :
            throw new InvalidOperationException("Reading is already in progress.");

        ThrowIfCompleted();

        AdvanceDecoratee();

        long size = (_consumed is null ? 0 : _sequence.GetOffset(_consumed.Value)) + minimumSize;
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
            if (readResult.IsCanceled)
            {
                _isCanceled = true;
                _isReadingInProgress = false;
            }
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

    /// <summary>Advances the decoratee to the examined position saved by the AdvanceTo call on the decorator.</summary>
    /// <remarks>Calling AdvanceTo on the decoratee commits the latest examined data and ensures the next read call will
    /// read additional data if all the data was examined. If the decorator is reset, this ensures that the next read
    /// call will immediately return the buffered data instead of blocking.</remarks>
    private void AdvanceDecoratee()
    {
        _isCanceled = false;

        if (_isResettable && _examined is not null)
        {
            // The examined position given to _decoratee.AdvanceTo must be ever-increasing.
            if (_highestExamined is null ||
                _sequence.GetOffset(_examined.Value) > _sequence.GetOffset(_highestExamined.Value))
            {
                _highestExamined = _examined;
            }

            _decoratee.AdvanceTo(_sequence.Start, _highestExamined.Value);

            _examined = null;
        }
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

        // We don't retry when the buffered data exceeds the maximum buffer size.
        if (_isResettable && (_sequence.Length > _maxBufferSize))
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
