// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal static class MultiplexedStreamExtensions
    {
        /// <summary>Aborts the stream.</summary>
        /// <param name="stream">The stream to abort.</param>
        /// <param name="errorCode">The reason of the abort.</param>
        internal static void Abort(this IMultiplexedStream stream, MultiplexedStreamError errorCode)
        {
            // TODO: XXX: Aborting both read/write triggers the sending of two frames: StopSending frame for AbortRead
            // and Reset frame for AbortWrite
            stream.AbortRead((byte)errorCode);
            stream.AbortWrite((byte)errorCode);
        }

        /// <summary>Reads data from the stream until the given buffer is full.</summary>
        /// <param name="stream">The stream to read data from.</param>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is filled up with read data.</returns>
        internal static async ValueTask ReadUntilFullAsync(
            this IMultiplexedStream stream,
            Memory<byte> buffer,
            CancellationToken cancel)
        {
            // Loop until we received enough data to fully fill the given buffer.
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = await stream.ReadAsync(buffer[offset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException("unexpected end of stream");
                }
                offset += received;
            }
        }

        /// <summary>Aborts the stream read side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        internal static void AbortRead(this IMultiplexedStream stream, byte errorCode)
        {
        }

        /// <summary>Aborts the stream write side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        internal static void AbortWrite(this IMultiplexedStream stream, byte errorCode)
        {
        }

        /// <summary>Reads data from the stream.</summary>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes read.</returns>
        internal static ValueTask<int> ReadAsync(
            this IMultiplexedStream stream,
            Memory<byte> buffer,
            CancellationToken cancel)
        {
        }

        /// <summary>Writes data over the stream.</summary>
        /// <param name="buffers">The buffers containing the data to write.</param>
        /// <param name="endStream"><c>true</c> if no more data will be written over this stream, <c>false</c>
        /// otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are written.</returns>
        internal static ValueTask WriteAsync(
            this IMultiplexedStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
        }

        internal static PipeReader ToPipeReader(this IMultiplexedStream stream, CancellationToken cancel)
        {
            // TODO: in the future, multiplexed stream should provide directly the PipeReader which may or may not
            // come from a Pipe.

            // The PauseWriterThreshold appears to be a soft limit - otherwise, the stress test would hang/fail.

            // TODO: we could also use the default values, which are larger but not documented. A transport that uses
            // a Pipe internally could/should make these options configurable.
            var pipe = new Pipe(new PipeOptions(
                minimumSegmentSize: 1024,
                pauseWriterThreshold: 16 * 1024,
                resumeWriterThreshold: 8 * 1024));

            _ = FillPipeAsync();

            return pipe.Reader;

            async Task FillPipeAsync()
            {
                // This can run synchronously for a while.

                Exception? completeReason = null;
                PipeWriter writer = pipe.Writer;

                while (true)
                {
                    Memory<byte> buffer = writer.GetMemory();

                    int count;
                    try
                    {
                        count = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
                    }
                    catch (MultiplexedStreamAbortedException ex)
                    {
                        // We don't want the PipeReader to throw MultiplexedStreamAbortedException to the decoding code.

                        completeReason = (MultiplexedStreamError)ex.ErrorCode switch
                        {
                            MultiplexedStreamError.ConnectionAborted =>
                                new ConnectionLostException(ex),
                            MultiplexedStreamError.ConnectionShutdown =>
                                new OperationCanceledException("connection shutdown", ex),
                            MultiplexedStreamError.ConnectionShutdownByPeer =>
                                new ConnectionClosedException("connection shutdown by peer", ex),
                            MultiplexedStreamError.DispatchCanceled =>
                                new OperationCanceledException("dispatch canceled by peer", ex),
                            _ => ex
                        };

                        // TODO: confirm there is no need to AbortRead the stream.

                        break; // done
                    }
                    catch (OperationCanceledException ex)
                    {
                        stream.AbortRead((byte)MultiplexedStreamError.InvocationCanceled);
                        completeReason = ex;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // TODO: error code!
                        Console.WriteLine($"stream.ReadAsync failed with {ex}");
                        stream.AbortRead((byte)124);
                        completeReason = ex;
                        break;
                    }

                    if (count == 0)
                    {
                        break; // done
                    }

                    writer.Advance(count);

                    try
                    {
                        FlushResult flushResult = await writer.FlushAsync(cancel).ConfigureAwait(false);

                        // We don't expose writer to anybody, so who would call CancelPendingFlush?
                        Debug.Assert(!flushResult.IsCanceled);

                        if (flushResult.IsCompleted)
                        {
                            // reader no longer reading
                            stream.AbortRead((byte)MultiplexedStreamError.StreamingCanceledByReader);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        // TODO: error code?
                        stream.AbortRead((byte)MultiplexedStreamError.StreamingCanceledByReader);
                        completeReason = ex;
                        break;
                    }
                }

                await writer.CompleteAsync(completeReason).ConfigureAwait(false);

                // TODO: stream should be completed at this point, but there is no way to tell.
            }
        }
    }
}
