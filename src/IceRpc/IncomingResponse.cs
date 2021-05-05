// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame received by the application.</summary>
    public sealed class IncomingResponse : IncomingFrame, IDisposable
    {
        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> BinaryContext { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <inheritdoc/>
        public override ArraySegment<byte> Payload
        {
            get => _payload;
            set
            {
                var istr = new InputStream(value, Protocol.GetEncoding());
                ReplyStatus replyStatus = istr.ReadReplyStatus();

                // If the response frame has an encapsulation reset the payload encoding and compression format values
                if (Protocol == Protocol.Ice2 || replyStatus <= ReplyStatus.UserException)
                {
                    (int _, Encoding payloadEncoding) = istr.ReadEncapsulationHeader(checkFullBuffer: true);
                    PayloadCompressionFormat = payloadEncoding == Encoding.V11 ?
                        CompressionFormat.Decompressed : istr.ReadCompressionFormat();
                    PayloadEncoding = payloadEncoding;
                }
                else
                {
                    PayloadEncoding = Encoding.V11;
                }
                _payload = value;
            }
        }

        /// <inheritdoc/>
        public override CompressionFormat PayloadCompressionFormat { get; private protected set; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response frame.</summary>
        public ResultType ResultType => Payload[0] == 0 ? ResultType.Success : ResultType.Failure;

        // The optional socket stream. The stream is non-null if there's still data to read over the stream
        // after the reading of the response frame.
        internal SocketStream? SocketStream { get; set; }

        private ArraySegment<byte> _payload;

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of the response.</param>
        /// <param name="data">The frame data as an array segment.</param>
        public IncomingResponse(Protocol protocol, ArraySegment<byte> data)
            : this(protocol, data, null)
        {
        }

        /// <summary>Releases resources used by the response frame.</summary>
        public void Dispose() => SocketStream?.Release();

        /*
        /// <summary>Reads the return value which contains a stream return value. If this response frame carries a
        /// failure, reads and throws this exception.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="proxy">The proxy used to send the request.</param>
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
        /// <param name="proxy">The proxy used to send the request.</param>
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
        */

        /// <summary>Constructs an incoming response frame.</summary>
        /// <param name="protocol">The protocol of this response</param>
        /// <param name="data">The frame data as an array segment.</param>
        /// <param name="socketStream">The optional socket stream. The stream is non-null if there's still data to
        /// read on the stream after the reading the response frame.</param>
        internal IncomingResponse(
            Protocol protocol,
            ArraySegment<byte> data,
            SocketStream? socketStream)
            : base(protocol)
        {
            SocketStream = socketStream;

            var istr = new InputStream(data, Protocol.GetEncoding());
            if (Protocol == Protocol.Ice1)
            {
                Payload = data; // there is no response frame header with ice1
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
            }
        }

        /// <summary>Constructs an incoming response frame from an outgoing response frame. Used for colocated calls.
        /// </summary>
        /// <param name="response">The outgoing response frame.</param>
        internal IncomingResponse(OutgoingResponse response)
            : base(response.Protocol)
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
        internal IncomingResponse(Connection connection, Encoding encoding)
            : base(connection.Protocol)
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
    }
}
