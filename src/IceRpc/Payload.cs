// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Methods to read and write the payloads of requests and responses.</summary>
    public static class Payload
    {

        // When a response frame contains an encapsulation, it always starts at position 1 of the first segment,
        // and the first segment has always at least 2 bytes.
        private static readonly OutputStream.Position _responseEncapsulationStart = new(0, 1);

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="proxy">A proxy to the target service. This method uses the protocol and encoding of the proxy
        /// to create the payload.</param>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="writer">The <see cref="OutputStreamWriter{T}"/> that writes the argument into the payload.
        /// </param>
        /// <param name="classFormat">The class format in case T is a class.</param>
        /// <returns>A new payload.</returns>
        public static IList<ArraySegment<byte>> FromSingleArg<T>(
            IServicePrx proxy,
            T arg,
            OutputStreamWriter<T> writer,
            FormatType classFormat = default)
        {
            var payload = new List<ArraySegment<byte>>();

            var ostr = new OutputStream(proxy.Protocol.GetEncoding(),
                                        payload,
                                        startAt: default,
                                        proxy.Encoding,
                                        classFormat);
            writer(ostr, arg);
            ostr.Finish();
            return payload;
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="proxy">A proxy to the target service. This method uses the protocol and encoding of the proxy
        /// to create the payload.</param>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="writer">The <see cref="OutputStreamValueWriter{T}"/> that writes the arguments into the
        /// payload.</param>
        /// <param name="classFormat">The class format in case any parameter is a class.</param>
        /// <returns>A new payload.</returns>
        public static IList<ArraySegment<byte>> FromArgs<T>(
            IServicePrx proxy,
            in T args,
            OutputStreamValueWriter<T> writer,
            FormatType classFormat = default) where T : struct
        {
            var payload = new List<ArraySegment<byte>>();

            var ostr = new OutputStream(proxy.Protocol.GetEncoding(),
                                        payload,
                                        startAt: default,
                                        proxy.Encoding,
                                        classFormat);
            writer(ostr, in args);
            ostr.Finish();
            return payload;
        }

        /// <summary>Creates the payload of a request without parameter.</summary>
        /// <param name="proxy">A proxy to the target service. This method uses the protocol and encoding of the proxy
        /// to create the payload.</param>
        /// <returns>A new payload.</returns>
        public static IList<ArraySegment<byte>> FromEmptyArgs(IServicePrx proxy) =>
            new List<ArraySegment<byte>> { proxy.Protocol.GetEmptyArgsPayload(proxy.Encoding) };

        /// <summary>Reads a remote exception from a response payload.</summary>
        /// <param name="payload">The response's payload.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <returns>The remote exception.</returns>
        public static RemoteException ToRemoteException(
            this ReadOnlyMemory<byte> payload,
            Connection connection,
            IInvoker? invoker)
        {
            if (payload.Length == 0 || (ResultType)payload.Span[0] == ResultType.Success)
            {
                throw new ArgumentException("payload does not carry a remote exception", nameof(payload));
            }

            var replyStatus = (ReplyStatus)payload.Span[0]; // can be reassigned below

            Protocol protocol = connection.Protocol;
            InputStream istr;

            if (protocol == Protocol.Ice2 || replyStatus == ReplyStatus.UserException)
            {
                istr = new InputStream(payload.Slice(1),
                                       protocol.GetEncoding(),
                                       connection,
                                       invoker,
                                       startEncapsulation: true);

                if (protocol == Protocol.Ice2 && istr.Encoding == Encoding.V11)
                {
                    replyStatus = istr.ReadReplyStatus();
                }
            }
            else
            {
                Debug.Assert(protocol == Protocol.Ice1);
                istr = new InputStream(payload.Slice(1), Encoding.V11);
            }

            RemoteException exception;
            if (istr.Encoding == Encoding.V11 && replyStatus != ReplyStatus.UserException)
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

        /// <summary>Reads a response payload and converts it into a return value or a remote exception.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="payload">The response payload.</param>
        /// <param name="reader">An input stream reader used to read the return value.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <returns>The return value.</returns>
        /// <exception cref="RemoteException">Thrown when the payload carries a failure.</exception>
        public static T ToReturnValue<T>(
            this ReadOnlyMemory<byte> payload,
            InputStreamReader<T> reader,
            Connection connection,
            IInvoker? invoker)
        {
            if (payload.Length == 0)
            {
                throw new ArgumentException("invalid empty payload", nameof(payload));
            }

            return (ResultType)payload.Span[0] == ResultType.Success ?
                payload.Slice(1).ReadEncapsulation(connection.Protocol.GetEncoding(),
                                                   reader,
                                                   connection,
                                                   invoker) :
                throw payload.ToRemoteException(connection, invoker);
        }

        /// <summary>Reads a response payload and converts it into a void return value or a remote exception.</summary>
        /// <param name="payload">The response payload.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <exception cref="RemoteException">Thrown when the payload carries a failure.</exception>
        public static void ToVoidReturnValue(
            this ReadOnlyMemory<byte> payload,
            Connection connection,
            IInvoker? invoker)
        {
            if (payload.Length == 0)
            {
                throw new ArgumentException("invalid empty payload", nameof(payload));
            }

            if ((ResultType)payload.Span[0] == ResultType.Success)
            {
                new InputStream(payload.Slice(1),
                                connection.Protocol.GetEncoding(),
                                startEncapsulation: true).CheckEndOfBuffer(skipTaggedParams: true);
            }
            else
            {
                throw payload.ToRemoteException(connection, invoker);
            }
        }

        /// <summary>Creates the payload of a response from the request's dispatch and response argument.
        /// Use this method when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="dispatch">The dispatch for the request.</param>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="writer">The <see cref="OutputStreamWriter{T}"/> that writes the argument into the payload.
        /// </param>
        /// <param name="classFormat">The class format in case T is a class.</param>
        /// <returns>A new payload.</returns>

        public static IList<ArraySegment<byte>> FromSingleReturnValue<T>(
            Dispatch dispatch,
            T returnValue,
            OutputStreamWriter<T> writer,
            FormatType classFormat = default)
        {
            var payload = new List<ArraySegment<byte>>();

            // Write result type Success or reply status OK (both have the same value, 0) followed by an encapsulation.
            byte[] buffer = new byte[256];
            buffer[0] = (byte)ResultType.Success;
            payload.Add(buffer);

            var ostr = new OutputStream(dispatch.Protocol.GetEncoding(),
                                        payload,
                                        _responseEncapsulationStart,
                                        dispatch.Encoding,
                                        classFormat);
            writer(ostr, returnValue);
            ostr.Finish();
            return payload;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and response arguments.
        /// Use this method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="dispatch">The dispatch for the request.</param>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="writer">The <see cref="OutputStreamWriter{T}"/> that writes the arguments into the payload.
        /// </param>
        /// <param name="classFormat">The class format in case T is a class.</param>
        /// <returns>A new payload.</returns>
        public static IList<ArraySegment<byte>> FromReturnValueTuple<T>(
            Dispatch dispatch,
            in T returnValueTuple,
            OutputStreamValueWriter<T> writer,
            FormatType classFormat = default) where T : struct
        {
            var payload = new List<ArraySegment<byte>>();

            // Write result type Success or reply status OK (both have the same value, 0) followed by an encapsulation.
            byte[] buffer = new byte[256];
            buffer[0] = (byte)ResultType.Success;
            payload.Add(buffer);

            var ostr = new OutputStream(dispatch.Protocol.GetEncoding(),
                                        payload,
                                        _responseEncapsulationStart,
                                        dispatch.Encoding,
                                        classFormat);
            writer(ostr, in returnValueTuple);
            ostr.Finish();
            return payload;
        }

        /// <summary>Creates a response payload from a <see cref="RemoteException"/>.</summary>
        /// <param name="request">The incoming request used to create this response payload. </param>
        /// <param name="exception">The exception.</param>
        /// <returns>A response payload containing the exception.</returns>
        public static IList<ArraySegment<byte>> FromRemoteException(IncomingRequest request, RemoteException exception)
        {
            var payload = new List<ArraySegment<byte>>();

            ReplyStatus replyStatus = ReplyStatus.UserException;
            if (request.PayloadEncoding == Encoding.V11)
            {
                replyStatus = exception switch
                {
                    ServiceNotFoundException _ => ReplyStatus.ObjectNotExistException,
                    OperationNotFoundException _ => ReplyStatus.OperationNotExistException,
                    UnhandledException _ => ReplyStatus.UnknownLocalException,
                    _ => ReplyStatus.UserException
                };
            }

            OutputStream ostr;
            if (request.Protocol == Protocol.Ice2 || replyStatus == ReplyStatus.UserException)
            {
                // Write ResultType.Failure or ReplyStatus.UserException (both have the same value, 1) followed by an
                // encapsulation.
                byte[] buffer = new byte[256];
                buffer[0] = (byte)ResultType.Failure;
                payload.Add(buffer);

                ostr = new OutputStream(request.Protocol.GetEncoding(),
                                        payload,
                                        _responseEncapsulationStart,
                                        request.PayloadEncoding,
                                        FormatType.Sliced);

                if (request.Protocol == Protocol.Ice2 && request.PayloadEncoding == Encoding.V11)
                {
                    // The first byte of the encapsulation data is the actual ReplyStatus
                    ostr.Write(replyStatus);
                }
            }
            else
            {
                Debug.Assert(request.Protocol == Protocol.Ice1 && (byte)replyStatus > (byte)ReplyStatus.UserException);
                ostr = new OutputStream(Ice1Definitions.Encoding, payload); // not an encapsulation
                ostr.Write(replyStatus);
            }

            exception.Origin = new RemoteExceptionOrigin(request.Path, request.Operation);
            if (request.PayloadEncoding == Encoding.V11)
            {
                switch (replyStatus)
                {
                    case ReplyStatus.ObjectNotExistException:
                    case ReplyStatus.OperationNotExistException:
                        if (request.Protocol == Protocol.Ice1)
                        {
                            request.Identity.IceWrite(ostr);
                        }
                        else
                        {
                            var identity = Identity.Empty;
                            try
                            {
                                identity = Identity.FromPath(request.Path);
                            }
                            catch (FormatException)
                            {
                                // ignored, i.e. we'll marshal an empty identity
                            }
                            identity.IceWrite(ostr);
                        }
                        ostr.WriteIce1Facet(request.Facet);
                        ostr.WriteString(request.Operation);
                        break;

                    case ReplyStatus.UnknownLocalException:
                        ostr.WriteString(exception.Message);
                        break;

                    default:
                        ostr.WriteException(exception);
                        break;
                }
            }
            else
            {
                ostr.WriteException(exception);
            }

            ostr.Finish();

            return payload;
        }

        /// <summary>Creates a payload representing a void return value.</summary>
        /// <param name="dispatch">The request's dispatch object. Used for the protocol and encoding.</param>
        /// <returns>A new payload.</returns>
        public static IList<ArraySegment<byte>> FromVoidReturnValue(Dispatch dispatch) =>
            FromVoidReturnValue(dispatch.IncomingRequest);

        /// <summary>Creates a payload representing a void return value.</summary>
        /// <param name="request">The request. Used for the protocol and encoding.</param>
        /// <returns>A new payload.</returns>
        public static IList<ArraySegment<byte>> FromVoidReturnValue(IncomingRequest request) =>
            new List<ArraySegment<byte>> { request.Protocol.GetVoidReturnPayload(request.PayloadEncoding) };

        /// <summary>Reads a request payload and converts it into the request arguments.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="reader">An input stream reader used to read the arguments.</param>
        /// <param name="connection">The connection the payload was received on.</param>
        /// <paramtype name="T">The type of the arguments.</paramtype>
        /// <returns>The request arguments.</returns>
        public static T ToArgs<T>(
            this ReadOnlyMemory<byte> payload,
            InputStreamReader<T> reader,
            Connection connection)
        {
            if (payload.Length == 0)
            {
                throw new ArgumentException("invalid empty payload", nameof(payload));
            }

            return payload.ReadEncapsulation(connection.Protocol.GetEncoding(),
                                             reader,
                                             connection: connection,
                                             invoker: connection.Server?.Invoker);
        }

        /// <summary>Reads the arguments from the request and makes sure this request carries no argument or only
        /// unknown tagged arguments.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="connection">The connection the payload was received on.</param>
        public static void ToEmptyArgs(this ReadOnlyMemory<byte> payload, Connection connection) =>
            new InputStream(payload,
                            connection.Protocol.GetEncoding(),
                            startEncapsulation: true).CheckEndOfBuffer(skipTaggedParams: true);

        /// <summary>Reads the contents of an encapsulation from the buffer.</summary>
        /// <typeparam name="T">The type of the contents.</typeparam>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The encoding of encapsulation header in the buffer.</param>
        /// <param name="payloadReader">The <see cref="InputStreamReader{T}"/> that reads the payload of the
        /// encapsulation using an <see cref="InputStream"/>.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <returns>The contents of the encapsulation read from the buffer.</returns>
        /// <exception name="InvalidDataException">Thrown when <c>buffer</c> is not a valid encapsulation or
        /// <c>payloadReader</c> finds invalid data.</exception>
        private static T ReadEncapsulation<T>(
            this ReadOnlyMemory<byte> buffer,
            Encoding encoding,
            InputStreamReader<T> payloadReader,
            Connection connection,
            IInvoker? invoker)
        {
            var istr = new InputStream(buffer, encoding, connection, invoker, startEncapsulation: true);
            T result = payloadReader(istr);
            istr.CheckEndOfBuffer(skipTaggedParams: true);
            return result;
        }
    }
}
