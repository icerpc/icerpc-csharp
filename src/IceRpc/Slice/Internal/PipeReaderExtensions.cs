// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        /// <summary>Reads/decodes a remote exception from a response payload represented by a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <param name="activator">The Slice activator.</param>
        /// <param name="classGraphMaxDepth">The class graph max depth for the decoder created by this method.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The remote exception.</returns>
        /// <remarks>The reader is always completed when this method returns.</remarks>
        internal static async ValueTask<RemoteException> ReadRemoteExceptionAsync(
            this PipeReader reader,
            IceEncoding encoding,
            Connection connection,
            IInvoker? invoker,
            IActivator activator,
            int classGraphMaxDepth,
            CancellationToken cancel)
        {
            RemoteException result;
            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(
                    encoding,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("empty remote exception");
                }

                result = Decode(readResult.Buffer);
                reader.AdvanceTo(readResult.Buffer.End);
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            // If there are any bytes in the pipe reader after the exception, we ignore them.
            await reader.CompleteAsync().ConfigureAwait(false);
            return result;

            RemoteException Decode(ReadOnlySequence<byte> buffer)
            {
                RemoteException remoteException;

                var decoder = new IceDecoder(
                    buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    classGraphMaxDepth);
                remoteException = decoder.DecodeException();

                if (remoteException is not UnknownSlicedRemoteException)
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                }
                // else, we did not decode the full exception from the buffer

                return remoteException;
            }
        }

        /// <summary>Reads a segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless IsCanceled is true.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded.</exception>
        /// <remarks>The caller must call AdvanceTo when the returned segment length is greater than 0. This method
        /// never marks the reader as completed.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(
            this PipeReader reader,
            IceEncoding encoding,
            CancellationToken cancel)
        {
            (int segmentSize, bool isCanceled, bool isCompleted) =
                await encoding.DecodeSegmentSizeAsync(reader, cancel).ConfigureAwait(false);

            if (isCanceled || segmentSize == 0)
            {
                return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled, isCompleted);
            }

            if (isCompleted)
            {
                throw new InvalidDataException($"no byte in segment with {segmentSize} bytes");
            }

            ReadResult readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return readResult;
            }

            if (readResult.Buffer.Length < segmentSize)
            {
                throw new InvalidDataException($"too few bytes in segment with {segmentSize} bytes");
            }

            return readResult.Buffer.Length == segmentSize ? readResult :
                new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
        }

        /// <summary>Reads/decodes a value from a pipe reader.</summary>
        /// <paramtype name="T">The type of the value.</paramtype>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The Slice activator.</param>
        /// <param name="classGraphMaxDepth">The class graph max depth for the decoder created by this method.</param>
        /// <param name="decodeFunc">The decode function.</param>
        /// <param name="hasStream">When true, T is or includes a stream parameter or return value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decoded value.</returns>
        /// <remarks>This method marks the reader as completed when this method throws an exception or when it succeeds
        /// and hasStream is false. When this methods returns a T with a stream, the returned stream is responsible to
        /// complete the pipe reader.</remarks>
        internal static async ValueTask<T> ReadValueAsync<T>(
            this PipeReader reader,
            IceEncoding encoding,
            Connection connection,
            IInvoker? invoker,
            IActivator activator,
            int classGraphMaxDepth,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel)
        {
            T value;

            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(
                    encoding,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                // The segment can be empty, for example args with only tagged parameters where the sender does not know
                // any tagged param or all the tagged params are null. We still decode such an empty segment to make
                // sure decodeFunc is fine with it.
                value = Decode(readResult.Buffer);

                if (!readResult.Buffer.IsEmpty)
                {
                    reader.AdvanceTo(readResult.Buffer.End);
                }
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            if (!hasStream)
            {
                // If there are actually additional bytes on the pipe reader, we ignore them since we're not expecting
                // a stream.
                await reader.CompleteAsync().ConfigureAwait(false);
            }
            return value;

            T Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new IceDecoder(
                    buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    classGraphMaxDepth);
                T value = decodeFunc(ref decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return value;
            }
        }

        /// <summary>Reads/decodes empty args or a void return value.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>The reader is always completed when this method returns.</remarks>
        internal static async ValueTask ReadVoidAsync(
            this PipeReader reader,
            IceEncoding encoding,
            CancellationToken cancel)
        {
            try
            {
                ReadResult readResult = await reader.ReadSegmentAsync(
                    encoding,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    Decode(readResult.Buffer);
                    reader.AdvanceTo(readResult.Buffer.End);
                }
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            await reader.CompleteAsync().ConfigureAwait(false);

            void Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new IceDecoder(buffer, encoding);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
            }
        }

        /// <summary>Creates an async enumerable over a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The Slice activator.</param>
        /// <param name="classGraphMaxDepth">The class graph max depth for the decoder created by this method.</param>
        /// <param name="decodeFunc">The function used to decode the streamed param.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <remarks>The implementation currently always uses segments.</remarks>
        internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this PipeReader reader,
            IceEncoding encoding,
            Connection connection,
            IInvoker? invoker,
            IActivator activator,
            int classGraphMaxDepth,
            DecodeFunc<T> decodeFunc,
            CancellationToken cancel = default)
        {
            // TODO: consider mapping incoming stream to ChannelReader<T>

            // when CancelPendingRead is called on _reader, ReadSegmentAsync returns a ReadResult with
            // IsCanceled set to true.
            _ = cancel.Register(() => reader.CancelPendingRead());

            var channel = Channel.CreateUnbounded<T>(); // TODO: switch to bounded with options

            _ = Task.Run(() => FillChannelWriterAsync(channel.Writer), cancel);

            // cancel on ReadAllAsync has attribute[EnumeratorCancellation]
            return channel.Reader.ReadAllAsync(cancel);

            async Task FillChannelWriterAsync(ChannelWriter<T> channelWriter)
            {
                while (true)
                {
                    // Each iteration decodes a segment with n values.

                    ReadResult readResult;

                    try
                    {
                        readResult = await reader.ReadSegmentAsync(encoding, cancel).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        channelWriter.Complete(ex);
                        await reader.CompleteAsync(ex).ConfigureAwait(false);
                        break; // done
                    }

                    if (readResult.IsCanceled)
                    {
                        reader.AdvanceTo(readResult.Buffer.End);
                        var ex = new OperationCanceledException();
                        channelWriter.Complete(ex);
                        await reader.CompleteAsync(ex).ConfigureAwait(false);
                        break; // done
                    }

                    if (!readResult.Buffer.IsEmpty)
                    {
                        try
                        {
                            Decode(readResult.Buffer);
                        }
                        catch (Exception ex)
                        {
                            channelWriter.Complete(ex);
                            await reader.CompleteAsync(ex).ConfigureAwait(false);
                            break;
                        }
                        finally
                        {
                            reader.AdvanceTo(readResult.Buffer.End);
                        }
                    }

                    if (readResult.IsCompleted)
                    {
                        channelWriter.Complete();
                        await reader.CompleteAsync().ConfigureAwait(false);
                        break; // done
                    }
                }

                void Decode(ReadOnlySequence<byte> buffer)
                {
                    var decoder = new IceDecoder(
                        buffer,
                        encoding,
                        connection,
                        invoker,
                        activator,
                        classGraphMaxDepth);

                    do
                    {
                        bool written = channelWriter.TryWrite(decodeFunc(ref decoder));

                        Debug.Assert(written); // TODO: deal better with this situation
                    }
                    while (decoder.Consumed < buffer.Length);
                }
            }
        }
    }
}
