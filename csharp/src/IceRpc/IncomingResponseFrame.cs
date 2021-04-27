// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponseFrame : IncomingFrame, IDisposable
    {
        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> BinaryContext { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response frame.</summary>
        public ResultType ResultType => Payload[0] == 0 ? ResultType.Success : ResultType.Failure;

        // The optional socket stream. The stream is non-null if there's still data to read over the stream
        // after the reading of the response frame.
        internal SocketStream? SocketStream { get; set; }

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol that received this frame.</param>
        /// <param name="data">The frame data as an array segment.</param>
        /// <param name="maxSize">The maximum payload size, checked during decompress.</param>
        public IncomingResponseFrame(Protocol protocol, ArraySegment<byte> data, int maxSize)
            : this(protocol, data, maxSize, null)
        {
        }

        /// <summary>Releases resources used by the response frame.</summary>
        public void Dispose() => SocketStream?.Release();

        /// <summary>Reads the return value. If this response frame carries a failure, reads and throws this exception.
        /// </summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="proxy">The proxy used to send the request. <c>proxy</c> is used to read relative proxies.
        /// </param>
        /// <param name="reader">An input stream reader used to read the frame return value, when the frame
        /// return value contain multiple values the reader must use a tuple to return the values.</param>
        /// <returns>The frame return value.</returns>
        public T ReadReturnValue<T>(IServicePrx proxy, InputStreamReader<T> reader)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (SocketStream != null)
            {
                throw new InvalidDataException("stream data available for operation without stream parameter");
            }

            return ResultType == ResultType.Success ?
                Payload.AsReadOnlyMemory(1).ReadEncapsulation(Protocol.GetEncoding(),
                                                              reader,
                                                              Connection,
                                                              proxy.GetOptions()) :
                throw ReadException(proxy);
        }

        /// <summary>Reads the return value which contains a stream return value. If this response frame carries a
        /// failure, reads and throws this exception.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="proxy">The proxy used to send the request. <c>proxy</c> is used to read relative proxies.
        /// </param>
        /// <param name="reader">A reader used to read the frame return value, when the frame return value contain
        /// multiple values the reader must use a tuple to return the values.</param>
        /// <returns>The frame return value.</returns>
        public T ReadReturnValue<T>(IServicePrx proxy, InputStreamReaderWithStreamable<T> reader)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (ResultType == ResultType.Success)
            {
                if (SocketStream == null)
                {
                    throw new InvalidDataException("no stream data available for operation with stream parameter");
                }

                var istr = new InputStream(Payload.AsReadOnlyMemory(1),
                                           Protocol.GetEncoding(),
                                           Connection,
                                           proxy.GetOptions(),
                                           startEncapsulation: true);
                T value = reader(istr, SocketStream);
                // Clear the socket stream to ensure it's not disposed with the response frame. It's now the
                // responsibility of the stream parameter object to dispose the socket stream.
                SocketStream = null;
                istr.CheckEndOfBuffer(skipTaggedParams: true);
                return value;
            }
            else
            {
                if (SocketStream != null)
                {
                    throw new InvalidDataException("stream data available with remote exception result");
                }
                throw ReadException(proxy);
            }
        }

        /// <summary>Reads the return value which is a stream return value only. If this response frame carries a
        /// failure, reads and throws this exception.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="proxy">The proxy used to send the request. <c>proxy</c> is used to read relative proxies.
        /// </param>
        /// <param name="reader">A reader used to read the frame return value.</param>
        /// <returns>The frame return value.</returns>
        public T ReadReturnValue<T>(IServicePrx proxy, Func<SocketStream, T> reader)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (ResultType == ResultType.Success)
            {
                if (SocketStream == null)
                {
                    throw new InvalidDataException("no stream data available for operation with stream parameter");
                }
                Payload.AsReadOnlyMemory(1).ReadEmptyEncapsulation(Protocol.GetEncoding());
                T value = reader(SocketStream);
                // Clear the socket stream to ensure it's not disposed with the response frame. It's now the
                // responsibility of the stream parameter object to dispose the socket stream.
                SocketStream = null;
                return value;
            }
            else
            {
                if (SocketStream != null)
                {
                    throw new InvalidDataException("stream data available with remote exception result");
                }
                throw ReadException(proxy);
            }
        }

        /// <summary>Reads the return value and makes sure this return value is empty (void) or has only unknown tagged
        /// members. If this response frame carries a failure, reads and throws this exception.</summary>
        /// <param name="proxy">The proxy used to send the request. <c>proxy</c> is used to read relative proxies.
        /// </param>
        public void ReadVoidReturnValue(IServicePrx proxy)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (SocketStream != null)
            {
                throw new InvalidDataException("stream data available for operation without stream parameter");
            }

            if (ResultType == ResultType.Success)
            {
                Payload.AsReadOnlyMemory(1).ReadEmptyEncapsulation(Protocol.GetEncoding());
            }
            else
            {
                throw ReadException(proxy);
            }
        }

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of this response</param>
        /// <param name="data">The frame data as an array segment.</param>
        /// <param name="maxSize">The maximum payload size, checked during decompress.</param>
        /// <param name="socketStream">The optional socket stream. The stream is non-null if there's still data to
        /// read on the stream after the reading the response frame.</param>
        internal IncomingResponseFrame(
            Protocol protocol,
            ArraySegment<byte> data,
            int maxSize,
            SocketStream? socketStream)
            : base(protocol, maxSize)
        {
            SocketStream = socketStream;

            var istr = new InputStream(data, Protocol.GetEncoding());

            bool hasEncapsulation = false;
            if (Protocol == Protocol.Ice1)
            {
                Payload = data; // there is no response frame header with ice1

                if ((byte)istr.ReadReplyStatus() <= (byte)ReplyStatus.UserException)
                {
                    hasEncapsulation = true;
                }
                else
                {
                    PayloadEncoding = Encoding.V11;
                }
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice2);
                int headerSize = istr.ReadSize();
                int startPos = istr.Pos;
                BinaryContext = istr.ReadBinaryContext();
                if (istr.Pos - startPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid response header: expected {headerSize} bytes but read {istr.Pos - startPos
                        } bytes");
                }

                Payload = data.Slice(istr.Pos);
                _ = istr.ReadResultType(); // just to check the value
                hasEncapsulation = true;
            }

            if (hasEncapsulation)
            {
                // Read encapsulation header, in particular the payload encoding.

                PayloadEncoding = istr.ReadEncapsulationHeader(checkFullBuffer: true).Encoding;

                if (PayloadEncoding == Encoding.V20)
                {
                    PayloadCompressionFormat = istr.ReadCompressionFormat();
                }
            }
        }

        /// <summary>Constructs an incoming response frame from an outgoing response frame. Used for colocated calls.
        /// </summary>
        /// <param name="protocol">The protocol of this frame.</param>
        /// <param name="response">The outgoing response frame.</param>
        internal IncomingResponseFrame(Protocol protocol, OutgoingResponseFrame response)
            : base(protocol, int.MaxValue)
        {
            if (Protocol == Protocol.Ice2)
            {
                BinaryContext = response.GetBinaryContext();
            }

            PayloadEncoding = response.PayloadEncoding;
            PayloadCompressionFormat = response.PayloadCompressionFormat;
            Payload = response.Payload.AsArraySegment();
        }

        // Constructor for oneway response pseudo frame.
        internal IncomingResponseFrame(Connection connection, Encoding encoding)
            : base(connection.Protocol, int.MaxValue)
        {
            Connection = connection;
            PayloadEncoding = encoding;
            Payload = Protocol.GetVoidReturnPayload(encoding);
        }

        internal RetryPolicy GetRetryPolicy(ServicePrx proxy)
        {
            RetryPolicy retryPolicy = RetryPolicy.NoRetry;
            if (PayloadEncoding == Encoding.V11)
            {
                retryPolicy = Ice1Definitions.GetRetryPolicy(this, proxy);
            }
            else if (BinaryContext.TryGetValue((int)BinaryContextKey.RetryPolicy, out ReadOnlyMemory<byte> value))
            {
                retryPolicy = value.Read(istr => new RetryPolicy(istr));
            }
            return retryPolicy;
        }

        private Exception ReadException(IServicePrx proxy)
        {
            Debug.Assert(ResultType != ResultType.Success);

            var replyStatus = (ReplyStatus)Payload[0]; // can be reassigned below

            InputStream istr;

            if (Protocol == Protocol.Ice2 || replyStatus == ReplyStatus.UserException)
            {
                istr = new InputStream(Payload.Slice(1),
                                       Protocol.GetEncoding(),
                                       Connection,
                                       proxy.GetOptions(),
                                       startEncapsulation: true);

                if (Protocol == Protocol.Ice2 && PayloadEncoding == Encoding.V11)
                {
                    replyStatus = istr.ReadReplyStatus();
                }
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1 && PayloadEncoding == Encoding.V11);
                istr = new InputStream(Payload.Slice(1), Encoding.V11);
            }

            Exception exception;
            if (PayloadEncoding == Encoding.V11 && replyStatus != ReplyStatus.UserException)
            {
                exception = istr.ReadIce1SystemException(replyStatus);
                istr.CheckEndOfBuffer(skipTaggedParams: false);
            }
            else
            {
                exception = istr.ReadException();
                istr.CheckEndOfBuffer(skipTaggedParams: true);
            }
            return exception;
        }
    }
}
