// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Instrumentation;
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
    /// <summary>The base class for all service proxies. In general, applications should use proxies through interfaces
    /// and not through this class.</summary>
    public class ServicePrx : IServicePrx, IEquatable<ServicePrx>
    {
        /// <inheritdoc/>
        public bool CacheConnection { get; set; }

        /// <inheritdoc/>
        public Connection? Connection
        {
            get => _connection;
            set => _connection = value;
        }

        public Communicator Communicator { get; }

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
        public IReadOnlyList<Endpoint> Endpoints
        {
            get => _endpoints;
            set
            {
                var endpoints = value.ToImmutableList();
                if (endpoints.Count > 0)
                {
                    // TODO: we should not use Linq each time we unmarshal a proxy.

                    if (endpoints.Count > 1 && endpoints.Any(e => e.Transport == Transport.Loc))
                    {
                        throw new ArgumentException("a loc endpoint must be the only endpoint", nameof(Endpoints));
                    }

                    if (endpoints.Any(e => e.Protocol != Protocol))
                    {
                        throw new ArgumentException($"the protocol of all endpoints must be {Protocol.GetName()}",
                                                    nameof(Endpoints));
                    }
                }
                _endpoints = endpoints;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<InvocationInterceptor> InvocationInterceptors
        {
            get => _invocationInterceptors;
            set => _invocationInterceptors = value.ToImmutableList();
        }

        /// <inheritdoc/>
        public TimeSpan InvocationTimeout
        {
            get => _invocationTimeout;
            set => _invocationTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException("0 is not a valid value for the invocation timeout", nameof(value));
        }

        /// <inheritdoc/>
        public bool IsOneway { get; set; }

        /// <inheritdoc/>
        public ILocationResolver? LocationResolver { get; set; }

        /// <inheritdoc/>
        public NonSecure NonSecure { get; set; }

        /// <inheritdoc/>
        public string Path { get; } = "";

        /// <inheritdoc/>
        public bool PreferExistingConnection { get; set; }

        /// <inheritdoc/>
        public Protocol Protocol { get; }

        ServicePrx IServicePrx.Impl => this;

        internal string Facet { get; } = "";
        internal Identity Identity { get; } = Identity.Empty;

        internal bool IsFixed => Endpoints.Count == 0 && !IsRelative;
        internal bool IsIndirect => Endpoints.Count == 1 && Endpoints[0].Transport == Transport.Loc;
        internal bool IsRelative =>
            Endpoints.Count == 0 &&
            (_connection?.Endpoint.Transport ?? Transport.Coloc) == Transport.Coloc;
        internal bool IsWellKnown => Protocol == Protocol.Ice1 && IsIndirect && Endpoints[0].HasOptions;

        private volatile Connection? _connection;

        private ImmutableSortedDictionary<string, string> _context;

        private ImmutableList<Endpoint> _endpoints;

        private ImmutableList<InvocationInterceptor> _invocationInterceptors;

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
            if (!_endpoints.SequenceEqual(other._endpoints))
            {
                return false;
            }
            if (Facet != other.Facet)
            {
                return false;
            }
            if (!_invocationInterceptors.SequenceEqual(other._invocationInterceptors))
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
            if (NonSecure != other.NonSecure)
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

            if (IsFixed)
            {
                if (_connection != other._connection)
                {
                    return false;
                }
            }
            // else we assume that for non-fixed proxies, connection differences don't affect equality.

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
            hash.Add(IsOneway);
            hash.Add(Path);
            hash.Add(Protocol);
            if (IsFixed)
            {
                hash.Add(_connection);
            }
            else if (_endpoints.Count > 0)
            {
                hash.Add(_endpoints[0].GetHashCode());
            }
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public void IceWrite(OutputStream ostr)
        {
            if (IsFixed)
            {
                throw new NotSupportedException("cannot marshal a fixed proxy");
            }

            InvocationMode? invocationMode = IsOneway ? InvocationMode.Oneway : null;
            if (Protocol == Protocol.Ice1 && IsOneway && Endpoints.Count > 0 && Endpoints.All(e => e.IsDatagram))
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
                    ostr.WriteString(IsWellKnown ? "" : Endpoints[0].Host); // adapter ID unless well-known
                }
                else if (IsRelative)
                {
                    ostr.WriteSize(0); // 0 endpoints
                    ostr.WriteString(""); // empty adapter ID
                }
                else
                {
                    Debug.Assert(Endpoints.Count > 0);

                    IEnumerable<Endpoint> endpoints = Endpoints.Where(e => e.Transport != Transport.Coloc);

                    if (endpoints.Any())
                    {
                        ostr.WriteSequence(endpoints, (ostr, endpoint) => ostr.WriteEndpoint11(endpoint));
                    }
                    else // marshaled as relative/well-known proxy
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

                IEnumerable<Endpoint> endpoints = Endpoints.Where(e => e.Transport != Transport.Coloc);

                var proxyData = new ProxyData20(
                    path,
                    protocol: Protocol != Protocol.Ice2 ? Protocol : null,
                    encoding: Encoding != Encoding.V20 ? Encoding : null,
                    endpoints.Any() ? endpoints.Select(e => e.Data).ToArray() : null);

                proxyData.IceWrite(ostr);
            }
        }

        /// <summary>Converts the reference into a string. The format of this string depends on the protocol: for ice1,
        /// this method uses the ice1 format, which can be customized by Communicator.ToStringMode. For ice2 and
        /// greater, this method uses the URI format.</summary>
        public override string ToString()
        {
            if (Protocol == Protocol.Ice1)
            {
                var sb = new StringBuilder();

                // If the encoded identity string contains characters which the reference parser uses as separators,
                // then we enclose the identity string in quotes.
                string id = Identity.ToString(Communicator.ToStringMode);
                if (StringUtil.FindFirstOf(id, " :@") != -1)
                {
                    sb.Append('"');
                    sb.Append(id);
                    sb.Append('"');
                }
                else
                {
                    sb.Append(id);
                }

                if (Facet.Length > 0)
                {
                    // If the encoded facet string contains characters which the reference parser uses as separators,
                    // then we enclose the facet string in quotes.
                    sb.Append(" -f ");
                    string fs = StringUtil.EscapeString(Facet, Communicator.ToStringMode);
                    if (StringUtil.FindFirstOf(fs, " :@") != -1)
                    {
                        sb.Append('"');
                        sb.Append(fs);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(fs);
                    }
                }

                if (IsOneway)
                {
                    if (Endpoints.Count > 0 && Endpoints.All(e => e.IsDatagram))
                    {
                        sb.Append(" -d");
                    }
                    else
                    {
                        sb.Append(" -o");
                    }
                }
                else
                {
                    sb.Append(" -t");
                }

                // Always print the encoding version to ensure a stringified proxy will convert back to a proxy with the
                // same encoding with StringToProxy. (Only needed for backwards compatibility).
                sb.Append(" -e ");
                sb.Append(Encoding.ToString());

                if (IsIndirect)
                {
                    if (!IsWellKnown)
                    {
                        string adapterId = Endpoints[0].Host;

                        sb.Append(" @ ");

                        // If the encoded adapter ID contains characters which the proxy parser uses as separators, then
                        // we enclose the adapter ID string in double quotes.
                        adapterId = StringUtil.EscapeString(adapterId, Communicator.ToStringMode);
                        if (StringUtil.FindFirstOf(adapterId, " :@") != -1)
                        {
                            sb.Append('"');
                            sb.Append(adapterId);
                            sb.Append('"');
                        }
                        else
                        {
                            sb.Append(adapterId);
                        }
                    }
                }
                else
                {
                    foreach (Endpoint e in Endpoints)
                    {
                        sb.Append(':');
                        sb.Append(e);
                    }
                }
                return sb.ToString();
            }
            else // >= ice2, use URI format
            {
                var sb = new StringBuilder();
                bool firstOption = true;

                if (Endpoints.Count > 0)
                {
                    // Use ice+transport scheme
                    Endpoint mainEndpoint = Endpoints[0];
                    sb.AppendEndpoint(mainEndpoint, Path);
                    firstOption = !mainEndpoint.HasOptions;
                }
                else
                {
                    sb.Append("ice:"); // relative proxy
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

                if (IsFixed)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("fixed=true");
                }

                if (_invocationTimeout != ProxyOptions.DefaultInvocationTimeout)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("invocation-timeout=");
                    sb.Append(TimeSpanExtensions.ToPropertyValue(_invocationTimeout));
                }

                if (NonSecure != NonSecure.Always)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("non-secure=");
                    sb.Append(NonSecure.ToString().ToLowerInvariant());
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

                if (Endpoints.Count > 1)
                {
                    Transport mainTransport = Endpoints[0].Transport;
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("alt-endpoint=");
                    for (int i = 1; i < Endpoints.Count; ++i)
                    {
                        if (i > 1)
                        {
                            sb.Append(',');
                        }
                        sb.AppendEndpoint(Endpoints[i], "", mainTransport != Endpoints[i].Transport, '$');
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
            IEnumerable<Endpoint> endpoints,
            Connection? connection,
            ProxyOptions options)
            : this(protocol, encoding, endpoints, connection, options)
        {
            UriParser.CheckPath(path, nameof(path));
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
            IEnumerable<Endpoint> endpoints,
            Connection? connection,
            ProxyOptions options)
            : this(Protocol.Ice1, encoding, endpoints, connection, options)
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

        internal static Task<IncomingResponseFrame> InvokeAsync(
            IServicePrx proxy,
            OutgoingRequestFrame request,
            bool oneway,
            IProgress<bool>? progress = null)
        {
            IReadOnlyList<InvocationInterceptor> invocationInterceptors = proxy.InvocationInterceptors;

            return InvokeWithInterceptorsAsync(proxy,
                                               request,
                                               oneway,
                                               0,
                                               progress,
                                               request.CancellationToken);

            async Task<IncomingResponseFrame> InvokeWithInterceptorsAsync(
                IServicePrx proxy,
                OutgoingRequestFrame request,
                bool oneway,
                int i,
                IProgress<bool>? progress,
                CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();

                if (i < invocationInterceptors.Count)
                {
                    // Call the next interceptor in the chain
                    return await invocationInterceptors[i](
                        proxy,
                        request,
                        (target, request, cancel) =>
                            InvokeWithInterceptorsAsync(target, request, oneway, i + 1, progress, cancel),
                        cancel).ConfigureAwait(false);
                }
                else
                {
                    // After we went down the interceptor chain make the invocation.
                    ServicePrx impl = proxy.Impl;
                    Communicator communicator = impl.Communicator;
                    // If the request size is greater than Ice.RetryRequestSizeMax or the size of the request
                    // would increase the buffer retry size beyond Ice.RetryBufferSizeMax we release the request
                    // after it was sent to avoid holding too much memory and we wont retry in case of a failure.

                    // TODO: this "request size" is now just the payload size. Should we rename the property to
                    // RetryRequestPayloadMaxSize?

                    int requestSize = request.PayloadSize;
                    bool releaseRequestAfterSent =
                        requestSize > communicator.RetryRequestMaxSize ||
                        !communicator.IncRetryBufferSize(requestSize);

                    IInvocationObserver? observer = communicator.Observer?.GetInvocationObserver(proxy,
                                                                                                 request.Operation,
                                                                                                 request.Context);
                    observer?.Attach();
                    try
                    {
                        return await impl.PerformInvokeAsync(request,
                                                             oneway,
                                                             progress,
                                                             releaseRequestAfterSent,
                                                             observer,
                                                             cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (!releaseRequestAfterSent)
                        {
                            communicator.DecRetryBufferSize(requestSize);
                        }
                        // TODO release the request memory if not already done after sent.
                        // TODO: Use IDisposable for observers, this will allow using "using".
                        observer?.Detach();
                    }
                }
            }
        }

        /// <summary>Creates a shallow copy of this service proxy.</summary>
        internal ServicePrx Clone() => (ServicePrx)MemberwiseClone();

        /// <summary>Returns a new copy of the underlying options.</summary>
        internal ProxyOptions GetOptions() =>
             new()
             {
                 CacheConnection = CacheConnection,
                 Communicator = Communicator,
                 Context = _context,
                 InvocationInterceptors = _invocationInterceptors,
                 InvocationTimeout = _invocationTimeout,
                 IsOneway = IsOneway,
                 LocationResolver = LocationResolver,
                 NonSecure = NonSecure,
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
            using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancel,
                Communicator.CancellationToken);
            cancel = linkedCancellationSource.Token;

            List<Endpoint>? endpoints = null;

            if ((connection == null || (!IsFixed && !connection.IsActive)) && PreferExistingConnection)
            {
                // No cached connection, so now check if there is an existing connection that we can reuse.
                endpoints =
                    await ComputeEndpointsAsync(refreshCache: false, IsOneway, cancel).ConfigureAwait(false);
                connection = Communicator.GetConnection(endpoints, NonSecure);
                if (CacheConnection)
                {
                    _connection = connection;
                }
            }

            OutgoingConnectionOptions options = Communicator.ConnectionOptions.Clone();
            options.NonSecure = NonSecure;

            bool refreshCache = false;

            while (connection == null)
            {
                if (endpoints == null)
                {
                    endpoints = await ComputeEndpointsAsync(refreshCache, IsOneway, cancel).ConfigureAwait(false);
                }

                Endpoint last = endpoints[^1];
                foreach (Endpoint endpoint in endpoints)
                {
                    try
                    {
                        connection = await Communicator.ConnectAsync(endpoint, options, cancel).ConfigureAwait(false);
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
            if (IsFixed)
            {
                throw new NotSupportedException("cannot convert a fixed proxy to a property dictionary");
            }

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
                if (NonSecure != NonSecure.Always)
                {
                    properties[$"{prefix}.NonSecure"] = NonSecure.ToString();
                }
            }
            // else, only a single property in the dictionary

            return properties;
        }

        // Helper constructor
        private ServicePrx(
            Protocol protocol,
            Encoding encoding,
            IEnumerable<Endpoint> endpoints,
            Connection? connection,
            ProxyOptions options)
        {
            CacheConnection = options.CacheConnection;
            Communicator = options.Communicator!;
            _connection = connection;
            _context = options.Context.ToImmutableSortedDictionary();
            Encoding = encoding;
            _invocationInterceptors = options.InvocationInterceptors.ToImmutableList();
            _invocationTimeout = options.InvocationTimeout;
            IsOneway = options.IsOneway;
            LocationResolver = options.LocationResolver;
            NonSecure = options.NonSecure;
            PreferExistingConnection = options.PreferExistingConnection;
            Protocol = protocol;

            _endpoints = ImmutableList<Endpoint>.Empty;
            var endpointList = endpoints.ToImmutableList();
            if (endpointList.Count > 0)
            {
                Endpoints = endpointList; // use Endpoints set validation, which uses Protocol
            }
        }

        private void ClearConnection(Connection connection)
        {
            Debug.Assert(!IsFixed);
            Interlocked.CompareExchange(ref _connection, null, connection);
        }

        private async ValueTask<List<Endpoint>> ComputeEndpointsAsync(
            bool refreshCache,
            bool oneway,
            CancellationToken cancel)
        {
            Debug.Assert(!IsFixed);

            foreach (Endpoint endpoint in Endpoints)
            {
                if (endpoint.ToColocEndpoint() is Endpoint colocEndpoint)
                {
                    return new List<Endpoint>() { colocEndpoint };
                }
            }

            IReadOnlyList<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;

            // Get the proxy's endpoint or query the location resolver to get endpoints.

            if (IsIndirect)
            {
                if (LocationResolver is ILocationResolver locationResolver)
                {
                    endpoints =
                        await locationResolver.ResolveAsync(Endpoints[0], refreshCache, cancel).ConfigureAwait(false);
                }
                // else endpoints remains empty.
            }
            else if (Endpoints.Count > 0)
            {
                endpoints = Endpoints;
            }

            // Apply overrides and filter endpoints
            var filteredEndpoints = endpoints.Where(endpoint =>
            {
                // Filter out opaque and universal endpoints
                if (endpoint is OpaqueEndpoint || endpoint is UniversalEndpoint)
                {
                    return false;
                }

                // With ice1 when secure endpoint is required filter out all non-secure endpoints.
                if (Protocol == Protocol.Ice1 && NonSecure == NonSecure.Never && !endpoint.IsAlwaysSecure)
                {
                    return false;
                }

                // Filter out datagram endpoints when oneway is false.
                if (endpoint.IsDatagram)
                {
                    return oneway;
                }

                return true;
            }).ToList();

            if (filteredEndpoints.Count == 0)
            {
                throw new NoEndpointException(ToString());
            }

            if (filteredEndpoints.Count > 1)
            {
                filteredEndpoints = Communicator.OrderEndpointsByTransportFailures(filteredEndpoints);
            }
            return filteredEndpoints;
        }

        private async Task<IncomingResponseFrame> PerformInvokeAsync(
            OutgoingRequestFrame request,
            bool oneway,
            IProgress<bool>? progress,
            bool releaseRequestAfterSent,
            IInvocationObserver? observer,
            CancellationToken cancel)
        {
            Connection? connection = _connection;
            List<Endpoint>? endpoints = null;

            if (connection != null && !oneway && connection.Endpoint.IsDatagram)
            {
                throw new InvalidOperationException(
                    "cannot make two-way invocation using a cached datagram connection");
            }

            if ((connection == null || (!IsFixed && !connection.IsActive)) && PreferExistingConnection)
            {
                // No cached connection, so now check if there is an existing connection that we can reuse.
                endpoints = await ComputeEndpointsAsync(refreshCache: false, oneway, cancel).ConfigureAwait(false);
                connection = Communicator.GetConnection(endpoints, NonSecure);
                if (CacheConnection)
                {
                    _connection = connection;
                }
            }

            OutgoingConnectionOptions connectionOptions = Communicator.ConnectionOptions.Clone();
            connectionOptions.NonSecure = NonSecure;

            ILogger logger = Communicator.Logger;
            int nextEndpoint = 0;
            int attempt = 1;
            bool triedAllEndpoints = false;
            List<Endpoint>? excludedEndpoints = null;
            IncomingResponseFrame? response = null;
            Exception? exception = null;

            bool tryAgain = false;

            do
            {
                bool sent = false;
                SocketStream? stream = null;
                try
                {
                    if (connection == null)
                    {
                        if (endpoints == null)
                        {
                            Debug.Assert(nextEndpoint == 0);

                            // ComputeEndpointsAsync throws if it can't figure out the endpoints
                            // We also request fresh endpoints when retrying, but not for the first attempt.
                            endpoints = await ComputeEndpointsAsync(refreshCache: tryAgain,
                                                                    oneway,
                                                                    cancel).ConfigureAwait(false);
                            if (excludedEndpoints != null)
                            {
                                endpoints = endpoints.Except(excludedEndpoints).ToList();
                                if (endpoints.Count == 0)
                                {
                                    endpoints = null;
                                    throw new NoEndpointException();
                                }
                            }
                        }

                        connection = await Communicator.ConnectAsync(endpoints[nextEndpoint],
                                                                     connectionOptions,
                                                                     cancel).ConfigureAwait(false);

                        if (CacheConnection)
                        {
                            _connection = connection;
                        }
                    }

                    cancel.ThrowIfCancellationRequested();

                    response?.Dispose();
                    response = null;

                    using IDisposable? socketScope = connection.Socket.StartScope();

                    // Create the outgoing stream.
                    stream = connection.CreateStream(!oneway);

                    // Send the request and wait for the sending to complete.
                    await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

                    using IDisposable? streamSocket = stream.StartScope();
                    logger.LogSentRequest(request);

                    // The request is sent, notify the progress callback.
                    // TODO: Get rid of the sentSynchronously parameter which is always false now?
                    if (progress != null)
                    {
                        progress.Report(false);
                        progress = null; // Only call the progress callback once (TODO: revisit this?)
                    }
                    if (releaseRequestAfterSent)
                    {
                        // TODO release the request
                    }
                    sent = true;
                    exception = null;

                    if (oneway)
                    {
                        return IncomingResponseFrame.WithVoidReturnValue(request.Protocol, request.PayloadEncoding);
                    }

                    // Wait for the reception of the response.
                    response = await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);

                    logger.LogReceivedResponse(response);

                    // If success, just return the response!
                    if (response.ResultType == ResultType.Success)
                    {
                        return response;
                    }
                    observer?.RemoteException();
                }
                catch (NoEndpointException ex) when (tryAgain)
                {
                    // If we get NoEndpointException while retrying, either all endpoints have been excluded or the
                    // proxy has no endpoints. So we cannot retry, and we return here to preserve any previous
                    // exception that might have been thrown.
                    observer?.Failed(ex.GetType().FullName ?? "System.Exception"); // TODO cleanup observer logic
                    return response ?? throw exception ?? ex;
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    stream?.Release();
                }

                // Compute retry policy based on the exception or response retry policy, whether or not the connection
                // is established or the request sent and idempotent
                Debug.Assert(response != null || exception != null);
                RetryPolicy retryPolicy =
                    response?.GetRetryPolicy(this) ?? exception!.GetRetryPolicy(request.IsIdempotent, sent);

                // With the retry-policy OtherReplica we add the current endpoint to the list of excluded
                // endpoints this prevents the endpoints to be tried again during the current retry sequence.
                if (retryPolicy == RetryPolicy.OtherReplica &&
                    (endpoints?[nextEndpoint] ?? connection?.Endpoint) is Endpoint endpoint)
                {
                    excludedEndpoints ??= new();
                    excludedEndpoints.Add(endpoint);
                }

                if (endpoints != null && (connection == null || retryPolicy == RetryPolicy.OtherReplica))
                {
                    // If connection establishment failed or if the endpoint was excluded, try the next endpoint
                    nextEndpoint = ++nextEndpoint % endpoints.Count;
                    if (nextEndpoint == 0)
                    {
                        // nextEndpoint == 0 indicates that we already tried all the endpoints.
                        if (IsIndirect && !tryAgain)
                        {
                            // If we were potentially using cached endpoints, so we clear the endpoints before trying
                            // again.
                            endpoints = null;
                        }
                        else
                        {
                            // Otherwise we set triedAllEndpoints to true to ensure further connection establishment
                            // failures will now count as attempts (to prevent indefinitely looping if connection
                            // establishment failure results in a retryable exception).
                            triedAllEndpoints = true;
                            if (excludedEndpoints != null)
                            {
                                endpoints = endpoints.Except(excludedEndpoints).ToList();
                            }
                        }
                    }
                }

                // Check if we can retry, we cannot retry if we have consumed all attempts, the current retry
                // policy doesn't allow retries, the request was already released, there are no more endpoints
                // or a fixed reference receives an exception with OtherReplica retry policy.

                if (attempt == Communicator.InvocationMaxAttempts ||
                    retryPolicy == RetryPolicy.NoRetry ||
                    (sent && releaseRequestAfterSent) ||
                    (triedAllEndpoints && endpoints != null && endpoints.Count == 0) ||
                    (IsFixed && retryPolicy == RetryPolicy.OtherReplica))
                {
                    tryAgain = false;
                }
                else
                {
                    tryAgain = true;

                    // Only count an attempt if the connection was established or if all the endpoints were tried
                    // at least once. This ensures that we don't end up into an infinite loop for connection
                    // establishment failures which don't result in endpoint exclusion.
                    if (connection != null)
                    {
                        attempt++;

                        using IDisposable? socketScope = connection?.Socket.StartScope();
                        logger.LogRetryRequestRetryableException(
                            retryPolicy,
                            attempt,
                            Communicator.InvocationMaxAttempts,
                            request,
                            exception);
                    }
                    else if (triedAllEndpoints)
                    {
                        attempt++;

                        logger.LogRetryRequestConnectionException(
                            retryPolicy,
                            attempt,
                            Communicator.InvocationMaxAttempts,
                            request,
                            exception);
                    }

                    if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                    {
                        // The delay task can be canceled either by the user code using the provided cancellation
                        // token or if the communicator is destroyed.
                        await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                    }

                    observer?.Retried();

                    if (!IsFixed && connection != null)
                    {
                        // Retry with a new connection!
                        connection = null;
                    }
                }
            }
            while (tryAgain);

            if (exception != null)
            {
                logger.LogRequestException(request, exception);
            }

            // TODO cleanup observer logic we report "System.Exception" for all remote exceptions
            observer?.Failed(exception?.GetType().FullName ?? "System.Exception");
            Debug.Assert(response != null || exception != null);
            Debug.Assert(response == null || response.ResultType == ResultType.Failure);
            return response ?? throw ExceptionUtil.Throw(exception!);
        }
    }
}
