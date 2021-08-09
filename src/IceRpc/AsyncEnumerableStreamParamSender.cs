// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A stream param sender that encapsulates a <see cref="IAsyncEnumerable{T}"/> and it is used to send
    /// <c>stream T</c> params using one or more <see cref="Ice2FrameType.BoundedData"/> frames.</summary>
    public sealed class AsyncEnumerableStreamParamSender<T> : IStreamParamSender
    {
        private readonly IAsyncEnumerable<T> _inputStream;
        private readonly Action<IceEncoder, T> _encodeAction;
        private readonly Encoding _encoding;
        private readonly Func<RpcStream, Task> _encoder;

        /// <summary>Constructs an async enumerable stream param sender from the given
        /// <see cref="IAsyncEnumerable{T}"/>.</summary>
        /// <param name="asyncEnumerable">The async enumerable to read the elements from.</param>
        /// <param name="encoding">The encoding used to encode the enumerable elements.</param>
        /// <param name="encodeAction">The action to encode each element.</param>
        public AsyncEnumerableStreamParamSender(
            IAsyncEnumerable<T> asyncEnumerable,
            Encoding encoding,
            Action<IceEncoder, T> encodeAction)
        {
            _inputStream = asyncEnumerable;
            _encoding = encoding;
            _encodeAction = encodeAction;
            _encoder = stream => SendAsync(stream, _inputStream, _encoding, _encodeAction);
        }

        // TODO should we pass a CancelationToken and forward it to RpcStream.SendAsync calls?
        Task IStreamParamSender.SendAsync(
            RpcStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor) =>
            _encoder(stream);

        private static async Task SendAsync(
            RpcStream rpcStream,
            IAsyncEnumerable<T> asyncEnumerable,
            Encoding encoding,
            Action<IceEncoder, T> encodeAction)
        {
            using var cancelationSource = new CancellationTokenSource();
            rpcStream.EnableSendFlowControl();
            IAsyncEnumerator<T>? asyncEnumerator = null;
            try
            {
                asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(cancelationSource.Token);
                (IceEncoder encoder, IceEncoder.Position sizeStart, IceEncoder.Position payloadStart) = StartFrame();
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
                            if (encoder.Tail != payloadStart)
                            {
                                await FinishFrameAndSendAsync(encoder, sizeStart).ConfigureAwait(false);
                            }
                            break; // End iteration
                        }
                    }
                    else
                    {
                        // If we already wrote some elements send the frame now and start a new one.
                        if (encoder.Tail != payloadStart)
                        {
                            await FinishFrameAndSendAsync(encoder, sizeStart).ConfigureAwait(false);
                            (encoder, sizeStart, payloadStart) = StartFrame();
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
                    if (encoder.Size > 32 * 1024)
                    {
                        await FinishFrameAndSendAsync(encoder, sizeStart).ConfigureAwait(false);
                        (encoder, sizeStart, payloadStart) = StartFrame();
                    }
                }
                while (true);

                // Write end of stream (TODO: this might not work with Quic)
                await rpcStream.SendAsync(
                    rpcStream.TransportHeader.Length == 0 ?
                        ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty :
                        new ReadOnlyMemory<byte>[1] { rpcStream.TransportHeader },
                    true,
                    default).ConfigureAwait(false);
            }
            catch (RpcStreamAbortedException)
            {
                cancelationSource.Cancel();
            }
            catch
            {
                rpcStream.AbortWrite(RpcStreamError.StreamingCanceledByWriter);
                throw;
            }
            finally
            {
                if (asyncEnumerator != null)
                {
                    await asyncEnumerator!.DisposeAsync().ConfigureAwait(false);
                }
            }

            (IceEncoder encoder, IceEncoder.Position sizeStart, IceEncoder.Position payloadStart) StartFrame()
            {
                var encoder = new IceEncoder(encoding);
                if (rpcStream.TransportHeader.Length > 0)
                {
                    encoder.WriteByteSpan(rpcStream.TransportHeader.Span);
                }
                encoder.EncodeByte((byte)Ice2FrameType.BoundedData);
                IceEncoder.Position sizeStart = encoder.StartFixedLengthSize();
                return (encoder, sizeStart, encoder.Tail);
            }

            async ValueTask FinishFrameAndSendAsync(IceEncoder encoder, IceEncoder.Position start)
            {
                encoder.EndFixedLengthSize(start);
                ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = encoder.Finish();
                await rpcStream.SendAsync(buffers, false, default).ConfigureAwait(false);
            }
        }
    }
}
