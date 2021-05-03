// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Helpers methods to read and write the payloads of requests and responses.</summary>
    public static class Payload
    {
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
        /// <param name="proxy">The proxy that sent the request.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <returns>The remote exception.</returns>
        public static RemoteException ToRemoteException(
            this ReadOnlyMemory<byte> payload,
            IServicePrx proxy,
            Connection connection)
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
                                       proxy.GetOptions(),
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
        /// <param name="proxy">The proxy that sent the request.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <returns>The return value.</returns>
        /// <exception cref="RemoteException">Thrown when the payload carries a failure.</exception>
        public static T ToReturnValue<T>(
            this ReadOnlyMemory<byte> payload,
            InputStreamReader<T> reader,
            IServicePrx proxy,
            Connection connection)
        {
            if (payload.Length == 0)
            {
                throw new ArgumentException("invalid empty payload", nameof(payload));
            }

            return (ResultType)payload.Span[0] == ResultType.Success ?
                payload.Slice(1).ReadEncapsulation(connection.Protocol.GetEncoding(),
                                                   reader,
                                                   connection,
                                                   proxy.GetOptions()) :
                throw payload.ToRemoteException(proxy, connection);
        }

        /// <summary>Reads a response payload and converts it into a void return value or a remote exception.</summary>
        /// <param name="payload">The response payload.</param>
        /// <param name="proxy">The proxy that sent the request.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <exception cref="RemoteException">Thrown when the payload carries a failure.</exception>
        public static void ToVoidReturnValue(
            this ReadOnlyMemory<byte> payload,
            IServicePrx proxy,
            Connection connection)
        {
            if (payload.Length == 0)
            {
                throw new ArgumentException("invalid empty payload", nameof(payload));
            }

            if ((ResultType)payload.Span[0] == ResultType.Success)
            {
                payload.Slice(1).ReadEmptyEncapsulation(connection.Protocol.GetEncoding());
            }
            else
            {
                throw payload.ToRemoteException(proxy, connection);
            }
        }
    }
}
