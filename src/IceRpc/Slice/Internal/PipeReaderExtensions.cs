// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

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
            ClassGraphMaxDepth? classGraphMaxDepth,
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

                var decoder = new IceDecoder(
                    readResult.Buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    classGraphMaxDepth);
                result = decoder.DecodeException();

                if (result is not UnknownSlicedRemoteException)
                {
                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                }
                // else, we did not decode the full exception from the buffer

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
            ClassGraphMaxDepth? classGraphMaxDepth,
            DecodeFunc<IceDecoder, T> decodeFunc,
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

                var decoder = new IceDecoder(
                    readResult.Buffer,
                    encoding,
                    connection,
                    invoker,
                    activator,
                    classGraphMaxDepth);
                value = decodeFunc(decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);

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
                    var decoder = new IceDecoder(readResult.Buffer, encoding);
                    decoder.CheckEndOfBuffer(skipTaggedParams: true);
                    reader.AdvanceTo(readResult.Buffer.End);
                }
            }
            catch (Exception ex)
            {
                await reader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            await reader.CompleteAsync().ConfigureAwait(false);
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
        internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this PipeReader reader,
            IceEncoding encoding,
            Connection connection,
            IInvoker? invoker,
            IActivator activator,
            ClassGraphMaxDepth? classGraphMaxDepth,
            Func<IceDecoder, T> decodeFunc,
            [EnumeratorCancellation] CancellationToken cancel = default)
        {
            // when CancelPendingRead is called on _reader, ReadSegmentAsync returns a ReadResult with
            // IsCanceled set to true.
            _ = cancel.Register(() => reader.CancelPendingRead());

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
                    await reader.CompleteAsync(ex).ConfigureAwait(false);
                    yield break; // done
                }

                if (readResult.IsCanceled)
                {
                    await reader.CompleteAsync(new OperationCanceledException()).ConfigureAwait(false);
                    yield break; // done
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    // TODO: it would be nice to reuse the same decoder for all iterations
                    var decoder = new IceDecoder(
                        readResult.Buffer,
                        encoding,
                        connection,
                        invoker,
                        activator,
                        classGraphMaxDepth);

                    T value = default!;
                    do
                    {
                        try
                        {
                            value = decodeFunc(decoder);
                        }
                        catch (Exception ex)
                        {
                            await reader.CompleteAsync(ex).ConfigureAwait(false);
                            yield break; // done
                        }

                        yield return value;
                    }
                    while (decoder.Pos < readResult.Buffer.Length);
                }

                if (readResult.IsCompleted)
                {
                    await reader.CompleteAsync().ConfigureAwait(false);
                    yield break; // done
                }
            }
        }
    }
}
