// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace IceRpc
{
    /// <summary>A proxy is a local ambassador for a remote IceRPC service, and can send requests to this remote
    /// service. It can also be encoded in requests and responses. There are two forms of proxies: untyped proxies
    /// (instances of this class), and typed proxies that are Prx structs generated by the Slice compiler. Each typed
    /// proxy struct holds only a Proxy.</summary>
    /// <seealso cref="IPrx"/>
    public sealed class Proxy : IEquatable<Proxy>
    {
        /// <summary>Gets or sets the secondary endpoints of this proxy.</summary>
        /// <value>The secondary endpoints of this proxy.</value>
        public ImmutableList<Endpoint> AltEndpoints
        {
            get => _altEndpoints;

            set
            {
                if (value.Count > 0)
                {
                    if (_endpoint == null)
                    {
                        throw new ArgumentException(
                            $"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} is empty",
                            nameof(AltEndpoints));
                    }

                    if (_endpoint.Transport == TransportNames.Coloc)
                    {
                        throw new ArgumentException(
                            @$"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} uses the coloc transport",
                            nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Transport == TransportNames.Coloc))
                    {
                        throw new ArgumentException("cannot use coloc transport", nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Protocol != Protocol))
                    {
                        throw new ArgumentException($"the protocol of all endpoints must be {Protocol.GetName()}",
                                                    nameof(AltEndpoints));
                    }

                    if (Protocol == Protocol.Ice1)
                    {
                        if (_endpoint.Transport == TransportNames.Loc)
                        {
                            throw new ArgumentException(
                                @$"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} uses the loc transport",
                                nameof(AltEndpoints));
                        }

                        if (value.Any(e => e.Transport == TransportNames.Loc))
                        {
                            throw new ArgumentException("cannot use loc transport", nameof(AltEndpoints));
                        }
                    }
                }
                // else, no need to check anything, an empty list is always fine.

                _altEndpoints = value;
            }
        }

        /// <summary>Gets or sets the connection of this proxy. Setting the connection does not affect the proxy
        /// endpoints (if any); in particular, set does check the new connection is compatible with these endpoints.
        /// </summary>
        /// <value>The connection for this proxy, or null if the proxy does not have a connection.</value>
        public Connection? Connection
        {
            get => _connection;
            set => _connection = value;
        }

        /// <summary>The encoding that a caller should use when encoding request parameters when such a caller supports
        /// multiple encodings. Its value is usually the encoding of the protocol.</summary>
        public Encoding Encoding { get; set; }

        /// <summary>Gets or sets the main endpoint of this proxy.</summary>
        /// <value>The main endpoint of this proxy, or null if this proxy has no endpoint.</value>
        public Endpoint? Endpoint
        {
            get => _endpoint;

            set
            {
                if (value != null)
                {
                    if (value.Protocol != Protocol)
                    {
                        throw new ArgumentException("the new endpoint must use the proxy's protocol",
                                                    nameof(Endpoint));
                    }

                    if (_altEndpoints.Count > 0 && value.Transport == TransportNames.Coloc)
                    {
                        throw new ArgumentException(
                            "a proxy with a coloc endpoint cannot have alt endpoints", nameof(Endpoint));
                    }

                    if (Protocol == Protocol.Ice1 && _altEndpoints.Count > 0 && value.Transport == TransportNames.Loc)
                    {
                        throw new ArgumentException(
                            "an ice1 proxy with a loc endpoint cannot have alt endpoints", nameof(Endpoint));
                    }
                }
                else if (_altEndpoints.Count > 0)
                {
                    throw new ArgumentException(
                        $"cannot clear {nameof(Endpoint)} when {nameof(AltEndpoints)} is not empty",
                        nameof(Endpoint));
                }
                _endpoint = value;
            }
        }

        /// <summary>Gets or sets the invoker of this proxy.</summary>
        public IInvoker? Invoker { get; set; }

        /// <summary>Gets the path of this proxy. This path is a percent-escaped URI path.</summary>
        // private set only used in WithPath
        public string Path { get; private set; }

        /// <summary>The Ice protocol of this proxy. Requests sent with this proxy use only this Ice protocol.</summary>
        public Protocol Protocol { get; }

        /// <summary>The endpoint encoder is used when encoding ice1 endpoint (typically inside a proxy)
        /// with the Ice 1.1 encoding. We need such an encoder/decoder because the Ice 1.1 encoding of endpoints is
        /// transport-dependent.</summary>
        /// <seealso cref="Connection.EndpointCodex"/>
        // TODO: provide public API to get/set this encoder.
        internal IEndpointEncoder EndpointEncoder { get; set; } = Connection.DefaultEndpointCodex;
        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile Connection? _connection;
        private Endpoint? _endpoint;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Proxy? lhs, Proxy? rhs)
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

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Proxy? lhs, Proxy? rhs) => !(lhs == rhs);

        /// <summary>Creates a proxy from a connection and a path.</summary>
        /// <param name="connection">The connection of the new proxy. If it's a client connection, the endpoint of the
        /// new proxy is <see cref="Connection.RemoteEndpoint"/>; otherwise, the new proxy has no endpoint.</param>
        /// <param name="path">The path of the proxy.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromConnection(Connection connection, string path, IInvoker? invoker = null)
        {
            var proxy = new Proxy(path, connection.Protocol);
            proxy.Endpoint = connection.IsServer ? null : connection.RemoteEndpoint;
            proxy.Connection = connection;
            proxy.Invoker = invoker;
            return proxy;
        }

        /// <summary>Creates a proxy from a path and protocol.</summary>
        /// <param name="path">The path.</param>
        /// <param name="protocol">The protocol.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromPath(string path, Protocol protocol = Protocol.Ice2) => new(path, protocol);

        /// <summary>Creates a proxy from a string and an invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The parsed proxy.</returns>
        public static Proxy Parse(string s, IInvoker? invoker = null)
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            Proxy proxy = IceUriParser.IsProxyUri(proxyString) ?
                IceUriParser.ParseProxyUri(proxyString) : Ice1Parser.ParseProxyString(proxyString);

            proxy.Invoker = invoker;
            return proxy;
        }

        /// <summary>Tries to create a proxy from a string and invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="proxy">The parsed proxy.</param>
        /// <returns><c>true</c> when the string is parsed successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(string s, IInvoker? invoker, [NotNullWhen(true)] out Proxy? proxy)
        {
            try
            {
                proxy = Parse(s, invoker);
                return true;
            }
            catch
            {
                proxy = null;
                return false;
            }
        }

        /// <summary>Creates a shallow copy of this proxy. It's a safe copy since the only container property
        /// (AltEndpoints) is immutable.</summary>
        /// <returns>A copy of this proxy.</returns>
        public Proxy Clone() => (Proxy)MemberwiseClone();

        /// <inheritdoc/>
        public bool Equals(Proxy? other)
        {
            if (other == null)
            {
                return false;
            }
            else if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Encoding != other.Encoding)
            {
                return false;
            }
            if (_endpoint != other._endpoint)
            {
                return false;
            }

            // Only compare the connections of endpointless proxies.
            if (_endpoint == null && _connection != other._connection)
            {
                return false;
            }
            if (!_altEndpoints.SequenceEqual(other._altEndpoints))
            {
                return false;
            }
            if (Invoker != other.Invoker)
            {
                return false;
            }
            if (Path != other.Path)
            {
                return false;
            }
            if (Protocol != other.Protocol)
            {
                return false;
            }
            if (EndpointEncoder != other.EndpointEncoder)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as Proxy);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // We only hash a subset of the properties to keep GetHashCode reasonably fast.
            var hash = new HashCode();
            hash.Add(Invoker);
            hash.Add(Path);
            hash.Add(Protocol);
            if (_endpoint != null)
            {
                hash.Add(_endpoint.GetHashCode());
            }
            else if (_connection != null)
            {
                hash.Add(_connection);
            }
            return hash.ToHashCode();
        }

        /// <inherit-doc/>
        public override string ToString()
        {
            if (Protocol == Protocol.Ice1)
            {
                return InteropProxyExtensions.ToString(this, default);
            }
            else // >= ice2, use URI format
            {
                var sb = new StringBuilder();
                bool firstOption = true;

                if (_endpoint != null)
                {
                    // Use ice+transport scheme
                    sb.AppendEndpoint(_endpoint, Path);

                    firstOption = _endpoint.Protocol == Protocol.Ice2 && _endpoint.Params.Count == 0;
                }
                else
                {
                    sb.Append("ice:"); // endpointless proxy
                    sb.Append(Path);

                    // TODO: append protocol
                }

                if (Encoding != Ice2Definitions.Encoding)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("encoding=");
                    sb.Append(Encoding);
                }

                if (_altEndpoints.Count > 0)
                {
                    string mainTransport = _endpoint!.Transport;
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("alt-endpoint=");
                    for (int i = 0; i < _altEndpoints.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }
                        sb.AppendEndpoint(_altEndpoints[i], "", mainTransport != _altEndpoints[i].Transport, '$');
                    }
                }

                return sb.ToString();
            }

            static void StartQueryOption(StringBuilder sb, ref bool firstOption)
            {
                if (firstOption)
                {
                    sb.Append('?');
                    firstOption = false;
                }
                else
                {
                    sb.Append('&');
                }
            }
        }

        /// <summary>Creates a copy of this proxy with a new path.</summary>
        /// <param name="path">The new path.</param>
        /// <returns>A new proxy with the specified path.</returns>
        public Proxy WithPath(string path)
        {
            Proxy proxy = Clone();
            proxy.Path = path;

            if (Protocol == Protocol.Ice1 && proxy.Endpoint == null)
            {
                // clear cached connection of well-known proxy
                proxy.Connection = null;
            }
            return proxy;
        }

        /// <summary>Constructs a new proxy.</summary>
        /// <param name="path">The proxy path.</param>
        /// <param name="protocol">The proxy protocol.</param>
        internal Proxy(string path, Protocol protocol = Protocol.Ice2)
        {
            Protocol = protocol;
            IceUriParser.CheckPath(path, nameof(path));
            Path = path;
            Encoding = Protocol.GetEncoding();
        }
    }

    /// <summary>Provides extension methods for <see cref="Proxy"/>.</summary>
    public static class ProxyExtensions
    {
        /// <summary>The invoker that a proxy calls when its invoker is null.</summary>
        internal static IInvoker NullInvoker { get; } =
            new InlineInvoker((request, cancel) =>
                request.Connection?.InvokeAsync(request, cancel) ??
                    throw new ArgumentNullException($"{nameof(request.Connection)} is null", nameof(request)));

        /// <summary>Sends a request to a service and returns the response.</summary>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="streamParamSender">The stream param sender.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When true, the request payload should be compressed.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="returnStreamParamReceiver">When true, a stream param receiver will be returned.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response and the optional stream reader.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task<(IncomingResponse, StreamParamReceiver?)> InvokeAsync(
            this Proxy proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            IStreamParamSender? streamParamSender = null,
            Invocation? invocation = null,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            bool returnStreamParamReceiver = false,
            CancellationToken cancel = default)
        {
            proxy.Protocol.CheckSupported();

            CancellationTokenSource? timeoutSource = null;
            CancellationTokenSource? combinedSource = null;

            if (compress)
            {
                invocation ??= new Invocation();
                invocation.RequestFeatures = invocation.RequestFeatures.CompressPayload();
            }

            try
            {
                DateTime deadline = invocation?.Deadline ?? DateTime.MaxValue;
                if (deadline == DateTime.MaxValue)
                {
                    TimeSpan timeout = invocation?.Timeout ?? Timeout.InfiniteTimeSpan;
                    if (timeout != Timeout.InfiniteTimeSpan)
                    {
                        deadline = DateTime.UtcNow + timeout;

                        timeoutSource = new CancellationTokenSource(timeout);
                        if (cancel.CanBeCanceled)
                        {
                            combinedSource = CancellationTokenSource.CreateLinkedTokenSource(
                                cancel,
                                timeoutSource.Token);
                            cancel = combinedSource.Token;
                        }
                        else
                        {
                            cancel = timeoutSource.Token;
                        }
                    }
                    // else deadline remains MaxValue
                }
                else if (!cancel.CanBeCanceled)
                {
                    throw new ArgumentException(
                        $"{nameof(cancel)} must be cancelable when the invocation deadline is set",
                        nameof(cancel));
                }

                var request = new OutgoingRequest(proxy.Protocol, path: proxy.Path, operation: operation)
                {
                    AltEndpoints = proxy.AltEndpoints,
                    Connection = proxy.Connection,
                    Deadline = deadline,
                    Endpoint = proxy.Endpoint,
                    Features = invocation?.RequestFeatures ?? FeatureCollection.Empty,
                    IsOneway = oneway || (invocation?.IsOneway ?? false),
                    IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false),
                    Proxy = proxy,
                    PayloadEncoding = proxy.Encoding,
                    Payload = requestPayload,
                    StreamParamSender = streamParamSender
                };

                // We perform as much work as possible in a non async method to throw exceptions synchronously.
                Task<IncomingResponse> responseTask = (proxy.Invoker ?? NullInvoker).InvokeAsync(request, cancel);
                return ConvertResponseAsync(request, responseTask, timeoutSource, combinedSource);
            }
            catch
            {
                combinedSource?.Dispose();
                timeoutSource?.Dispose();
                throw;

                // If there is no synchronous exception, ConvertResponseAsync disposes these cancellation sources.
            }

            async Task<(IncomingResponse, StreamParamReceiver?)> ConvertResponseAsync(
                OutgoingRequest request,
                Task<IncomingResponse> responseTask,
                CancellationTokenSource? timeoutSource,
                CancellationTokenSource? combinedSource)
            {
                try
                {
                    IncomingResponse response = await responseTask.ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.ResponseFeatures = response.Features;
                    }

                    // TODO: temporary
                    _ = await response.GetPayloadAsync(cancel).ConfigureAwait(false);

                    StreamParamReceiver? streamParamReceiver = null;
                    if (returnStreamParamReceiver)
                    {
                        streamParamReceiver = new StreamParamReceiver(request.Stream, request.StreamDecompressor);
                    }
                    return (response, streamParamReceiver);
                }
                finally
                {
                    combinedSource?.Dispose();
                    timeoutSource?.Dispose();
                }
            }
        }
    }
}
