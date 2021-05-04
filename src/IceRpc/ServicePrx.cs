// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The base afor all service proxies. Applications should use proxies through interfaces and rarely
    /// use this class directly.</summary>
    public class ServicePrx : IServicePrx, IEquatable<ServicePrx>
    {
        /// <inheritdoc/>
        public IEnumerable<string> AltEndpoints
        {
            get => ParsedAltEndpoints.Select(e => e.ToString());
            set => ParsedAltEndpoints = value.Select(s => IceRpc.Endpoint.Parse(s)).ToImmutableList();
        }

        /// <inheritdoc/>
        public bool CacheConnection { get; set; }

        /// <inheritdoc/>
        public Connection? Connection
        {
            get => _connection;
            set => _connection = value;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Context
        {
            // TODO: should we change this property's type to ImmutableSortedDictionary<string, string>?
            get => _context;
            set => _context = value.ToImmutableSortedDictionary();
        }

        /// <inheritdoc/>
        public Encoding Encoding { get; set; }

        /// <inheritdoc/>
        public string Endpoint
        {
            get => _endpoint?.ToString() ?? "";
            set => ParsedEndpoint = value.Length > 0 ? IceRpc.Endpoint.Parse(value) : null;
        }

        /// <inheritdoc/>
        public TimeSpan InvocationTimeout
        {
            get => _invocationTimeout;
            set => _invocationTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException("0 is not a valid value for the invocation timeout", nameof(value));
        }

        /// <inheritdoc/>
        public IInvoker Invoker { get; set; }

        /// <inheritdoc/>
        public bool IsOneway { get; set; }

        /// <inheritdoc/>
        public ILocationResolver? LocationResolver { get; set; }

        /// <summary>Gets or sets the endpoint objects that back <see cref="AltEndpoints"/>.</summary>
        public ImmutableList<Endpoint> ParsedAltEndpoints
        {
            get => _altEndpoints;

            private set
            {
                if (value.Count > 0)
                {
                    if (_endpoint == null)
                    {
                        throw new ArgumentException(
                            $"cannot set {nameof(ParsedAltEndpoints)} when {nameof(ParsedEndpoint)} is empty",
                            nameof(ParsedAltEndpoints));
                    }

                    if (_endpoint.Transport == Transport.Loc || _endpoint.Transport == Transport.Coloc)
                    {
                        throw new ArgumentException(
                            @$"cannot set {nameof(ParsedAltEndpoints)} when {nameof(ParsedEndpoint)
                            } uses the loc or coloc transports",
                            nameof(ParsedAltEndpoints));
                    }

                    if (value.Any(e => e.Transport == Transport.Loc || e.Transport == Transport.Coloc))
                    {
                        throw new ArgumentException("cannot use loc or coloc transport", nameof(ParsedAltEndpoints));
                    }

                    if (value.Any(e => e.Protocol != Protocol))
                    {
                        throw new ArgumentException($"the protocol of all endpoints must be {Protocol.GetName()}",
                                                     nameof(ParsedAltEndpoints));
                    }
                }
                // else, no need to check anything, an empty list is always fine.

                _altEndpoints = value;
            }
        }

        /// <summary>Gets or sets the endpoint object that backs <see cref="Endpoint"/>.</summary>
        public Endpoint? ParsedEndpoint
        {
            get => _endpoint;

            private set
            {
                if (value != null)
                {
                    if (value.Protocol != Protocol)
                    {
                        throw new ArgumentException("the new endpoint must use the proxy's protocol",
                                                    nameof(ParsedEndpoint));
                    }
                    if (_altEndpoints.Count > 0 &&
                        (value.Transport == Transport.Loc || value.Transport == Transport.Coloc))
                    {
                        throw new ArgumentException(
                            "a proxy with a loc or coloc endpoint cannot have alt endpoints", nameof(ParsedEndpoint));
                    }
                }
                else if (_altEndpoints.Count > 0)
                {
                    throw new ArgumentException(
                        $"cannot clear {nameof(ParsedEndpoint)} when {nameof(ParsedAltEndpoints)} is not empty",
                        nameof(ParsedEndpoint));
                }
                _endpoint = value;
            }
        }

        /// <inheritdoc/>
        public string Path { get; } = "";

        /// <inheritdoc/>
        public bool PreferExistingConnection { get; set; }

        /// <inheritdoc/>
        public Protocol Protocol { get; }

        ServicePrx IServicePrx.Impl => this;

        internal string Facet { get; } = "";
        internal Identity Identity { get; } = Identity.Empty;

        internal bool IsIndirect => _endpoint is Endpoint endpoint && endpoint.Transport == Transport.Loc;
        internal bool IsWellKnown => Protocol == Protocol.Ice1 && IsIndirect && _endpoint!.Data.Options.Length > 0;

        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile Connection? _connection;
        private ImmutableSortedDictionary<string, string> _context;

        private Endpoint? _endpoint;

        private TimeSpan _invocationTimeout;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(ServicePrx? lhs, ServicePrx? rhs)
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
        public static bool operator !=(ServicePrx? lhs, ServicePrx? rhs) => !(lhs == rhs);

        /// <inheritdoc/>
        public bool Equals(ServicePrx? other)
        {
            if (other == null)
            {
                return false;
            }
            else if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (CacheConnection != other.CacheConnection)
            {
                return false;
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
            if (_invocationTimeout != other._invocationTimeout)
            {
                return false;
            }
            if (IsOneway != other.IsOneway)
            {
                return false;
            }
            if (LocationResolver != other.LocationResolver)
            {
                return false;
            }
            if (Path != other.Path)
            {
                return false;
            }
            if (PreferExistingConnection != other.PreferExistingConnection)
            {
                return false;
            }
            if (Protocol != other.Protocol)
            {
                return false;
            }

            if (!_context.DictionaryEqual(other._context)) // done last since it's more expensive
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public bool Equals(IServicePrx? other) => Equals(other?.Impl);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ServicePrx);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // We only hash a subset of the properties to keep GetHashCode reasonably fast.
            var hash = new HashCode();
            hash.Add(Facet);
            hash.Add(_invocationTimeout);
            hash.Add(Invoker);
            hash.Add(IsOneway);
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

        /// <inheritdoc/>
        public void IceWrite(OutputStream ostr)
        {
            if (_connection?.IsIncoming ?? false)
            {
                throw new InvalidOperationException("cannot marshal a proxy bound to an incoming connection");
            }

            InvocationMode? invocationMode = IsOneway ? InvocationMode.Oneway : null;
            if (Protocol == Protocol.Ice1 && IsOneway && (_endpoint?.IsDatagram ?? false))
            {
                invocationMode = InvocationMode.Datagram;
            }

            if (ostr.Encoding == Encoding.V11)
            {
                if (Protocol == Protocol.Ice1)
                {
                    Debug.Assert(Identity.Name.Length > 0);
                    Identity.IceWrite(ostr);
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
                            @$"cannot marshal proxy with path '{Path}' using encoding 1.1", ex);
                    }
                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            @$"cannot marshal proxy with path '{Path}' using encoding 1.1");
                    }

                    identity.IceWrite(ostr);
                }

                ostr.WriteProxyData11(Facet, invocationMode ?? InvocationMode.Twoway, Protocol, Encoding);

                if (IsIndirect)
                {
                    ostr.WriteSize(0); // 0 endpoints
                    ostr.WriteString(IsWellKnown ? "" : _endpoint!.Host); // adapter ID unless well-known
                }
                else if (_endpoint == null)
                {
                    ostr.WriteSize(0); // 0 endpoints
                    ostr.WriteString(""); // empty adapter ID
                }
                else
                {
                    IEnumerable<Endpoint> endpoints = _endpoint.Transport == Transport.Coloc ?
                        _altEndpoints : Enumerable.Empty<Endpoint>().Append(_endpoint).Concat(_altEndpoints);

                    if (endpoints.Any())
                    {
                        ostr.WriteSequence(endpoints, (ostr, endpoint) => ostr.WriteEndpoint11(endpoint));
                    }
                    else // marshaled as an endpointless proxy
                    {
                        ostr.WriteSize(0); // 0 endpoints
                        ostr.WriteString(""); // empty adapter ID
                    }
                }
            }
            else
            {
                Debug.Assert(ostr.Encoding == Encoding.V20);
                string path = Path;

                // Facet is the only ice1-specific option that is encoded when using the 2.0 encoding.
                if (Facet.Length > 0)
                {
                    path = $"{path}#{Uri.EscapeDataString(Facet)}";
                }

                var proxyData = new ProxyData20(
                    path,
                    protocol: Protocol != Protocol.Ice2 ? Protocol : null,
                    encoding: Encoding != Encoding.V20 ? Encoding : null,
                    endpoint: _endpoint is Endpoint endpoint && endpoint.Transport != Transport.Coloc ?
                         endpoint.Data : null,
                    altEndpoints: _altEndpoints.Count == 0 ? null : _altEndpoints.Select(e => e.Data).ToArray());

                proxyData.IceWrite(ostr);
            }
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

                if (!CacheConnection)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("cache-connection=false");
                }

                if (Context.Count > 0)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("context=");
                    int index = 0;
                    foreach ((string key, string value) in Context)
                    {
                        sb.Append(Uri.EscapeDataString(key));
                        sb.Append('=');
                        sb.Append(Uri.EscapeDataString(value));
                        if (++index != Context.Count)
                        {
                            sb.Append(',');
                        }
                    }
                }

                if (Encoding != Ice2Definitions.Encoding) // possible but quite unlikely
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("encoding=");
                    sb.Append(Encoding);
                }

                if (_invocationTimeout != ProxyOptions.DefaultInvocationTimeout)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("invocation-timeout=");
                    sb.Append(TimeSpanExtensions.ToPropertyValue(_invocationTimeout));
                }

                if (IsOneway)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("oneway=true");
                }

                if (!PreferExistingConnection)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("prefer-existing-connection=false");
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

        /// <summary>Constructs a new proxy class instance with the specified options.</summary>
        protected internal ServicePrx(
            string path,
            Protocol protocol,
            Encoding encoding,
            Endpoint? endpoint,
            IEnumerable<Endpoint> altEndpoints,
            Connection? connection,
            ProxyOptions options)
            : this(protocol, encoding, endpoint, altEndpoints, connection, options)
        {
            Internal.UriParser.CheckPath(path, nameof(path));
            Path = path;

            if (Protocol == Protocol.Ice1)
            {
                Identity = Identity.FromPath(Path);
                if (Identity.Name.Length == 0)
                {
                    throw new ArgumentException("cannot create ice1 service proxy with an empty identity name",
                                                 nameof(path));
                }
                // and keep facet empty
            }
        }

        /// <summary>Constructs a new proxy class instance with the specified options.</summary>
        protected internal ServicePrx(
            Identity identity,
            string facet,
            Encoding encoding,
            Endpoint? endpoint,
            IEnumerable<Endpoint> altEndpoints,
            Connection? connection,
            ProxyOptions options)
            : this(Protocol.Ice1, encoding, endpoint, altEndpoints, connection, options)
        {
            if (identity.Name.Length == 0)
            {
                throw new ArgumentException("cannot create ice1 service proxy with an empty identity name",
                                             nameof(identity));
            }

            Identity = identity;
            Facet = facet;
            Path = identity.ToPath();
        }

        // TODO: currently cancel is/should always be request.CancellationToken but we should eliminate
        // request.CancellationToken.
        public static async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            Activity? activity = null;

            var communicator = (Communicator)request.Proxy.Invoker;

            // TODO add a client ActivitySource and use it to start the activities
            // Start the invocation activity before running client side interceptors. Activities started
            // by interceptors will be children of IceRpc.Invocation activity.
            if (communicator.Logger.IsEnabled(LogLevel.Critical) || Activity.Current != null)
            {
                activity = new Activity("IceRpc.Invocation");
                activity.AddTag("Operation", request.Operation);
                activity.AddTag("Path", request.Path);
                activity.Start();
            }

            ServicePrx proxy = request.Proxy.Impl;
            try
            {
                return await proxy.Invoker.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            finally
            {
                activity?.Stop();
            }
        }

        /// <summary>Creates a shallow copy of this service proxy.</summary>
        internal ServicePrx Clone() => (ServicePrx)MemberwiseClone();

        /// <summary>Returns a new copy of the underlying options.</summary>
        internal ProxyOptions GetOptions() =>
             new()
             {
                 CacheConnection = CacheConnection,
                 Context = _context,
                 InvocationTimeout = _invocationTimeout,
                 Invoker = Invoker,
                 IsOneway = IsOneway,
                 LocationResolver = LocationResolver,
                 PreferExistingConnection = PreferExistingConnection
             };

        /// <summary>Provides the implementation of <see cref="Proxy.GetConnectionAsync"/>.</summary>
        internal async ValueTask<Connection> GetConnectionAsync(CancellationToken cancel)
        {
            Connection? connection = _connection;
            if (connection != null && connection.IsActive)
            {
                return connection;
            }

            var communicator = (Communicator)Invoker;

            using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancel,
                communicator.CancellationToken);
            cancel = linkedCancellationSource.Token;

            List<Endpoint>? endpoints = null;

            if ((connection == null || (!connection.IsIncoming && !connection.IsActive)) && PreferExistingConnection)
            {
                // No cached connection, so now check if there is an existing connection that we can reuse.
                endpoints = await communicator.ComputeEndpointsAsync(this,
                                                                     refreshCache: false,
                                                                     IsOneway,
                                                                     cancel).ConfigureAwait(false);
                connection = communicator.GetConnection(endpoints);
                if (CacheConnection)
                {
                    _connection = connection;
                }
            }

            OutgoingConnectionOptions options = communicator.ConnectionOptions.Clone();

            bool refreshCache = false;

            while (connection == null)
            {
                if (endpoints == null)
                {
                    endpoints = await communicator.ComputeEndpointsAsync(this,
                                                                         refreshCache,
                                                                         IsOneway,
                                                                         cancel).ConfigureAwait(false);
                }

                Endpoint last = endpoints[^1];
                foreach (Endpoint endpoint in endpoints)
                {
                    try
                    {
                        connection = await communicator.ConnectAsync(endpoint, options, cancel).ConfigureAwait(false);
                        if (CacheConnection)
                        {
                            _connection = connection;
                        }
                        break;
                    }
                    catch
                    {
                        // Ignore the exception unless this is the last endpoint.
                        if (ReferenceEquals(endpoint, last))
                        {
                            if (IsIndirect && !refreshCache)
                            {
                                // Try again once with freshly resolved endpoints
                                refreshCache = true;
                                endpoints = null;
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }
            }
            Debug.Assert(connection != null);
            return connection;
        }

        /// <summary>Provides the implementation of <see cref="Proxy.ToProperty(IServicePrx, string)"/>.</summary>
        internal Dictionary<string, string> ToProperty(string prefix)
        {
            var properties = new Dictionary<string, string> { [prefix] = ToString() };

            if (Protocol == Protocol.Ice1)
            {
                if (!CacheConnection)
                {
                    properties[$"{prefix}.CacheConnection"] = "false";
                }

                // We don't output context as this would require hard-to-generate escapes.

                if (_invocationTimeout != ProxyOptions.DefaultInvocationTimeout)
                {
                    // For ice2 the invocation timeout is included in the URI
                    properties[$"{prefix}.InvocationTimeout"] = _invocationTimeout.ToPropertyValue();
                }
                if (!PreferExistingConnection)
                {
                    properties[$"{prefix}.PreferExistingConnection"] = "false";
                }
            }
            // else, only a single property in the dictionary

            return properties;
        }

        // Helper constructor
        private ServicePrx(
            Protocol protocol,
            Encoding encoding,
            Endpoint? endpoint,
            IEnumerable<Endpoint> altEndpoints,
            Connection? connection,
            ProxyOptions options)
        {
            CacheConnection = options.CacheConnection;
            _connection = connection;
            _context = options.Context.ToImmutableSortedDictionary();
            Encoding = encoding;
            _invocationTimeout = options.InvocationTimeout;
            Invoker = options.Invoker!;
            IsOneway = options.IsOneway;
            LocationResolver = options.LocationResolver;
            PreferExistingConnection = options.PreferExistingConnection;
            Protocol = protocol;

            ParsedEndpoint = endpoint; // use the ParsedEndpoint set validation
            if (altEndpoints.Any())
            {
                ParsedAltEndpoints = altEndpoints.ToImmutableList();
            }
        }
    }
}
