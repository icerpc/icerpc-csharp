// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
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

                    if (_endpoint.Transport == Transport.Loc || _endpoint.Transport == Transport.Coloc)
                    {
                        throw new ArgumentException(
                            @$"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)
                            } uses the loc or coloc transports",
                            nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Transport == Transport.Loc || e.Transport == Transport.Coloc))
                    {
                        throw new ArgumentException("cannot use loc or coloc transport", nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Protocol != Protocol))
                    {
                        throw new ArgumentException($"the protocol of all endpoints must be {Protocol.GetName()}",
                                                    nameof(AltEndpoints));
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

        /// <summary>The encoding used to marshal request parameters.</summary>
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
                    if (_altEndpoints.Count > 0 &&
                        (value.Transport == Transport.Loc || value.Transport == Transport.Coloc))
                    {
                        throw new ArgumentException(
                            "a proxy with a loc or coloc endpoint cannot have alt endpoints", nameof(Endpoint));
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
        public string Path { get; }

        /// <summary>The Ice protocol of this proxy. Requests sent with this proxy use only this Ice protocol.</summary>
        public Protocol Protocol { get; }

        /// <summary>The facet of this proxy. Used only with the ice1 protocol.</summary>
        internal string Facet
        {
            get => FacetPath.Count == 0 ? "" : FacetPath[0];
            set => FacetPath = value.Length > 0 ? ImmutableList.Create(value) : ImmutableList<string>.Empty;
        }

        /// <summary>The identity of this proxy. Used only with the ice1 protocol.</summary>
        internal Identity Identity
        {
            get => _identity;
            set
            {
                Debug.Assert(Protocol == Protocol.Ice1 || value == Identity.Empty);
                if (Protocol == Protocol.Ice1 && value.Name.Length == 0)
                {
                    throw new ArgumentException("identity name of ice1 service cannot be empty",
                                                nameof(Identity));
                }
                _identity = value;
            }
        }

        internal bool IsIndirect => _endpoint?.Transport == Transport.Loc || IsWellKnown;
        internal bool IsWellKnown => Protocol == Protocol.Ice1 && _endpoint == null;

        /// <summary>The facet path that holds the facet. Used only during marshaling/unmarshaling of ice1 proxies.
        /// </summary>
        internal IList<string> FacetPath { get; set; } = ImmutableList<string>.Empty;

        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile Connection? _connection;
        private Endpoint? _endpoint;
        private Identity _identity = Identity.Empty;

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
            return rhs.Equals(lhs);
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
            if (proxy.Protocol == Protocol.Ice1)
            {
                proxy.Identity = Identity.FromPath(path);
            }
            proxy.Endpoint = connection.IsServer ? null : connection.RemoteEndpoint;
            proxy.Connection = connection;
            proxy.Invoker = invoker;
            return proxy;
        }

        /// <summary>Creates a proxy from a path and protocol. Unlike Proxy's constructor, this method also sets
        /// Identity from the path when the protocol is ice1.</summary>
        /// <param name="path">The path.</param>
        /// <param name="protocol">The protocol.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromPath(string path, Protocol protocol = Protocol.Ice2)
        {
            var proxy = new Proxy(path, protocol);
            if (protocol == Protocol.Ice1)
            {
                proxy.Identity = Identity.FromPath(path);
            }
        }

        /// <summary>Creates a proxy from a server and a path.</summary>
        /// <param name="server">The server.</param>
        /// <param name="path">The path.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromServer(Server server, string path)
        {
            if (server.ProxyEndpoint == null)
            {
                throw new InvalidOperationException("cannot create a proxy using a server with no endpoint");
            }

            var proxy = new Proxy(path, server.Protocol);

            if (server.Protocol == Protocol.Ice1)
            {
                proxy.Identity = Identity.FromPath(path);
            }
            proxy.Endpoint = server.ProxyEndpoint;
            return proxy;
        }

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

            Proxy proxy;
            Encoding encoding;
            Endpoint? endpoint;
            ImmutableList<Endpoint> altEndpoints;
            if (Internal.UriParser.IsProxyUri(proxyString))
            {
                string path;
                (path, encoding, endpoint, altEndpoints) = Internal.UriParser.ParseProxy(proxyString);
                proxy = new(path, endpoint?.Protocol ?? Protocol.Ice2);
            }
            else
            {
                Identity identity;
                string facet;
                (identity, facet, encoding, endpoint, altEndpoints) = Ice1Parser.ParseProxy(proxyString);
                proxy = new(identity, facet);
            }
            proxy.Encoding = encoding;
            proxy.Endpoint = endpoint;
            proxy.AltEndpoints = altEndpoints;
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

        /// <summary>Constructs a new proxy.</summary>
        /// <param name="path">The proxy path.</param>
        /// <param name="protocol">The proxy protocol.</param>
        public Proxy(string path, Protocol protocol)
        {
            Protocol = protocol;
            Internal.UriParser.CheckPath(path, nameof(path));
            Path = path;
            Encoding = protocol.IsSupported() ? protocol.GetEncoding() : Encoding.V20;
        }

        /// <summary>Creates a shallow copy of this proxy.</summary>
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
            if (Facet != other.Facet)
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

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as Proxy);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // We only hash a subset of the properties to keep GetHashCode reasonably fast.
            var hash = new HashCode();
            hash.Add(Facet);
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
                return Interop.Proxy.ToString(this, default);
            }
            else // >= ice2, use URI format
            {
                var sb = new StringBuilder();
                bool firstOption = true;

                if (_endpoint != null)
                {
                    // Use ice+transport scheme
                    sb.AppendEndpoint(_endpoint, Path);
                    firstOption = !_endpoint.HasOptions;
                }
                else
                {
                    sb.Append("ice:"); // endpointless proxy
                    sb.Append(Path);
                }

                if (Encoding != Ice2Definitions.Encoding) // possible but quite unlikely
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("encoding=");
                    sb.Append(Encoding);
                }

                if (_altEndpoints.Count > 0)
                {
                    Transport mainTransport = _endpoint!.Transport;
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

        private Proxy(Identity identity, string facet)
            : this(identity.ToPath(), Protocol.Ice1)
        {
            Identity = identity;
            Facet = facet;
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
        /// <param name="streamWriter">The stream writer to write the stream parameter on the <see cref="RpcStream"/>.
        /// </param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When true, the request payload should be compressed.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="returnStreamReader">When true, a stream reader will be returned.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response payload, the optional stream reader, its encoding and the connection that received
        /// the response.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task<(ReadOnlyMemory<byte>, RpcStreamReader?, Encoding, Connection)> InvokeAsync(
            this Proxy proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            RpcStreamWriter? streamWriter = null,
            Invocation? invocation = null,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            bool returnStreamReader = false,
            CancellationToken cancel = default)
        {
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
                    TimeSpan timeout = invocation?.Timeout ?? Runtime.DefaultInvocationTimeout;
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

                var request = new OutgoingRequest(proxy,
                                                  operation,
                                                  requestPayload,
                                                  streamWriter,
                                                  deadline,
                                                  invocation,
                                                  idempotent,
                                                  oneway);

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

            async Task<(ReadOnlyMemory<byte> Payload, RpcStreamReader?, Encoding PayloadEncoding, Connection Connection)> ConvertResponseAsync(
                OutgoingRequest request,
                Task<IncomingResponse> responseTask,
                CancellationTokenSource? timeoutSource,
                CancellationTokenSource? combinedSource)
            {
                try
                {
                    IncomingResponse response = await responseTask.ConfigureAwait(false);

                    ReadOnlyMemory<byte> responsePayload = await response.GetPayloadAsync(cancel).ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.ResponseFeatures = response.Features;
                    }

                    if (response.ResultType == ResultType.Failure)
                    {
                        throw Payload.ToRemoteException(responsePayload,
                                                        response.PayloadEncoding,
                                                        response.ReplyStatus,
                                                        response.Connection,
                                                        proxy.Invoker);
                    }

                    RpcStreamReader? streamReader = null;
                    if (returnStreamReader)
                    {
                        streamReader = new RpcStreamReader(request);
                    }

                    return (responsePayload, streamReader, response.PayloadEncoding, response.Connection);
                }
                finally
                {
                    combinedSource?.Dispose();
                    timeoutSource?.Dispose();
                }
            }
        }
    }

    /// <summary>Proxy provides extension methods for IceDecoder.</summary>
    public static class IceDecoderProxyExtensions
    {
        /// <summary>Decodes a nullable proxy.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The proxy decoded by the decoder (can be null).</returns>
        public static Proxy? DecodeNullableProxy(this IceDecoder decoder)
        {
            if (decoder.Encoding == Encoding.V11)
            {
                var identity = new Identity(decoder);
                if (identity.Name.Length == 0) // such identity means received a null proxy with the 1.1 encoding
                {
                    return null;
                }

                var proxyData = new ProxyData11(decoder);

                if ((byte)proxyData.Protocol == 0)
                {
                    throw new InvalidDataException("received proxy with protocol set to 0");
                }
                if (proxyData.ProtocolMinor != 0)
                {
                    throw new InvalidDataException(
                        $"received proxy with invalid protocolMinor value: {proxyData.ProtocolMinor}");
                }

                // The min size for an Endpoint with the 1.1 encoding is: transport (short = 2 bytes) + encapsulation
                // header (6 bytes), for a total of 8 bytes.
                Endpoint[] endpointArray =
                    decoder.DecodeArray(minElementSize: 8, decoder => decoder.DecodeEndpoint11(proxyData.Protocol));

                Endpoint? endpoint = null;
                IEnumerable<Endpoint> altEndpoints;

                if (endpointArray.Length == 0)
                {
                    string adapterId = decoder.DecodeString();
                    if (adapterId.Length > 0)
                    {
                        endpoint = LocEndpoint.Create(adapterId, proxyData.Protocol);
                    }
                    altEndpoints = ImmutableList<Endpoint>.Empty;
                }
                else
                {
                    endpoint = endpointArray[0];
                    altEndpoints = endpointArray[1..];
                }

                if (proxyData.Protocol == Protocol.Ice1)
                {
                    if (proxyData.FacetPath.Count > 1)
                    {
                        throw new InvalidDataException(
                            $"received proxy with {proxyData.FacetPath.Count} elements in its facet path");
                    }

                    try
                    {
                        var proxy = new Proxy(identity.ToPath(), Protocol.Ice1);
                        proxy.Identity = identity;
                        proxy.FacetPath = proxyData.FacetPath;
                        proxy.Encoding = proxyData.Encoding;
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints.ToImmutableList();
                        proxy.Invoker = decoder.Invoker;
                        return proxy;
                    }
                    catch (InvalidDataException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
                else
                {
                    if (proxyData.FacetPath.Count > 0)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {proxyData.Protocol.GetName()} with facet");
                    }
                    if (proxyData.InvocationMode != InvocationMode.Twoway)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {proxyData.Protocol.GetName()} with invocation mode set");
                    }

                    try
                    {
                        Proxy proxy;

                        if (endpoint == null && decoder.Connection is Connection connection)
                        {
                            proxy = Proxy.FromConnection(connection, identity.ToPath(), decoder.Invoker);
                        }
                        else
                        {
                            proxy = new Proxy(identity.ToPath(), proxyData.Protocol);
                            proxy.Endpoint = endpoint;
                            proxy.AltEndpoints = altEndpoints.ToImmutableList();
                            proxy.Invoker = decoder.Invoker;
                        }

                        proxy.Encoding = proxyData.Encoding;
                        return proxy;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }
            else
            {
                Debug.Assert(decoder.Encoding == Encoding.V20);

                var proxyData = new ProxyData20(decoder);

                if (proxyData.Path == null)
                {
                    return null;
                }

                Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;
                var endpoint = proxyData.Endpoint?.ToEndpoint(protocol);
                ImmutableList<Endpoint> altEndpoints =
                    proxyData.AltEndpoints?.Select(
                        data => data.ToEndpoint(protocol))?.ToImmutableList() ?? ImmutableList<Endpoint>.Empty;

                if (endpoint == null && altEndpoints.Count > 0)
                {
                    throw new InvalidDataException("received proxy with only alt endpoints");
                }

                if (protocol == Protocol.Ice1)
                {
                    ImmutableList<string> facetPath;
                    string path;

                    int hashIndex = proxyData.Path.IndexOf('#', StringComparison.InvariantCulture);
                    if (hashIndex == -1)
                    {
                        path = proxyData.Path;
                        facetPath = ImmutableList<string>.Empty;
                    }
                    else
                    {
                        path = proxyData.Path[0..hashIndex];
                        facetPath = ImmutableList.Create(proxyData.Path[(hashIndex + 1)..]);
                    }

                    try
                    {
                        var proxy = Proxy.FromPath(path, Protocol.Ice1);
                        proxy.FacetPath = facetPath;
                        proxy.Encoding = proxyData.Encoding ?? Encoding.V20;
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints;
                        proxy.Invoker = decoder.Invoker;
                        return proxy;
                    }
                    catch (InvalidDataException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
                else
                {
                    try
                    {
                        Proxy proxy;

                        if (endpoint == null && decoder.Connection is Connection connection)
                        {
                            proxy = Proxy.FromConnection(connection, proxyData.Path, decoder.Invoker);
                        }
                        else
                        {
                            proxy = new Proxy(proxyData.Path, protocol);
                            proxy.Endpoint = endpoint;
                            proxy.AltEndpoints = altEndpoints;
                            proxy.Invoker = decoder.Invoker;
                        }

                        proxy.Encoding = proxyData.Encoding ?? Encoding.V20;

                        return proxy;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }
        }

        /// <summary>Decodes a nullable typed proxy.</summary>
        /// <paramtype name="T">The type of the new proxy.</paramtype>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The proxy decoded by the decoder (can be null).</returns>
        public static T? DecodeNullableProxy<T>(this IceDecoder decoder) where T : struct, IPrx =>
            decoder.DecodeNullableProxy() is Proxy proxy ? new T { Proxy = proxy } : null;

        /// <summary>Decodes a non-null proxy.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The non-null proxy decoded by the decoder.</returns>
        public static Proxy DecodeProxy(this IceDecoder decoder) =>
            decoder.DecodeNullableProxy() ?? throw new InvalidDataException("decoded null for a non-nullable proxy");

        /// <summary>Decodes a non-null typed proxy.</summary>
        /// <paramtype name="T">The type of the new proxy.</paramtype>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The non-null proxy decoded by the decoder.</returns>
        public static T DecodeProxy<T>(this IceDecoder decoder) where T : IPrx, new() =>
           new T { Proxy = decoder.DecodeProxy()};

        /// <summary>Decodes a tagged proxy.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The decoded proxy (can be null).</returns>
        public static Proxy? DecodeTaggedProxy(this IceDecoder decoder, int tag) =>
            decoder.DecodeTaggedProxyHeader(tag) ? decoder.DecodeProxy() : null;

        /// <summary>Decodes a tagged typed proxy.</summary>
        /// <paramtype name="T">The type of the new proxy.</paramtype>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The decoded proxy (can be null).</returns>
        public static T? DecodeTaggedProxy<T>(this IceDecoder decoder, int tag) where T : struct, IPrx =>
            decoder.DecodeTaggedProxyHeader(tag) ? decoder.DecodeProxy<T>() : null;
    }

    /// <summary>Proxy provides extension methods for IceEncoder.</summary>
    public static class IceEncoderProxyExtensions
    {
        /// <summary>Encodes a nullable proxy.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="proxy">The proxy to encode, or null.</param>
        public static void EncodeNullableProxy(this IceEncoder encoder, Proxy? proxy)
        {
            if (proxy != null)
            {
                encoder.Encode(proxy.Value);
            }
            else
            {
                if (encoder.OldEncoding)
                {
                    proxy.Identity.Empty.IceEncode(encoder);
                }
                else
                {
                    ProxyData20 nullValue = default;
                    nullValue.IceEncode(encoder);
                }
            }
        }

        /// <summary>Encodes a nullable typed proxy.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="prx">The proxy to encode, or null.</param>
        public static void EncodeNullableProxy(this IceEncoder encoder, IPrx? prx) =>
            encoder.EncodeNullableProxy(prx?.Proxy);

        /// <summary>Encodes a proxy.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="proxy">The proxy to encode, or null.</param>
        public static void EncodeProxy(this IceEncoder encoder, Proxy proxy)
        {
            if (proxy.Connection?.IsServer ?? false)
            {
                throw new InvalidOperationException("cannot marshal a proxy bound to a server connection");
            }

            if (encoder.OldEncoding)
            {
                if (proxy.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(Identity.Name.Length > 0);
                    Identity.IceEncode(encoder);
                }
                else
                {
                    Identity identity;
                    try
                    {
                        identity = Identity.FromPath(proxy.Path);
                    }
                    catch (FormatException ex)
                    {
                        throw new InvalidOperationException(
                            $"cannot marshal proxy with path '{proxy.Path}' using encoding 1.1",
                            ex);
                    }
                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"cannot marshal proxy with path '{proxy.Path}' using encoding 1.1");
                    }

                    identity.IceEncode(encoder);
                }

                var proxyData = new ProxyData11(
                    proxy.FacetPath,
                    proxy.Protocol == Protocol.Ice1 && (proxy.Endpoint?.IsDatagram ?? false) ?
                        InvocationMode.Datagram : InvocationMode.Twoway,
                    secure: false,
                    proxy.Protocol,
                    protocolMinor: 0,
                    proxy.Encoding);
                proxyData.IceEncode(encoder);

                if (IsIndirect)
                {
                    encoder.EncodeSize(0); // 0 endpoints
                    encoder.EncodeString(proxy.IsWellKnown ? "" : proxy.Endpoint!.Host); // adapter ID unless well-known
                }
                else if (proxy.Endpoint == null)
                {
                    encoder.EncodeSize(0); // 0 endpoints
                    encoder.EncodeString(""); // empty adapter ID
                }
                else
                {
                    IEnumerable<Endpoint> endpoints = proxy.Endpoint.Transport == Transport.Coloc ?
                        proxy.AltEndpoints :
                            Enumerable.Empty<Endpoint>().Append(proxy.Endpoint).Concat(proxy.AltEndpoints);

                    if (endpoints.Any())
                    {
                        encoder.EncodeSequence(endpoints, (encoder, endpoint) => encoder.EncodeEndpoint11(endpoint));
                    }
                    else // marshaled as an endpointless proxy
                    {
                        encoder.EncodeSize(0); // 0 endpoints
                        encoder.EncodeString(""); // empty adapter ID
                    }
                }
            }
            else
            {
                Debug.Assert(encoder.Encoding == Encoding.V20);
                string path = proxy.Path;

                // Facet is the only ice1-specific option that is encoded when using the 2.0 encoding.
                if (proxy.Facet.Length > 0)
                {
                    path = $"{path}#{Uri.EscapeDataString(proxy.Facet)}";
                }

                var proxyData = new ProxyData20(
                    path,
                    protocol: proxy.Protocol != Protocol.Ice2 ? proxy.Protocol : null,
                    encoding: proxy.Encoding != Encoding.V20 ? proxy.Encoding : null,
                    endpoint: proxy.Endpoint is Endpoint endpoint && endpoint.Transport != Transport.Coloc ?
                        endpoint.Data : null,
                    altEndpoints: proxy.AltEndpoints.Count() == 0 ?
                        null : proxy.AltEndpoints.Select(e => e.Data).ToArray());

                proxyData.IceEncode(encoder);
            }
        }

        /// <summary>Encodes a typed proxy.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="prx">The proxy to encode.</param>
        public static void EncodeProxy(this IceEncoder encoder, IPrx prx) => encoder.EncodeProxy(prx.Proxy);

        /// <summary>Encodes a tagged proxy.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="tag">The tag.</param>
        /// <param name="proxy">The proxy to encode.</param>
        public static void EncodeTaggedProxy(this IceEncoder encoder, int tag, Proxy? proxy)
        {
            if (proxy != null)
            {
                encoder.EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = encoder.StartFixedLengthSize();
                encoder.EncodeProxy(proxy);
                encoder.EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged typed proxy.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="tag">The tag.</param>
        /// <param name="proxy">The proxy to encode.</param>
        public static void EncodeTaggedProxy(this IceEncoder encoder, int tag, IPrx? prx) =>
            encoder.EncodeTaggedProxy(tag, prx);
    }
}
