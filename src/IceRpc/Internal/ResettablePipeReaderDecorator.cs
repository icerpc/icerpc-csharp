// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A decorator that allows to reset a PipeReader decoratee to its initial state (from the caller's
    /// perspective). Exceptions, cancellation and other events can prevent reset.</summary>
    internal class ResettablePipeReaderDecorator : PipeReader, IAsyncDisposable
    {
        public bool IsResettable { get; private set; } = true;

        // The latest sequence returned by _decoratee; not affected by Reset.
        private ReadOnlySequence<byte>? _sequence;

        // The latest consumed given by caller; reset by Reset.
        private SequencePosition? _consumed;

        // The highest examined given to _decoratee; not affected by Reset.
        private SequencePosition? _highestExamined;

        private readonly PipeReader _decoratee;

        // True when the caller complete this reader; reset by Reset.
        private bool _isReaderCompleted;

        private bool _isReadingInProgress;

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

            if (IsResettable)
            {
                ThrowIfCompleted();
                _consumed = consumed; // saved for the next ReadAsync/TryRead

                try
                {
                    _decoratee.AdvanceTo(_sequence.Value.Start, _highestExamined.Value);
                }
                catch
                {
                    IsResettable = false;
                    throw;
                }
            }
            else
            {
                // consumed is always going to be equal or greater to the consumed we passed in the IsResettable block
                // above (since we always passed _sequence.Value.Start).
                _decoratee.AdvanceTo(consumed, _highestExamined.Value);
            }
        }

        /// <inheritdoc/>
        // This method can be called from another thread so we always forward it to the decoratee directly.
        // ReadAsync/TryRead will return IsCanceled as appropriate.
        public override void CancelPendingRead() => _decoratee.CancelPendingRead();

        /// <inheritdoc/>
        public override void Complete(Exception? exception)
        {
            if (IsResettable)
            {
                if (_isReadingInProgress)
                {
                    Debug.Assert(_sequence != null);
                    AdvanceTo(_sequence.Value.End);
                }

                _isReaderCompleted = true;
                // and that's it
            }
            else
            {
                _isReadingInProgress = false;
                _decoratee.Complete(exception);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask CompleteAsync(Exception? exception)
        {
            if (IsResettable)
            {
                if (_isReadingInProgress)
                {
                    Debug.Assert(_sequence != null);
                    AdvanceTo(_sequence.Value.End);
                }

                _isReaderCompleted = true;
                // and that's it
            }
            else
            {
                _isReadingInProgress = false;
                await _decoratee.CompleteAsync(exception).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        /// <remarks>If not resettable, the caller must call Complete/CompleteAsync, as usual.</remarks>
        public ValueTask DisposeAsync() => IsResettable ? _decoratee.CompleteAsync() : default;

        /// <inheritdoc/>
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            _isReadingInProgress = !_isReadingInProgress ? true :
                throw new InvalidOperationException("reading is already in progress");

            if (IsResettable)
            {
                ThrowIfCompleted();
                try
                {
                    ReadResult readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (!readResult.IsCanceled)
                    {
                        _sequence = readResult.Buffer;
                    }

                    if (_consumed is SequencePosition consumed)
                    {
                        readResult = new ReadResult(
                            readResult.Buffer.Slice(consumed),
                            readResult.IsCanceled,
                            readResult.IsCompleted);
                    }
                    return readResult;
                }
                catch
                {
                    IsResettable = false;
                    throw;
                }
            }
            else
            {
                ReadResult readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!readResult.IsCanceled)
                {
                    _sequence = readResult.Buffer;
                }
                return readResult;
            }
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            _isReadingInProgress = !_isReadingInProgress ? true :
                throw new InvalidOperationException("reading is already in progress");

            if (IsResettable)
            {
                ThrowIfCompleted();
                try
                {
                    if (_decoratee.TryRead(out result))
                    {
                        if (!result.IsCanceled)
                        {
                            _sequence = result.Buffer;
                        }

                        if (_consumed is SequencePosition consumed)
                        {
                            result = new ReadResult(
                                result.Buffer.Slice(consumed),
                                result.IsCanceled,
                                result.IsCompleted);
                        }
                        // else keep result as is
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    IsResettable = false;
                    throw;
                }
            }
            else if (_decoratee.TryRead(out result))
            {
                if (!result.IsCanceled)
                {
                    _sequence = result.Buffer;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>Constructs a ResettablePipeReaderDecorator.</summary>
        internal ResettablePipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;

        /// <summary>Resets this pipe reader.</summary>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="IsResettable"/> is false.</exception>
        internal void Reset()
        {
            if (IsResettable)
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
            }
            else
            {
                throw new InvalidOperationException(
                    "cannot reset non-resettable ResettablePipeReaderDecorator");
            }
        }

        private void ThrowIfCompleted()
        {
            Debug.Assert(IsResettable);
            if (_isReaderCompleted)
            {
                throw new InvalidOperationException("the pipe reader is completed");
            }
        }
    }
}
