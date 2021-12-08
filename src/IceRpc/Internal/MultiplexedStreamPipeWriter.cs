// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a multiplexed stream. This partial implementation should be good enough
    /// for PipeReader.CopyAsync.</summary>
    internal class MultiplexedStreamPipeWriter : PipeWriter
    {
        public override bool CanGetUnflushedBytes => false; // doesn't support unflushed bytes at all

        private bool _isReaderCompleted;
        private readonly IMultiplexedStream _stream;

        public override void Advance(int bytes) => throw new NotImplementedException();

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception)
        {
            #pragma warning disable CA2012
            // TODO: not very nice - can we do better? Called by the default PipeWriter.AsStream implementation.
            if (CompleteAsync(exception) is ValueTask valueTask && !valueTask.IsCompleted)
            {
                valueTask.AsTask().GetAwaiter().GetResult();
            }
            #pragma warning restore CA2012
        }

        public override async ValueTask CompleteAsync(Exception? exception = default)
        {
            if (exception == null)
            {
                if (!_isReaderCompleted)
                {
                    try
                    {
                        await _stream.WriteAsync(
                            ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty,
                            endStream: true,
                            default).ConfigureAwait(false);
                    }
                    catch (MultiplexedStreamAbortedException)
                    {
                        // See WriteASync
                        _isReaderCompleted = true;
                    }
                }
                // else no-op
            }
            else
            {
                // TODO: error code for other exceptions?
                byte errorCode = exception is MultiplexedStreamAbortedException multiplexedException ?
                    multiplexedException.ErrorCode : (byte)0;

                _stream.AbortWrite(errorCode);
            }
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
            new(new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted));

        public override Memory<byte> GetMemory(int sizeHint) => throw new NotImplementedException();
        public override Span<byte> GetSpan(int sizeHint) => throw new NotImplementedException();

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            if (!_isReaderCompleted)
            {
                try
                {
                    await _stream.WriteAsync(
                        new ReadOnlyMemory<byte>[] { source },
                        endStream: false,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (MultiplexedStreamAbortedException)
                {
                    // TODO: confirm this is indeed correct
                    _isReaderCompleted = true;
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
        }

        internal MultiplexedStreamPipeWriter(IMultiplexedStream stream) => _stream = stream;

    }
}
