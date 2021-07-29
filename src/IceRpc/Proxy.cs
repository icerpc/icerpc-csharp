// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using IceRpc.Transports.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

                    if (_endpoint.Transport == TransportNames.Loc || _endpoint.Transport == TransportNames.Coloc)
                    {
                        throw new ArgumentException(
                            @$"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)
                            } uses the loc or coloc transports",
                            nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Transport == TransportNames.Loc || e.Transport == TransportNames.Coloc))
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

        /// <summary>The encoding that a caller should use when encoding request parameters when such a caller supports
        /// multiple encodings. Its value is usually the encoding of the protocol.</summary>
        public string Encoding
        {
            get => _encoding;
            set => _encoding = string.IsInterned(value) ?? value;
        }

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
                        (value.Transport == TransportNames.Loc || value.Transport == TransportNames.Coloc))
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

        /// <summary>The Ice encoding version of this proxy, when <see cref="Encoding"/> is null or is a string that
        /// corresponds to a supported Ice encoding version such as "1.1" or "2.0".</summary>
        /// <exception name="NotSupportedException">Thrown when <see cref="Encoding"/> is set to some other value.
        /// </exception>
        public Encoding IceEncodingVersion =>
            Encoding switch
            {
                EncodingNames.V11 => IceRpc.Encoding.V11,
                EncodingNames.V20 => IceRpc.Encoding.V20,
                _ => throw new NotSupportedException($"'{Encoding}' is not a supported Ice encoding")
            };

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

        internal bool IsIndirect => _endpoint?.Transport == TransportNames.Loc || IsWellKnown;
        internal bool IsWellKnown => Protocol == Protocol.Ice1 && _endpoint == null;

        /// <summary>The facet path that holds the facet. Used only during marshaling/unmarshaling of ice1 proxies.
        /// </summary>
        internal IList<string> FacetPath { get; set; } = ImmutableList<string>.Empty;

        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile Connection? _connection;
        private Endpoint? _endpoint;
        private string _encoding;
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

        /// <summary>Creates a proxy from a path and protocol. This method sets Identity from the path when the
        /// protocol is ice1.</summary>
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
                return Interop.ProxyExtensions.ToString(this, default);
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

                if (Encoding != Ice2Definitions.Encoding.ToString())
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

            if (Protocol == Protocol.Ice1)
            {
                proxy.Identity = Identity.FromPath(path);

                // clear cached connection of well-known proxy
                if (proxy.Endpoint == null)
                {
                    proxy.Connection = null;
                }
            }
            return proxy;
        }

        internal static Proxy? Decode(IceDecoder decoder)
        {
            Debug.Assert(decoder.Connection != null);

            if (decoder.IceEncoding == IceRpc.Encoding.V11)
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
                        endpoint = new Endpoint(proxyData.Protocol,
                                                TransportNames.Loc,
                                                Host: adapterId,
                                                Port: proxyData.Protocol == Protocol.Ice1 ?
                                                    Ice1Parser.DefaultPort :
                                                    IceUriParser.DefaultUriPort,
                                                Params: ImmutableList<EndpointParam>.Empty);
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
                        proxy.Encoding = IceRpc.Encoding.FromMajorMinor(proxyData.EncodingMajor, proxyData.EncodingMinor).ToString();
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

                        proxy.Encoding = IceRpc.Encoding.FromMajorMinor(proxyData.EncodingMajor, proxyData.EncodingMinor).ToString();
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
                var proxyData = new ProxyData20(decoder);

                if (proxyData.Path == null)
                {
                    return null;
                }

                Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;
                Endpoint? endpoint = proxyData.Endpoint is EndpointData data ? data.ToEndpoint() : null;
                ImmutableList<Endpoint> altEndpoints =
                    proxyData.AltEndpoints?.Select(data => data.ToEndpoint()).ToImmutableList() ??
                        ImmutableList<Endpoint>.Empty;

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
                        proxy.Encoding = proxyData.Encoding ?? Ice1Definitions.Encoding.ToString();
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

                        proxy.Encoding = proxyData.Encoding ?? proxy.Protocol.GetEncoding().ToString();

                        return proxy;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }
        }

        /// <summary>Constructs a new proxy.</summary>
        /// <param name="path">The proxy path.</param>
        /// <param name="protocol">The proxy protocol.</param>
        internal Proxy(string path, Protocol protocol = Protocol.Ice2)
        {
            Protocol = protocol;
            IceUriParser.CheckPath(path, nameof(path));
            Path = path;

            _encoding = Protocol.IsSupported() ? Protocol.GetEncoding().ToString() : "";
        }

        internal void Encode(IceEncoder encoder)
        {
            if (Connection?.IsServer ?? false)
            {
                throw new InvalidOperationException("cannot encode a proxy bound to a server connection");
            }

            if (encoder.IceEncoding == IceRpc.Encoding.V11)
            {
                if (Protocol == Protocol.Ice1)
                {
                    Debug.Assert(Identity.Name.Length > 0);
                    Identity.Encode(encoder);
                }
                else
                {
                    Identity identity;
                    try
                    {
                        identity = Identity.FromPath(Path);
                    }
                    catch (FormatException ex)
                    {
                        throw new InvalidOperationException(
                            $"cannot encode proxy with path '{Path}' using encoding 1.1",
                            ex);
                    }
                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"cannot encode proxy with path '{Path}' using encoding 1.1");
                    }

                    identity.Encode(encoder);
                }

                (byte encodingMajor, byte encodingMinor) = IceEncodingVersion.ToMajorMinor();
                var proxyData = new ProxyData11(
                    FacetPath,
                    Protocol == Protocol.Ice1 && (Endpoint?.Transport == TransportNames.Udp) ?
                        InvocationMode.Datagram : InvocationMode.Twoway,
                    secure: false,
                    Protocol,
                    protocolMinor: 0,
                    encodingMajor,
                    encodingMinor);
                proxyData.Encode(encoder);

                if (IsIndirect)
                {
                    encoder.EncodeSize(0); // 0 endpoints
                    encoder.EncodeString(IsWellKnown ? "" : Endpoint!.Host); // adapter ID unless well-known
                }
                else if (Endpoint == null)
                {
                    encoder.EncodeSize(0); // 0 endpoints
                    encoder.EncodeString(""); // empty adapter ID
                }
                else
                {
                    IEnumerable<Endpoint> endpoints = Endpoint.Transport == TransportNames.Coloc ?
                        AltEndpoints : Enumerable.Empty<Endpoint>().Append(Endpoint).Concat(AltEndpoints);

                    if (endpoints.Any())
                    {
                        if (Protocol == Protocol.Ice1)
                        {
                            encoder.EncodeSequence(
                                endpoints,
                                (encoder, endpoint) => EndpointEncoder.EncodeEndpoint(endpoint, encoder));
                        }
                        else
                        {
                            encoder.EncodeSequence(
                                endpoints,
                                (encoder, endpoint) =>
                                    encoder.EncodeEndpoint11(
                                        endpoint,
                                        TransportCode.Any,
                                        static (encoder, endpoint) => endpoint.ToEndpointData().Encode(encoder)));
                        }
                    }
                    else // encoded as an endpointless proxy
                    {
                        encoder.EncodeSize(0); // 0 endpoints
                        encoder.EncodeString(""); // empty adapter ID
                    }
                }
            }
            else
            {
                string path = Path;

                // Facet is the only ice1-specific option that is encoded when using the 2.0 encoding.
                if (Facet.Length > 0)
                {
                    path = $"{path}#{Uri.EscapeDataString(Facet)}";
                }

                var proxyData = new ProxyData20(
                    path,
                    protocol: Protocol != Protocol.Ice2 ? Protocol : null,
                    encoding:
                        Protocol.IsSupported() && Encoding == Protocol.GetEncoding().ToString() ? null : Encoding,
                    endpoint: Endpoint is Endpoint endpoint && endpoint.Transport != TransportNames.Coloc ?
                        endpoint.ToEndpointData() : null,
                    altEndpoints: AltEndpoints.Count == 0 ? null : AltEndpoints.Select(e => e.ToEndpointData()).ToArray());

                proxyData.Encode(encoder);
            }
        }

        internal Proxy(Identity identity, string facet)
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
                                                        Encoding.Parse(response.PayloadEncoding),
                                                        response.ReplyStatus,
                                                        response.Connection,
                                                        proxy.Invoker);
                    }

                    RpcStreamReader? streamReader = null;
                    if (returnStreamReader)
                    {
                        streamReader = new RpcStreamReader(request);
                    }

                    return (responsePayload, streamReader, Encoding.Parse(response.PayloadEncoding), response.Connection);
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
