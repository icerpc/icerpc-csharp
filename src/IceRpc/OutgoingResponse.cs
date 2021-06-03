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
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialFields { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The <see cref="IceRpc.ReplyStatus"/> of this response.</summary>
        /// <value><see cref="IceRpc.ReplyStatus.OK"/> when <see cref="ResultType"/> is
        /// <see cref="IceRpc.ResultType.Success"/>; otherwise, if <see cref="PayloadEncoding"/> is 1.1, the value is
        /// stored in the response header or payload. For any other payload encoding, the value is
        /// <see cref="IceRpc.ReplyStatus.UserException"/>.</value>
        public ReplyStatus ReplyStatus { get; }

        /// <summary>The <see cref="IceRpc.ResultType"/> of this response.</summary>
        public ResultType ResultType { get; }

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

        /// <summary>Constructs an outgoing response with the given payload. The new response will use the protocol and
        /// encoding of <paramref name="request"/> and corresponds to a successful completion.</summary>
        /// <param name="request">The request for which this constructor creates a response.</param>
        /// <param name="payload">The payload of this response encoded using request.PayloadEncoding.</param>
        public OutgoingResponse(
            IncomingRequest request,
            IList<ArraySegment<byte>> payload)
            : this(request.Protocol, payload, request.PayloadEncoding, FeatureCollection.Empty)
        {
            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;
        }

        /// <summary>Constructs an outgoing response with a payload. The new response will use the protocol
        /// of the <paramref name="dispatch"/> and corresponds to a successful completion.</summary>
        /// <param name="dispatch">The dispatch for which this constructor creates a response.</param>
        /// <param name="payload">The payload of this response encoded using dispatch.Encoding.</param>
        public OutgoingResponse(Dispatch dispatch, IList<ArraySegment<byte>> payload)
            : this(dispatch.Protocol, payload, dispatch.Encoding, dispatch.ResponseFeatures)
        {
            ResultType = ResultType.Success;
            ReplyStatus = ReplyStatus.OK;
        }

        /// <summary>Constructs an outgoing response from the given incoming response. The new response will use the
        /// protocol of the <paramref name="request"/> and the encoding of <paramref name="response"/>.</summary>
        /// <param name="request">The request on which this constructor creates a response.</param>
        /// <param name="response">The incoming response used to construct the new outgoing response.</param>
        /// <param name="forwardFields">When true (the default), the new response uses the incoming response's fields as
        /// a fallback - all the field lines in this fields dictionary are added before the response is sent, except for
        /// fields previously added by middleware.</param>
        public OutgoingResponse(
            IncomingRequest request,
            IncomingResponse response,
            bool forwardFields = true)
            : base(request.Protocol, FeatureCollection.Empty)
        {
            ResultType = response.ResultType;
            ReplyStatus = response.ReplyStatus;

            PayloadEncoding = response.PayloadEncoding;
            Payload = new List<ArraySegment<byte>>();

            if (Protocol == response.Protocol)
            {
                Payload.Add(response.Payload);

                if (Protocol == Protocol.Ice2 && forwardFields)
                {
                    // Don't forward RetryPolicy
                    InitialFields = response.Fields.ToImmutableDictionary().Remove((int)Ice2FieldKey.RetryPolicy);
                }
            }
            else
            {
                if (response.ResultType == ResultType.Failure && PayloadEncoding == Encoding.V11)
                {
                    // When the response carries a failure encoded with 1.1, we need to perform a small adjustment
                    // between ice1 and ice2 response frames.
                    // ice1: [failure reply status][payload size, encoding and bytes|special exception]
                    // ice2: [failure result type][payload encoding][payload size][reply status][payload bytes|
                    //                                                                          special exception bytes]
                    // There is no such adjustment with other encoding, or when the response does not carry a failure.

                    if (Protocol == Protocol.Ice1)
                    {
                        Debug.Assert(response.Protocol == Protocol.Ice2);

                        // We slice-off the reply status that is part of the ice2 payload.
                        Payload.Add(response.Payload.Slice(1));
                    }
                    else
                    {
                        Debug.Assert(Protocol == Protocol.Ice2);
                        Debug.Assert(response.Protocol == Protocol.Ice1);

                        // Prepend a little buffer in front of the ice2 response payload to hold the reply status
                        byte[] buffer = new byte[1];
                        buffer[0] = (byte)ReplyStatus;
                        Payload.Add(buffer);
                        Payload.Add(response.Payload);
                    }
                }
                else
                {
                    Payload.Add(response.Payload);
                }
            }
        }

        /// <summary>Constructs a response that represents a failure and contains an exception.</summary>
        /// <param name="request">The incoming request for which this constructor
        ///  creates a response.</param>
        /// <param name="exception">The exception to store into the response's payload.</param>
        public OutgoingResponse(IncomingRequest request, RemoteException exception)
            : base(request.Protocol, exception.Features)
        {
            ResultType = ResultType.Failure;
            PayloadEncoding = request.PayloadEncoding;

            (Payload, ReplyStatus) = IceRpc.Payload.FromRemoteException(request, exception);

            if (Protocol == Protocol.Ice2 && exception.RetryPolicy.Retryable != Retryable.No)
            {
                RetryPolicy retryPolicy = exception.RetryPolicy;

                FieldsOverride.Add(
                    (int)Ice2FieldKey.RetryPolicy,
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
                WriteFields(ostr);
                ostr.Write(ResultType);
                PayloadEncoding.IceWrite(ostr);
                ostr.WriteSize(PayloadSize);
                ostr.EndFixedLengthSize(startPos, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);

                ostr.Write(ReplyStatus);
                if (ReplyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(encapsulationSize: PayloadSize + 6, PayloadEncoding);
                    responseHeader.IceWrite(ostr);
                }
            }
        }

        private OutgoingResponse(
            Protocol protocol,
            IList<ArraySegment<byte>> payload,
            Encoding payloadEncoding,
            FeatureCollection features)
            : base(protocol, features)
        {
            PayloadEncoding = payloadEncoding;
            Payload = payload;
        }
    }
}
