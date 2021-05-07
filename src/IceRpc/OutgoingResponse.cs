// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents a response protocol frame sent by the application.</summary>
    public sealed class OutgoingResponse : OutgoingFrame
    {
        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialBinaryContext { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <inheritdoc/>
        public override IList<ArraySegment<byte>> Payload
        {
            get => _payload;
            set
            {
                if (value.Count == 0 || value[0].Count == 0)
                {
                    throw new ArgumentException("the response payload cannot be empty");
                }

                // the payload encapsulation header is always in the payload first segment
                var istr = new InputStream(value[0], Protocol.GetEncoding());
                ReplyStatus replyStatus = istr.ReadReplyStatus();
                if (_payload.Count > 0 && replyStatus != (ReplyStatus)_payload[0][0])
                {
                    throw new ArgumentException(
                        "setting the Payload cannot change the ReplyStatus byte",
                        nameof(Payload));
                }

                // If the response frame has an encapsulation reset the payload encoding and compression format values
                if (Protocol == Protocol.Ice2 || replyStatus <= ReplyStatus.UserException)
                {
                    int _ = Protocol == Protocol.Ice1 ? istr.ReadInt() : istr.ReadSize();
                    var payloadEncoding = new Encoding(istr);
                    CompressionFormat payloadCompressionFormat = payloadEncoding == Encoding.V11 ?
                        CompressionFormat.Decompressed : istr.ReadCompressionFormat();
                    PayloadCompressionFormat = payloadCompressionFormat;
                    PayloadEncoding = payloadEncoding;
                }
                else
                {
                    PayloadEncoding = Encoding.V11;
                }
                _payload = value;
                _payloadSize = -1;
            }
        }

        /// <inheritdoc/>
        public override CompressionFormat PayloadCompressionFormat { get; private protected set; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <inheritdoc/>
        public override int PayloadSize
        {
            get
            {
                if (_payloadSize == -1)
                {
                    _payloadSize = Payload.GetByteCount();
                }
                return _payloadSize;
            }
        }

        /// <summary>The result type; see <see cref="IceRpc.ResultType"/>.</summary>
        public ResultType ResultType => Payload[0][0] == 0 ? ResultType.Success : ResultType.Failure;

        private IList<ArraySegment<byte>> _payload;
        private int _payloadSize = -1;

        /*
                /// <summary>Creates a new <see cref="OutgoingResponse"/> for an operation with a non-tuple non-struct
                /// return type.</summary>
                /// <typeparam name="T">The type of the return value.</typeparam>
                /// <param name="dispatch">The Dispatch object for the corresponding incoming request.</param>
                /// <param name="format">The format to use when writing class instances in case <c>returnValue</c> contains
                /// class instances.</param>
                /// <param name="returnValue">The return value to write into the frame.</param>
                /// <param name="writer">The <see cref="OutputStreamWriter{T}"/> that writes the return value into the frame.
                /// </param>
                /// <returns>A new OutgoingResponse.</returns>
                public static OutgoingResponse WithReturnValue<T>(
                    Dispatch dispatch,
                    FormatType format,
                    T returnValue,
                    OutputStreamWriter<T> writer)
                {
                    (OutgoingResponse response, OutputStream ostr) = PrepareReturnValue(dispatch, format);
                    writer(ostr, returnValue);
                    ostr.Finish();
                    return response;
                }

                /// <summary>Creates a new <see cref="OutgoingResponse"/> for an operation with a single stream return
                /// value.</summary>
                /// <typeparam name="T">The type of the return value.</typeparam>
                /// <param name="dispatch">The Dispatch object for the corresponding incoming request.</param>
                /// <param name="format">The format to use when writing class instances in case <c>returnValue</c> contains
                /// class instances.</param>
                /// <param name="returnValue">The return value to write into the frame.</param>
                /// <param name="writer">The delegate that will send the stream return value.</param>
                /// <returns>A new OutgoingResponse.</returns>
                [System.Diagnostics.CodeAnalysis.SuppressMessage(
                    "Microsoft.Performance",
                    "CA1801: Review unused parameters",
                    Justification = "TODO")]
                public static OutgoingResponse WithReturnValue<T>(
                    Dispatch dispatch,
                    FormatType format,
                    T returnValue,
                    Action<SocketStream, T, System.Threading.CancellationToken> writer)
                {
                    OutgoingResponse response = WithVoidReturnValue(dispatch);
                    // TODO: deal with format
                    response.StreamDataWriter = socketStream => writer(socketStream, returnValue, default);
                    return response;
                }

                /// <summary>Creates a new <see cref="OutgoingResponse"/> for an operation with a tuple or struct return
                /// type.</summary>
                /// <typeparam name="T">The type of the return value.</typeparam>
                /// <param name="dispatch">The Dispatch object for the corresponding incoming request.</param>
                /// <param name="format">The format to use when writing class instances in case <c>returnValue</c> contains
                /// class instances.</param>
                /// <param name="returnValue">The return value to write into the frame.</param>
                /// <param name="writer">The <see cref="OutputStreamWriter{T}"/> that writes the return value into the frame.
                /// </param>
                /// <returns>A new OutgoingResponse.</returns>
                public static OutgoingResponse WithReturnValue<T>(
                    Dispatch dispatch,
                    FormatType format,
                    in T returnValue,
                    OutputStreamValueWriter<T> writer)
                    where T : struct
                {
                    (OutgoingResponse response, OutputStream ostr) = PrepareReturnValue(dispatch, format);
                    writer(ostr, in returnValue);
                    ostr.Finish();
                    return response;
                }

                /// <summary>Creates a new <see cref="OutgoingResponse"/> for an operation with a tuple return
                /// type where the tuple return type contains a stream return value.</summary>
                /// <typeparam name="T">The type of the return value.</typeparam>
                /// <param name="dispatch">The Dispatch object for the corresponding incoming request.</param>
                /// <param name="format">The format to use when writing class instances in case <c>returnValue</c> contains
                /// class instances.</param>
                /// <param name="returnValue">The return value to write into the frame.</param>
                /// <param name="writer">The delegate that writes the return value into the frame.</param>
                /// <returns>A new OutgoingResponse.</returns>
                public static OutgoingResponse WithReturnValue<T>(
                    Dispatch dispatch,
                    FormatType format,
                    in T returnValue,
                    OutputStreamValueWriterWithStreamable<T> writer)
                    where T : struct
                {
                    (OutgoingResponse response, OutputStream ostr) = PrepareReturnValue(dispatch, format);
                    // TODO: deal with compress, format and cancellation token
                    response.StreamDataWriter = writer(ostr, in returnValue, default);
                    ostr.Finish();
                    return response;
                }
        */

        /// <summary>Constructs an outgoing response with a void payload. The new response will use the protocol and
        /// encoding of <paramref name="dispatch"/>.</summary>
        /// <param name="dispatch">The dispatch for the request on which this constructor creates a response.</param>
        public OutgoingResponse(Dispatch dispatch)
            : this(dispatch.IncomingRequest, dispatch.ResponseFeatures)
        {
        }

        /// <summary>Constructs an outgoing response with a void payload. The new response will use the protocol and
        /// encoding of <paramref name="request"/>.</summary>
        /// <param name="request">The request on which this constructor creates a response.</param>
        /// <param name="features">The features of this response.</param>
        public OutgoingResponse(IncomingRequest request, FeatureCollection? features = null)
            : this(request.Protocol,
                   IceRpc.Payload.FromVoidReturnValue(request),
                   features)
        {
        }

        /// <summary>Constructs an outgoing response with a payload. The new response will use the protocol
        /// of the <paramref name="dispatch"/>.</summary>
        /// <param name="dispatch">The dispatch for the request on which this constructor creates a response.</param>
        /// <param name="payload">The payload of this response.</param>
        public OutgoingResponse(Dispatch dispatch, IList<ArraySegment<byte>> payload)
            : this(dispatch.Protocol, payload, dispatch.ResponseFeatures)
        {
        }

        /// <summary>Constructs an outgoing response from the given incoming response. The new response will
        /// use the protocol of the <paramref name="dispatch"/> and the encoding of <paramref name="response"/>.
        /// </summary>
        /// <param name="dispatch">The dispatch for the request on which this constructor creates a response.</param>
        /// <param name="response">The incoming response used to construct the new outgoing response.</param>
        /// <param name="forwardBinaryContext">When true (the default), the new response uses the incoming response's
        /// binary context as a fallback - all the entries in this binary context are added before the response is sent,
        /// except for entries previously added by dispatch interceptors.</param>
        public OutgoingResponse(
            Dispatch dispatch,
            IncomingResponse response,
            bool forwardBinaryContext = true)
            : this(dispatch.IncomingRequest, response, forwardBinaryContext, dispatch.ResponseFeatures)
        {
        }

        /// <summary>Constructs an outgoing response from the given incoming response. The new response will
        /// use the protocol of the <paramref name="request"/> and the encoding of <paramref name="response"/>.
        /// </summary>
        /// <param name="request">The request on which this constructor creates a response.</param>
        /// <param name="response">The incoming response used to construct the new outgoing response.</param>
        /// <param name="forwardBinaryContext">When true (the default), the new response uses the incoming response's
        /// binary context as a fallback - all the entries in this binary context are added before the response is sent,
        /// except for entries previously added by dispatch interceptors.</param>
        /// <param name="features">The features for this response.</param>
        public OutgoingResponse(
            IncomingRequest request,
            IncomingResponse response,
            bool forwardBinaryContext = true,
            FeatureCollection? features = null)
            : this(request.Protocol, response.PayloadEncoding, features)
        {
            if (Protocol == response.Protocol)
            {
                Payload.Add(response.Payload);
                if (Protocol == Protocol.Ice2 && forwardBinaryContext)
                {
                    // Don't forward RetryPolicy context
                    InitialBinaryContext =
                        response.BinaryContext.ToImmutableDictionary().Remove((int)BinaryContextKey.RetryPolicy);
                }
            }
            else
            {
                int sizeLength = response.Protocol == Protocol.Ice1 ? 4 : response.Payload[1].ReadSizeLength20();

                // Create a small buffer to hold the result type or reply status plus the encapsulation header.
                Debug.Assert(Payload.Count == 0);
                byte[] buffer = new byte[8];
                Payload.Add(buffer);

                if (response.ResultType == ResultType.Failure && PayloadEncoding == Encoding.V11)
                {
                    // When the response carries a failure encoded with 1.1, we need to perform a small adjustment
                    // between ice1 and ice2 response frames.
                    // ice1: [failure reply status][encapsulation or special exception]
                    // ice2: [failure][encapsulation with reply status + encapsulation bytes or special exception]
                    // There is no such adjustment with other encoding, or when the response does not carry a failure.

                    if (Protocol == Protocol.Ice1)
                    {
                        Debug.Assert(response.Protocol == Protocol.Ice2);

                        // Read the reply status byte located immediately after the encapsulation header; +1
                        // corresponds to the result type and +2 corresponds to the encoding in the encapsulation
                        // header.
                        ReplyStatus replyStatus = response.Payload[1 + sizeLength + 2].AsReplyStatus();

                        var ostr = new OutputStream(Ice1Definitions.Encoding, Payload);
                        ostr.Write(replyStatus);
                        if (replyStatus == ReplyStatus.UserException)
                        {
                            // We are forwarding a user exception encoded using 1.1 and received over ice2 to ice1.
                            // The size of the new encapsulation is the size of the payload -1 byte of the ice2 result
                            // type, -1 byte of the reply status, sizeLength is not included in the encapsulation size.

                            ostr.WriteEncapsulationHeader(response.Payload.Count - 1 - sizeLength - 1, PayloadEncoding);
                        }
                        // else we are forwarding a system exception encoded using 1.1 and received over ice2 to ice1.
                        // and we include only the reply status

                        ostr.Finish();

                        // We need to get rid of the extra reply status byte and the start of the encapsulation added
                        // when 1.1 exceptions are marshaled using ice2.

                        // 1 for the result type in the response, then sizeLength + 2 to skip the encapsulation header,
                        // then + 1 to skip the reply status byte
                        Payload.Add(response.Payload.Slice(1 + sizeLength + 2 + 1));
                    }
                    else
                    {
                        Debug.Assert(response.Protocol == Protocol.Ice1);
                        var ostr = new OutputStream(Ice2Definitions.Encoding, Payload);
                        ostr.Write(ResultType.Failure);

                        var replyStatus = (ReplyStatus)response.Payload[0];
                        if (replyStatus == ReplyStatus.UserException)
                        {
                            // We are forwarding a user exception encoded using 1.1 and received over ice1 to ice2. We
                            // create an ice2 encapsulation and write the 1.1 reply status followed by the 1.1 encoded
                            // data, sizeLength is not included in the encapsulation size.

                            ostr.WriteEncapsulationHeader(response.Payload.Count - sizeLength, PayloadEncoding);
                            ostr.Write(replyStatus);
                            ostr.Finish();
                            Payload.Add(response.Payload.Slice(1 + sizeLength + 2));
                        }
                        else
                        {
                            // We are forwarding a system exception encoded using 1.1 and received over ice1 to ice2.
                            // We create an ice2 encapsulation and write the 1.1 encoded exception in it (which
                            // includes the 1.1 reply status, followed by the exception). The size of the new
                            // encapsulation is the size of the payload +2 bytes for the encapsulations encoding.

                            ostr.WriteEncapsulationHeader(response.Payload.Count + 2, PayloadEncoding);
                            ostr.Finish();
                            Payload.Add(response.Payload);
                        }
                    }
                }
                else
                {
                    // In this case we always have an encapsulation it is either an ice1 response with result type
                    // success or an ice2 response that always uses an encapsulation. We just need to rewrite the
                    // encapsulation header with the new encoding and keep the rest of the encapsulation data as is.
                    var ostr = new OutputStream(Protocol.GetEncoding(), Payload);
                    ostr.Write(response.ResultType);
                    ostr.WriteEncapsulationHeader(response.Payload.Count - 1 - sizeLength, PayloadEncoding);
                    ostr.Finish();
                    Payload.Add(response.Payload.Slice(1 + sizeLength + 2));
                }

                Debug.Assert(Payload.Count == 2);
            }
        }

        /// <summary>Constructs a response that represents a failure and contains an exception.</summary>
        /// <param name="request">The incoming request for which this constructor
        ///  creates a response.</param>
        /// <param name="exception">The exception to store into the response's payload.</param>
        /// <param name="features">The features for this response.</param>
        public OutgoingResponse(IncomingRequest request, RemoteException exception, FeatureCollection? features = null)
            : this(request.Protocol, IceRpc.Payload.FromRemoteException(request, exception), features)
        {
            if (Protocol == Protocol.Ice2 && exception.RetryPolicy.Retryable != Retryable.No)
            {
                RetryPolicy retryPolicy = exception.RetryPolicy;

                BinaryContextOverride.Add(
                    (int)BinaryContextKey.RetryPolicy,
                    ostr =>
                    {
                        ostr.Write(retryPolicy.Retryable);
                        if (retryPolicy.Retryable == Retryable.AfterDelay)
                        {
                            ostr.WriteVarUInt((uint)retryPolicy.Delay.TotalMilliseconds);
                        }
                    });
            }
        }

        /// <inheritdoc/>
        internal override IncomingFrame ToIncoming() => new IncomingResponse(this);

        /// <inheritdoc/>
        internal override void WriteHeader(OutputStream ostr)
        {
            Debug.Assert(ostr.Encoding == Protocol.GetEncoding());

            if (Protocol == Protocol.Ice2)
            {
                OutputStream.Position startPos = ostr.StartFixedLengthSize(2);
                WriteBinaryContext(ostr);
                ostr.EndFixedLengthSize(startPos, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);
                // no header
            }
        }

        private OutgoingResponse(Protocol protocol,
                                 IList<ArraySegment<byte>> payload,
                                 FeatureCollection? features)
                    : base(protocol, features)
        {
            _payload = payload;
            _payloadSize = -1;
            Payload = payload;
        }

        private OutgoingResponse(Protocol protocol, Encoding encoding, FeatureCollection? features)
            : base(protocol, features)
        {
            _payload = new List<ArraySegment<byte>>();
            _payloadSize = -1;
            PayloadEncoding = encoding;
        }
    }
}
