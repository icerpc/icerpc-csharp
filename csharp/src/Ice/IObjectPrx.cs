// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>A delegate that reads the response return value from a response frame.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="proxy">The proxy used to send the request.</param>
    /// <param name="response">The response frame.</param>
    /// <returns>The response return value.</returns>
    public delegate T ResponseReader<T>(IObjectPrx proxy, IncomingResponseFrame response);

    /// <summary>Base interface of all object proxies.</summary>
    public interface IObjectPrx : IEquatable<IObjectPrx>
    {
        /// <summary>Provides an <see cref="OutgoingRequestFrame"/> factory method for each remote operation defined in
        /// the pseudo-interface Object.</summary>
        public static class Request
        {
            /// <summary>Creates an <see cref="OutgoingRequestFrame"/> for operation ice_id.</summary>
            /// <param name="proxy">Proxy to the target Ice Object.</param>
            /// <param name="context">The context to write into the request.</param>
            /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
            /// <returns>A new <see cref="OutgoingRequestFrame"/>.</returns>
            public static OutgoingRequestFrame IceId(
                IObjectPrx proxy,
                IReadOnlyDictionary<string, string>? context,
                CancellationToken cancel) =>
                OutgoingRequestFrame.WithEmptyArgs(proxy, "ice_id", idempotent: true, context, cancel);

            /// <summary>Creates an <see cref="OutgoingRequestFrame"/> for operation ice_ids.</summary>
            /// <param name="proxy">Proxy to the target Ice Object.</param>
            /// <param name="context">The context to write into the request.</param>
            /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
            /// <returns>A new <see cref="OutgoingRequestFrame"/>.</returns>
            public static OutgoingRequestFrame IceIds(
                IObjectPrx proxy,
                IReadOnlyDictionary<string, string>? context,
                CancellationToken cancel) =>
                OutgoingRequestFrame.WithEmptyArgs(proxy, "ice_ids", idempotent: true, context, cancel);

            /// <summary>Creates an <see cref="OutgoingRequestFrame"/> for operation ice_isA.</summary>
            /// <param name="proxy">Proxy to the target Ice Object.</param>
            /// <param name="id">The type ID argument to write into the request.</param>
            /// <param name="context">The context to write into the request.</param>
            /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
            /// <returns>A new <see cref="OutgoingRequestFrame"/>.</returns>
            public static OutgoingRequestFrame IceIsA(
                IObjectPrx proxy,
                string id,
                IReadOnlyDictionary<string, string>? context,
                CancellationToken cancel) =>
                OutgoingRequestFrame.WithArgs(
                    proxy,
                    "ice_isA",
                    idempotent: true,
                    compress: false,
                    format: default,
                    context,
                    id,
                    OutputStream.IceWriterFromString,
                    cancel);

            /// <summary>Creates an <see cref="OutgoingRequestFrame"/> for operation ice_ping.</summary>
            /// <param name="proxy">Proxy to the target Ice Object.</param>
            /// <param name="context">The context to write into the request.</param>
            /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
            /// <returns>A new <see cref="OutgoingRequestFrame"/>.</returns>
            public static OutgoingRequestFrame IcePing(
                IObjectPrx proxy,
                IReadOnlyDictionary<string, string>? context,
                CancellationToken cancel) =>
                OutgoingRequestFrame.WithEmptyArgs(proxy, "ice_ping", idempotent: true, context, cancel);
        }

        /// <summary>Holds an <see cref="ResponseReader{T}"/> for each non-void remote operation defined in the
        /// pseudo-interface Object.</summary>
        public static class Response
        {
            /// <summary>The <see cref="ResponseReader{T}"/> reader for the return type of operation ice_id.
            /// </summary>
            public static string IceId(IObjectPrx proxy, IncomingResponseFrame response) =>
                 response.ReadReturnValue(proxy, InputStream.IceReaderIntoString);

            /// <summary>The <see cref="ResponseReader{T}"/> reader for the return type of operation ice_ids.
            /// </summary>
            public static string[] IceIds(IObjectPrx proxy, IncomingResponseFrame response) =>
                response.ReadReturnValue(
                    proxy, istr => istr.ReadArray(minElementSize: 1, InputStream.IceReaderIntoString));

            /// <summary>The <see cref="ResponseReader{T}"/> reader for the return type of operation ice_isA.
            /// </summary>
            public static bool IceIsA(IObjectPrx proxy, IncomingResponseFrame response) =>
                response.ReadReturnValue(proxy, InputStream.IceReaderIntoBool);
        }

        /// <summary>Factory for <see cref="IObjectPrx"/> proxies.</summary>
        public static readonly ProxyFactory<IObjectPrx> Factory = options => new ObjectPrx(options);

        /// <summary>An <see cref="InputStreamReader{T}"/> used to read <see cref="IObjectPrx"/> proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly InputStreamReader<IObjectPrx> IceReader = istr => istr.ReadProxy(Factory);

        /// <summary>An <see cref="InputStreamReader{T}"/> used to read <see cref="IObjectPrx"/> nullable proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly InputStreamReader<IObjectPrx?> IceReaderIntoNullable =
            istr => istr.ReadNullableProxy(Factory);

        /// <summary>An OutputStream writer used to write <see cref="IObjectPrx"/> proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly OutputStreamWriter<IObjectPrx> IceWriter = (ostr, value) => ostr.WriteProxy(value);

        /// <summary>An OutputStream writer used to write <see cref="IObjectPrx"/> nullable proxies.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly OutputStreamWriter<IObjectPrx?> IceWriterFromNullable =
            (ostr, value) => ostr.WriteNullableProxy(value);

        /// <summary>Indicates whether or not this proxy caches its connection.</summary>
        /// <value>True when the proxy caches its connection; otherwise, false.</value>
        public bool CacheConnection { get; }

        /// <summary>Returns the communicator that created this proxy.</summary>
        /// <returns>The communicator that created this proxy.</returns>
        public Communicator Communicator { get; }

        /// <summary>The context of this proxy, which will be sent with each invocation made using this proxy.
        /// </summary>
        public IReadOnlyDictionary<string, string> Context { get; }

        /// <summary>The encoding used to marshal request parameters.</summary>
        public Encoding Encoding { get; }

        /// <summary>The endpoints of this proxy. A proxy with a non-empty endpoint list is a direct proxy.</summary>
        public IReadOnlyList<Endpoint> Endpoints { get; }

        /// <summary>The facet to use on the target Ice object. The empty string corresponds to the default facet.
        /// </summary>
        public string Facet { get; }

        /// <summary>The identity of the target Ice object.</summary>
        public Identity Identity { get; }

        /// <summary>The invocation interceptors of this proxy.</summary>
        public IReadOnlyList<InvocationInterceptor> InvocationInterceptors { get; }

        /// <summary>The invocation timeout of this proxy.</summary>
        public TimeSpan InvocationTimeout { get; }

        /// <summary>Indicates whether or not this proxy is bound to a connection.</summary>
        /// <value>True when this proxy is bound to a connection. Such a proxy has no endpoint. Otherwise, false.
        /// </value>
        public bool IsFixed { get; }

        /// <summary>Indicates whether or not using this proxy to invoke an operation that does not return anything
        /// waits for an empty response from the target Ice object.</summary>
        /// <value>When true, invoking such an operation does not wait for the response from the target object. When
        /// false, invoking such an operation waits for the empty response from the target object, unless this behavior
        /// is overridden by metadata on the Slice operation's definition.</value>
        public bool IsOneway { get; }

        /// <summary>Indicates whether or not this proxy is marked relative.</summary>
        /// <value>True when this proxy is marked relative. Such a proxy has no endpoint and cannot be fixed as well.
        /// </value>
        public bool IsRelative { get; }

        /// <summary>An optional label that can be used to prevent proxies with identical endpoints to share a
        /// connection, outgoing connections between equivalent endpoints are shared for proxies with equal labels.
        /// </summary>
        public object? Label { get; }

        /// <summary>Gets the location of this proxy. Ice uses this location to find the target object.</summary>
        public IReadOnlyList<string> Location { get; }

        /// <summary>The locator associated with this proxy. This property is null when no locator is associated with
        /// this proxy.</summary>
        public ILocatorPrx? Locator { get; }

        /// <summary>The locator cache timeout of this proxy.</summary>
        public TimeSpan LocatorCacheTimeout { get; }

        /// <summary>Indicates whether or not this proxy prefers using an existing connection over creating a new one.
        /// When <c>true</c> the proxy will prefer reusing an active connection to any of its endpoints, otherwise
        /// endpoints are checked in order trying to get an active connection to the first endpoint, and if one doesn't
        /// exists creating a new one to the first endpoint.</summary>
        public bool PreferExistingConnection { get; }

        /// <summary>Indicates the proxy's preference for establishing non-secure connections.</summary>
        public NonSecure PreferNonSecure { get; }

        /// <summary>The Ice protocol of this proxy. Requests sent with this proxy use only this Ice protocol.</summary>
        public Protocol Protocol { get; }

        /// <summary>The class instance that implements this proxy.</summary>
        internal ObjectPrx Impl { get; }

        /// <summary>Indicates whether the two proxy operands are equal.</summary>
        /// <param name="lhs">The left hand-side operand.</param>
        /// <param name="rhs">The right hand-side operand.</param>
        /// <returns><c>True</c> if the tow proxies are equal, <c>False</c> otherwise.</returns>
        public static bool Equals(IObjectPrx? lhs, IObjectPrx? rhs)
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

        /// <summary>Converts the string representation of a proxy to its <see cref="IObjectPrx"/> equivalent.</summary>
        /// <param name="s">The proxy string representation.</param>
        /// <param name="communicator">The communicator for the new proxy.</param>
        /// <returns>The new proxy.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of a proxy.
        /// </exception>
        public static IObjectPrx Parse(string s, Communicator communicator) =>
            ObjectPrx.Parse(s, communicator, Factory);

        /// <summary>Converts the string representation of a proxy to its <see cref="IObjectPrx"/> equivalent.</summary>
        /// <param name="s">The proxy string representation.</param>
        /// <param name="communicator">The communicator for the new proxy.</param>
        /// <param name="proxy">When this method returns it contains the new proxy, if the conversion succeeded or null
        /// if the conversion failed.</param>
        /// <returns><c>true</c> if the s parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(string s, Communicator communicator, out IObjectPrx? proxy)
        {
            try
            {
                proxy = ObjectPrx.Parse(s, communicator, Factory);
            }
            catch
            {
                proxy = null;
                return false;
            }
            return true;
        }

        /// <summary>Returns the Slice type ID of the most-derived interface supported by the target object of this
        /// proxy.</summary>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task<string> IceIdAsync(
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) =>
            IceInvokeAsync(Request.IceId(this, context, cancel), Response.IceId, progress);

        /// <summary>Returns the Slice type IDs of the interfaces supported by the target object of this proxy.
        /// </summary>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task<string[]> IceIdsAsync(
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) =>
            IceInvokeAsync(Request.IceIds(this, context, cancel), Response.IceIds, progress);

        /// <summary>Tests whether this object supports a specific Slice interface.</summary>
        /// <param name="id">The type ID of the Slice interface to test against.</param>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task<bool> IceIsAAsync(
            string id,
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) =>
            IceInvokeAsync(Request.IceIsA(this, id, context, cancel), Response.IceIsA, progress);

        /// <summary>Tests whether the target object of this proxy can be reached.</summary>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public Task IcePingAsync(
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) =>
            IceInvokeAsync(Request.IcePing(this, context, cancel), IsOneway, progress);

        /// <summary>Marshals the proxy into an OutputStream.</summary>
        /// <param name="ostr">The OutputStream used to marshal the proxy.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceWrite(OutputStream ostr);

        /// <summary>Sends a request that returns a value and returns the result synchronously.</summary>
        /// <typeparam name="T">The operation's return type.</typeparam>
        /// <param name="request">The <see cref="OutgoingRequestFrame"/> for this invocation.</param>
        /// <param name="reader">An <see cref="ResponseReader{T}"/> for the operation's return value. Typically
        /// {IInterfaceNamePrx}.Response.{OperationName}.</param>
        /// <returns>The operation's return value.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected T IceInvoke<T>(OutgoingRequestFrame request, ResponseReader<T> reader)
        {
            try
            {
                IncomingResponseFrame response =
                    ObjectPrx.InvokeAsync(this, request, oneway: false).GetAwaiter().GetResult();
                return reader(this, response);
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>Sends a request that returns void and waits synchronously for the result.</summary>
        /// <param name="request">The <see cref="OutgoingRequestFrame"/> for this invocation.</param>
        /// <param name="oneway">When true, the request is sent as a oneway request. When false, it is sent as a
        /// twoway request.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected void IceInvoke(OutgoingRequestFrame request, bool oneway)
        {
            try
            {
                IncomingResponseFrame response = ObjectPrx.InvokeAsync(this, request, oneway).GetAwaiter().GetResult();
                if (!oneway)
                {
                    response.ReadVoidReturnValue(this);
                }
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>Sends a request that returns a value and returns the result asynchronously.</summary>
        /// <typeparam name="T">The operation's return type.</typeparam>
        /// <param name="request">The <see cref="OutgoingRequestFrame"/> for this invocation.</param>
        /// <param name="reader">An <see cref="ResponseReader{T}"/> for the operation's return value. Typically
        /// {IInterfaceNamePrx}.Response.{OperationName}.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <returns>A task that provides the return value asynchronously.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected Task<T> IceInvokeAsync<T>(
            OutgoingRequestFrame request,
            ResponseReader<T> reader,
            IProgress<bool>? progress)
        {
            Task<IncomingResponseFrame> responseTask;
            try
            {
                responseTask = ObjectPrx.InvokeAsync(this, request, oneway: false, progress);
            }
            catch
            {
                request.Dispose();
                throw;
            }

            return ReadResponseAsync();

            async Task<T> ReadResponseAsync()
            {
                try
                {
                    using IncomingResponseFrame response = await responseTask.ConfigureAwait(false);
                    return reader(this, response);
                }
                finally
                {
                    request.Dispose();
                }
            }
        }

        /// <summary>Sends a request that returns void and returns the result asynchronously.</summary>
        /// <param name="request">The <see cref="OutgoingRequestFrame"/> for this invocation.</param>
        /// <param name="oneway">When true, the request is sent as a oneway request. When false, it is sent as a
        /// twoway request.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <returns>A task that completes when the request completes.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected Task IceInvokeAsync(
            OutgoingRequestFrame request,
            bool oneway,
            IProgress<bool>? progress)
        {
            Task<IncomingResponseFrame> responseTask;
            try
            {
                responseTask = ObjectPrx.InvokeAsync(this, request, oneway, progress);
            }
            catch
            {
                request.Dispose();
                throw;
            }
            // A oneway request still need to await the dummy response to return only once the request is sent.
            return ReadResponseAsync();

            async Task ReadResponseAsync()
            {
                try
                {
                    using IncomingResponseFrame response = await responseTask.ConfigureAwait(false);
                    if (!oneway)
                    {
                        response.ReadVoidReturnValue(this);
                    }
                }
                finally
                {
                    request.Dispose();
                }
            }
        }
    }
}
