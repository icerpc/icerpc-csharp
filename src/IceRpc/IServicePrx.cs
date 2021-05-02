// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A delegate that reads the return value from a response payload.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="payload">The response payload.</param>
    /// <param name="proxy">The proxy used to send the request.</param>
    /// <param name="connection">The connection that received this response.</param>
    /// <returns>The response return value.</returns>
    /// <exception cref="RemoteException">Thrown when the response payload carries a failure.</exception>
    public delegate T ResponseReader<T>(ArraySegment<byte> payload, IServicePrx proxy, Connection connection);

    /// <summary>Base interface of all service proxies.</summary>
    public interface IServicePrx : IEquatable<IServicePrx>
    {
        /// <summary>Converts the arguments of each operation that takes arguments into a request payload.</summary>
        public static class Request
        {
            /// <summary>Creates the request payload for operation ice_isA.</summary>
            /// <param name="proxy">Proxy to the target service.</param>
            /// <param name="args">The type ID argument to write into the request.</param>
            /// <returns>The payload.</returns>
            public static IList<ArraySegment<byte>> IceIsA(IServicePrx proxy, string args) =>
                Payload.FromArgs(proxy, args, OutputStream.IceWriterFromString);
        }

        /// <summary>Holds an <see cref="ResponseReader{T}"/> for each non-void remote operation defined in the
        /// pseudo-interface Service.</summary>
        public static class Response
        {
            /// <summary>The <see cref="ResponseReader{T}"/> reader for the return type of operation ice_id.
            /// </summary>
            public static string IceId(ArraySegment<byte> payload, IServicePrx proxy, Connection connection) =>
                payload.ToReturnValue(InputStream.IceReaderIntoString, proxy, connection);

            /// <summary>The <see cref="ResponseReader{T}"/> reader for the return type of operation ice_ids.
            /// </summary>
            public static string[] IceIds(ArraySegment<byte> payload, IServicePrx proxy, Connection connection) =>
                payload.ToReturnValue(istr => istr.ReadArray(minElementSize: 1, InputStream.IceReaderIntoString),
                                      proxy,
                                      connection);

            /// <summary>The <see cref="ResponseReader{T}"/> reader for the return type of operation ice_isA.
            /// </summary>
            public static bool IceIsA(ArraySegment<byte> payload, IServicePrx proxy, Connection connection) =>
                payload.ToReturnValue(InputStream.IceReaderIntoBool, proxy, connection);
        }

        /// <summary>Factory for <see cref="IServicePrx"/> proxies.</summary>
        public static readonly IProxyFactory<IServicePrx> Factory = new ServicePrxFactory();

        /// <summary>An <see cref="InputStreamReader{T}"/> used to read <see cref="IServicePrx"/> proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly InputStreamReader<IServicePrx> IceReader = istr => Factory.Read(istr);

        /// <summary>An <see cref="InputStreamReader{T}"/> used to read <see cref="IServicePrx"/> nullable proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly InputStreamReader<IServicePrx?> IceReaderIntoNullable =
            istr => Factory.ReadNullable(istr);

        /// <summary>An OutputStream writer used to write <see cref="IServicePrx"/> proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly OutputStreamWriter<IServicePrx> IceWriter = (ostr, value) => ostr.WriteProxy(value);

        /// <summary>An OutputStream writer used to write <see cref="IServicePrx"/> nullable proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly OutputStreamWriter<IServicePrx?> IceWriterFromNullable =
            (ostr, value) => ostr.WriteNullableProxy(value);

        /// <summary>Gets or sets the secondary endpoints of this proxy.</summary>
        /// <value>The secondary endpoints of this proxy.</value>
        public IEnumerable<string> AltEndpoints { get; set; }

        /// <summary>Indicates whether or not this proxy caches its connection. Setting this value does not clear
        /// <see cref="Connection"/></summary>
        /// <value>True when the proxy caches its connection; otherwise, false.</value>
        public bool CacheConnection { get; set; }

        /// <summary>Gets or sets the connection of this proxy. Setting the connection does not affect the proxy
        /// endpoints (if any); in particular, set does check the new connection is compatible with these endpoints.
        /// </summary>
        /// <value>The connection for this proxy, or null if the proxy does not have a connection.</value>
        public Connection? Connection { get; set; }

        /// <summary>The context of this proxy, which will be sent with each invocation made using this proxy.
        /// </summary>
        public IReadOnlyDictionary<string, string> Context { get; set; }

        /// <summary>The encoding used to marshal request parameters.</summary>
        public Encoding Encoding { get; set; }

        /// <summary>Gets or sets the main endpoint of this proxy.</summary>
        /// <value>The main endpoint of this proxy. The empty string means this proxy has no endpoint.</value>
        public string Endpoint { get; set; }

        /// <summary>The invocation timeout of this proxy.</summary>
        public TimeSpan InvocationTimeout { get; set; }

        /// <summary>The invoker of this proxy.</summary>
        public IInvoker Invoker { get; set; }

        /// <summary>Indicates whether or not using this proxy to invoke an operation that does not return anything
        /// waits for an empty response from the target Ice object.</summary>
        /// <value>When true, invoking such an operation does not wait for the response from the target object. When
        /// false, invoking such an operation waits for the empty response from the target object, unless this behavior
        /// is overridden by metadata on the Slice operation's definition.</value>
        public bool IsOneway { get; set; }

        /// <summary>The location resolver associated with this proxy.</summary>
        public ILocationResolver? LocationResolver { get; set; }

        /// <summary>Gets the path of this proxy. This path is a percent-escaped URI path.</summary>
        public string Path { get; }

        /// <summary>Indicates whether or not this proxy prefers using an existing connection over creating a new one.
        /// When <c>true</c> the proxy will prefer reusing an active connection to any of its endpoints, otherwise
        /// endpoints are checked in order trying to get an active connection to the first endpoint, and if one doesn't
        /// exists creating a new one to the first endpoint.</summary>
        public bool PreferExistingConnection { get; set; }

        /// <summary>The Ice protocol of this proxy. Requests sent with this proxy use only this Ice protocol.</summary>
        public Protocol Protocol { get; }

        /// <summary>The class instance that implements this proxy.</summary>
        internal ServicePrx Impl { get; }

        /// <summary>Indicates whether the two proxy operands are equal.</summary>
        /// <param name="lhs">The left hand-side operand.</param>
        /// <param name="rhs">The right hand-side operand.</param>
        /// <returns><c>True</c> if the tow proxies are equal, <c>False</c> otherwise.</returns>
        public static bool Equals(IServicePrx? lhs, IServicePrx? rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null || rhs is null)
            {
                return false;
            }

            return lhs.Equals(rhs);
        }

        /// <summary>Creates a proxy from its string representation.</summary>
        /// <param name="s">The string representation of the proxy.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of a proxy.
        /// </exception>
        public static IServicePrx Parse(string s, IInvoker invoker) => Factory.Parse(s, invoker);

        /// <summary>Converts the string representation of a proxy to its <see cref="IServicePrx"/> equivalent.</summary>
        /// <param name="s">The proxy string representation.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <param name="proxy">When this method returns it contains the new proxy, if the conversion succeeded or null
        /// if the conversion failed.</param>
        /// <returns><c>true</c> if the s parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(string s, IInvoker invoker, out IServicePrx? proxy)
        {
            try
            {
                proxy = Factory.Parse(s, invoker);
            }
            catch
            {
                proxy = null;
                return false;
            }
            return true;
        }

        /// <summary>Returns the Slice type ID of the most derived interface supported by the target service of this
        /// proxy.</summary>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The Slice type ID of the most derived interface.</returns>
        public Task<string> IceIdAsync(Invocation? invocation = null, CancellationToken cancel = default) =>
            IceInvokeAsync("ice_id",
                           Payload.FromEmptyArgs(this),
                           Response.IceId,
                           invocation,
                           idempotent: true,
                           cancel: cancel);

        /// <summary>Returns the Slice type IDs of the interfaces supported by the target service of this proxy.
        /// </summary>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The Slice type IDs of the interfaces of the target service.</returns>
        public Task<string[]> IceIdsAsync(Invocation? invocation = null, CancellationToken cancel = default) =>
            IceInvokeAsync("ice_ids",
                           Payload.FromEmptyArgs(this),
                           Response.IceIds,
                           invocation,
                           idempotent: true,
                           cancel: cancel);

        /// <summary>Tests whether this service supports a specific Slice interface.</summary>
        /// <param name="id">The type ID of the Slice interface to test against.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task<bool> IceIsAAsync(string id, Invocation? invocation = null, CancellationToken cancel = default) =>
            IceInvokeAsync("ice_isA",
                           Request.IceIsA(this, id),
                           Response.IceIsA,
                           invocation,
                           idempotent: true,
                           cancel: cancel);

        /// <summary>Tests whether the target object of this proxy can be reached.</summary>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task IcePingAsync(Invocation? invocation = null, CancellationToken cancel = default) =>
            IceInvokeAsync("ice_ping",
                           Payload.FromEmptyArgs(this),
                           invocation,
                           idempotent: true,
                           oneway: IsOneway,
                           cancel: cancel);

        /// <summary>Marshals the proxy into an OutputStream.</summary>
        /// <param name="ostr">The OutputStream used to marshal the proxy.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceWrite(OutputStream ostr);

        /// <summary>Sends a request to this proxy's target service and reads the response.</summary>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="args">The payload of the request.</param>
        /// <param name="responseReader">The reader for the response payload. It reads and throws a
        /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When true, the request payload should be compressed.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The operation's return value read by response reader.</returns>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected Task<T> IceInvokeAsync<T>(
            string operation,
            IList<ArraySegment<byte>> args,
            ResponseReader<T> responseReader,
            Invocation? invocation,
            bool compress = false,
            bool idempotent = false,
            CancellationToken cancel = default)
        {
            Task<(ArraySegment<byte>, Connection)> responseTask =
                this.InvokeAsync(operation, args, invocation, compress, idempotent, oneway: false, cancel);

            return ReadResponseAsync();

            async Task<T> ReadResponseAsync()
            {
                (ArraySegment<byte> responsePayload, Connection connection) = await responseTask.ConfigureAwait(false);
                return responseReader(responsePayload, this, connection);
            }
        }

        /// <summary>Sends a request to this proxy's target service and reads the "void" response.</summary>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="args">The payload of the request.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When true, the request payload should be compressed.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is oneway and an empty response is returned immediately after
        /// sending the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A task that completes when the void response is returned. It can also hold an exception.</returns>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected Task IceInvokeAsync(
            string operation,
            IList<ArraySegment<byte>> args,
            Invocation? invocation,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            Task<(ArraySegment<byte>, Connection)> responseTask =
                this.InvokeAsync(operation, args, invocation, compress, idempotent, oneway, cancel);

            return ReadVoidResponseAsync();

            async Task ReadVoidResponseAsync()
            {
                (ArraySegment<byte> responsePayload, Connection connection) = await responseTask.ConfigureAwait(false);
                responsePayload.ToVoidReturnValue(this, connection);
            }
        }

        private class ServicePrxFactory : IProxyFactory<IServicePrx>
        {
            public IServicePrx Create(
                string path,
                Protocol protocol,
                Encoding encoding,
                Endpoint? endpoint,
                IEnumerable<Endpoint> altEndpoints,
                Connection? connection,
                ProxyOptions options) =>
                new ServicePrx(path, protocol, encoding, endpoint, altEndpoints, connection, options);

            public IServicePrx Create(
                Identity identity,
                string facet,
                Encoding encoding,
                Endpoint? endpoint,
                IEnumerable<Endpoint> altEndpoints,
                Connection? connection,
                ProxyOptions options) =>
                new ServicePrx(identity, facet, encoding, endpoint, altEndpoints, connection, options);
        }
    }
}
