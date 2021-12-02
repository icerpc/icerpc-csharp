// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using IceRpc.Transports;

namespace IceRpc.Slice
{
    /// <summary>A stream parameter sender that encapsulates an<see cref="IAsyncEnumerable{T}"/> used to send a
    /// <c> stream T</c> parameter.</summary>
    public sealed class AsyncEnumerableStreamParamSender<T> : IStreamParamSender
    {
        private readonly IAsyncEnumerable<T> _inputStream;
        private readonly Action<IceEncoder, T> _encodeAction;
        private readonly IceEncoding _encoding;
        private readonly Func<IMultiplexedStream, Task> _encoder;

        /// <summary>Constructs an async enumerable stream parameter sender from the given
        /// <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <param name="asyncEnumerable">The async enumerable to read the elements from.</param>
        /// <param name="encoding">The encoding used to encode the enumerable elements.</param>
        /// <param name="encodeAction">The action to encode each element.</param>
        public AsyncEnumerableStreamParamSender(
            IAsyncEnumerable<T> asyncEnumerable,
            IceEncoding encoding,
            Action<IceEncoder, T> encodeAction)
        {
            _inputStream = asyncEnumerable;
            _encoding = encoding;
            _encodeAction = encodeAction;
            _encoder = stream => SendAsync(stream, _inputStream, _encoding, _encodeAction);
        }

        Task IStreamParamSender.SendAsync(IMultiplexedStream stream) => _encoder(stream);

        private static async Task SendAsync(
            IMultiplexedStream multiplexedStream,
            IAsyncEnumerable<T> asyncEnumerable,
            IceEncoding encoding,
            Action<IceEncoder, T> encodeAction)
        {
            using var cancelationSource = new CancellationTokenSource();
            IAsyncEnumerator<T>? asyncEnumerator = null;
            try
            {
                asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(cancelationSource.Token);
                (IceEncoder encoder, BufferWriter.Position sizeStart, BufferWriter.Position payloadStart) = StartSegment();
                do
                {
                    ValueTask<bool> moveNext = asyncEnumerator.MoveNextAsync();
                    if (moveNext.IsCompletedSuccessfully)
                    {
                        if (moveNext.Result)
                        {
                            encodeAction(encoder, asyncEnumerator.Current);
                        }
                        else
                        {
                            if (encoder.BufferWriter.Tail != payloadStart)
                            {
                                await FinishSegmentAndSendAsync(encoder, sizeStart).ConfigureAwait(false);
                            }
                            break; // End iteration
                        }
                    }
                    else
                    {
                        // If we already wrote some elements send the frame now and start a new one.
                        if (encoder.BufferWriter.Tail != payloadStart)
                        {
                            await FinishSegmentAndSendAsync(encoder, sizeStart).ConfigureAwait(false);
                            (encoder, sizeStart, payloadStart) = StartSegment();
                        }

                        if (await moveNext.ConfigureAwait(false))
                        {
                            encodeAction(encoder, asyncEnumerator.Current);
                        }
                        else
                        {
                            break; // End iteration
                        }
                    }

                    // TODO allow to configure the size limit?
                    if (encoder.BufferWriter.Size > 32 * 1024)
                    {
                        await FinishSegmentAndSendAsync(encoder, sizeStart).ConfigureAwait(false);
                        (encoder, sizeStart, payloadStart) = StartSegment();
                    }
                }
                while (true);

                // Write end of stream (TODO: this might not work with Quic)
                await multiplexedStream.WriteAsync(
                    ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty,
                    true,
                    default).ConfigureAwait(false);
            }
            catch (MultiplexedStreamAbortedException)
            {
                cancelationSource.Cancel();
            }
            catch
            {
                multiplexedStream.AbortWrite((byte)MultiplexedStreamError.StreamingCanceledByWriter);
                throw;
            }
            finally
            {
                if (asyncEnumerator != null)
                {
                    await asyncEnumerator!.DisposeAsync().ConfigureAwait(false);
                }
            }

            (IceEncoder encoder, BufferWriter.Position sizeStart, BufferWriter.Position payloadStart) StartSegment()
            {
                var bufferWriter = new BufferWriter();
                IceEncoder encoder = encoding.CreateIceEncoder(bufferWriter);
                BufferWriter.Position sizeStart = encoder.StartFixedLengthSize();
                return (encoder, sizeStart, encoder.BufferWriter.Tail);
            }

            async ValueTask FinishSegmentAndSendAsync(IceEncoder encoder, BufferWriter.Position start)
            {
                encoder.EndFixedLengthSize(start);
                ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = encoder.BufferWriter.Finish();
                await multiplexedStream.WriteAsync(buffers, false, default).ConfigureAwait(false);
            }
        }
    }
}
