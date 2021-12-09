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

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (_sequence == null)
            {
                throw new InvalidOperationException("cannot call AdvanceTo before reading PipeReader");
            }

            // The examined given to _decoratee must be ever-increasing, but we can't compare sequence positions
            // directly.
            if (_highestExamined == null) // first AdvanceTo ever
            {
                _highestExamined = examined;
            }
            else
            {
                long currentOffset = _sequence.Value.GetOffset(_highestExamined.Value);
                long newOffset = _sequence.Value.GetOffset(examined);
                if (newOffset > currentOffset)
                {
                    _highestExamined = examined;
                }
            }

            if (IsResettable)
            {
                ThrowIfCompleted();
                _consumed = consumed;

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
                // consumed is always going to be greater than the consumed we passed in IsResettable (since we always
                // passed _sequence.Value.Start).
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
                _isReaderCompleted = true;
                // and that's it
            }
            else
            {
                _decoratee.Complete(exception);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask CompleteAsync(Exception? exception)
        {
            if (IsResettable)
            {
                _isReaderCompleted = true;
                // and that's it
            }
            else
            {
                await _decoratee.CompleteAsync(exception).ConfigureAwait(false);
            }
        }

        // If not resettable, the caller must call Complete/CompleteAsync, as usual.
        public ValueTask DisposeAsync() => IsResettable ? _decoratee.CompleteAsync() : default;

        /// <inheritdoc/>
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            ReadResult readResult;

            if (IsResettable)
            {
                ThrowIfCompleted();
                try
                {
                    readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);

                    if (_consumed is SequencePosition consumed)
                    {
                        return new ReadResult(
                            readResult.Buffer.Slice(consumed),
                            readResult.IsCanceled,
                            readResult.IsCompleted);
                    }
                }
                catch
                {
                    IsResettable = false;
                    throw;
                }
            }
            else
            {
                readResult = await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!readResult.IsCanceled)
            {
                _sequence = readResult.Buffer;
            }
            return readResult;
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            bool success;

            if (IsResettable)
            {
                ThrowIfCompleted();
                try
                {
                    if (_decoratee.TryRead(out result))
                    {
                        if (_consumed is SequencePosition consumed)
                        {
                            result = new ReadResult(
                                result.Buffer.Slice(consumed),
                                result.IsCanceled,
                                result.IsCompleted);
                        }
                        // else keep result as is
                        success = true;
                    }
                    else
                    {
                        success = false;
                    }
                }
                catch
                {
                    IsResettable = false;
                    throw;
                }
            }
            else
            {
                success = _decoratee.TryRead(out result);
            }

            if (success && !result.IsCanceled)
            {
                _sequence = result.Buffer;
            }
            return success;
        }

        internal ResettablePipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;

        internal void Reset()
        {
            if (IsResettable)
            {
                // TODO: enable this check or add comment on why it's not correct
                // if (!_isReaderCompleted)
                // {
                //    throw new InvalidOperationException("cannot reset a non-completed ResettablePipeReaderDecorator");
                // }

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
