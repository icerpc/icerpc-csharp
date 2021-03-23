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
        public bool CacheConnection { get; } = true;
        public Communicator Communicator { get; }
        public IReadOnlyDictionary<string, string> Context { get; }
        public Encoding Encoding { get; }
        public IReadOnlyList<Endpoint> Endpoints { get; } = ImmutableList<Endpoint>.Empty;
        public IReadOnlyList<InvocationInterceptor> InvocationInterceptors { get; }

        public TimeSpan InvocationTimeout => _invocationTimeoutOverride ?? TimeSpan.FromSeconds(60);
        public bool IsFixed { get; }

        public bool IsOneway { get; }
        public object? Label { get; }
        public ILocationResolver? LocationResolver { get; }

        public string Path { get; } = "";

        public bool PreferExistingConnection => _preferExistingConnectionOverride ?? true;
        public NonSecure PreferNonSecure => _preferNonSecureOverride ?? NonSecure.Always; // TODO: fix default
        public Protocol Protocol { get; }

        ServicePrx IServicePrx.Impl => this;

        internal string Facet { get; } = "";
        internal Identity Identity { get; } = Identity.Empty;

        internal bool IsIndirect => !IsFixed && Endpoints.Count == 1 && Endpoints[0].Transport == Transport.Loc;

        internal bool IsRelative => Protocol != Protocol.Ice1 && Endpoints.Count == 0 && !IsFixed;
        internal bool IsWellKnown => Protocol == Protocol.Ice1 && IsIndirect && Endpoints[0].HasOptions;

        private volatile Connection? _connection;
        private int _hashCode; // cached hash code value

        // The various Override fields override the value from the communicator. When null, they are not included in
        // the ToString()/ToProperty() representation.

        private readonly TimeSpan? _invocationTimeoutOverride;

        private readonly bool? _preferExistingConnectionOverride;
        private readonly NonSecure? _preferNonSecureOverride;

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

            if (_hashCode != 0 && other._hashCode != 0 && _hashCode != other._hashCode)
            {
                return false;
            }

            // Compare common properties
            if (Encoding != other.Encoding)
            {
                return false;
            }
            if (!InvocationInterceptors.SequenceEqual(other.InvocationInterceptors))
            {
                return false;
            }
            if (_invocationTimeoutOverride != other._invocationTimeoutOverride)
            {
                return false;
            }
            if (IsFixed != other.IsFixed)
            {
                return false;
            }
            if (IsOneway != other.IsOneway)
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

            if (!Context.DictionaryEqual(other.Context)) // done last since it's more expensive
            {
                return false;
            }

            if (Protocol == Protocol.Ice1 && Facet != other.Facet)
            {
                return false;
            }

            if (IsFixed)
            {
                // Compare properties and fields specific to fixed proxies
                if (_connection != other._connection)
                {
                    return false;
                }
            }
            else
            {
                // Compare properties specific to non-fixed proxies
                if (CacheConnection != other.CacheConnection)
                {
                    return false;
                }
                if (!Endpoints.SequenceEqual(other.Endpoints))
                {
                    return false;
                }
                if (Label != other.Label)
                {
                    return false;
                }
                if (LocationResolver != other.LocationResolver)
                {
                    return false;
                }
                if (_preferExistingConnectionOverride != other._preferExistingConnectionOverride)
                {
                    return false;
                }
                if (_preferNonSecureOverride != other._preferNonSecureOverride)
                {
                    return false;
                }
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
            if (_hashCode != 0)
            {
                // Already computed, return cached value.
                return _hashCode;
            }
            else
            {
                // Lazy initialization of _hashCode to a value other than 0. Reading/writing _hashCode is atomic.
                var hash = new HashCode();

                // common properties
                hash.Add(Context.GetDictionaryHashCode());
                hash.Add(Encoding);
                hash.Add(InvocationInterceptors.GetSequenceHashCode());
                hash.Add(_invocationTimeoutOverride);
                hash.Add(IsFixed);
                hash.Add(IsOneway);
                hash.Add(Path);
                hash.Add(Protocol);

                if (Protocol == Protocol.Ice1)
                {
                    hash.Add(Facet);
                }

                if (IsFixed)
                {
                    hash.Add(_connection);
                }
                else
                {
                    hash.Add(CacheConnection);
                    hash.Add(Endpoints.GetSequenceHashCode());
                    hash.Add(Label);
                    hash.Add(LocationResolver);
                    hash.Add(_preferExistingConnectionOverride);
                    hash.Add(_preferNonSecureOverride);
                }

                int hashCode = hash.ToHashCode();
                if (hashCode == 0)
                {
                    // 0 means uninitialized so we switch to 1
                    hashCode = 1;
                }
                _hashCode = hashCode;
                return _hashCode;
            }
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
                            @$"cannot marshal proxy with path `{Path}' using encoding 1.1", ex);
                    }
                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            @$"cannot marshal proxy with path `{Path}' using encoding 1.1");
                    }

                    identity.IceWrite(ostr);
                }

                ostr.WriteProxyData11(Facet, invocationMode ?? InvocationMode.Twoway, Protocol, Encoding);

                if (IsIndirect)
                {
                    ostr.WriteSize(0); // 0 endpoints
                    ostr.WriteString(IsWellKnown ? "" : Endpoints[0].Host); // adapter ID unless well-known
                }
                else
                {
                    ostr.WriteSequence(Endpoints, (ostr, endpoint) => ostr.WriteEndpoint11(endpoint));
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

                var proxyData = new ProxyData20(Path,
                                                protocol: Protocol != Protocol.Ice2 ? Protocol : null,
                                                encoding: Encoding != Encoding.V20 ? Encoding : null,
                                                Endpoints.Count == 0 ? null : Endpoints.Select(e => e.Data).ToArray());
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

                if (_invocationTimeoutOverride is TimeSpan invocationTimeout)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("invocation-timeout=");
                    sb.Append(TimeSpanExtensions.ToPropertyValue(invocationTimeout));
                }

                if (Label?.ToString() is string label && label.Length > 0)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("label=");
                    sb.Append(Uri.EscapeDataString(label));
                }

                if (IsOneway)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("oneway=true");
                }

                if (_preferExistingConnectionOverride is bool preferExistingConnection)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("prefer-existing-connection=");
                    sb.Append(preferExistingConnection ? "true" : "false");
                }

                if (_preferNonSecureOverride is NonSecure preferNonSecure)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("prefer-non-secure=");
                    sb.Append(preferNonSecure.ToString().ToLowerInvariant());
                }

                if (Protocol != Protocol.Ice2) // i.e. > ice2
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("protocol=");
                    sb.Append(Protocol.GetName());
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

        /// <summary>Constructs a new proxy class instance with the specified options. All dictionaries / lists must be
        /// safe to reference as-is since they are not copied by this constructor.</summary>
        protected internal ServicePrx(ServicePrxOptions options)
        {
            int endpointCount = options.Endpoints.Count;

            if (options.IsFixed)
            {
                if (endpointCount > 0)
                {
                    throw new ArgumentException("a fixed proxy cannot specify endpoints", nameof(options));
                }
                if (options.Connection == null)
                {
                    throw new ArgumentException("a fixed proxy requires a connection", nameof(options));
                }
            }

            if (endpointCount > 0)
            {
                if (endpointCount > 1 && options.Endpoints.Any(e => e.Transport == Transport.Loc))
                {
                    throw new ArgumentException("a loc endpoint must be the only endpoint", nameof(options));
                }

                if (options.Endpoints.Any(e => e.Protocol != options.Protocol))
                {
                    throw new ArgumentException($"the protocol of all endpoints must be {options.Protocol.GetName()}",
                                                nameof(options));
                }
            }
            else if (!options.IsFixed && options.Protocol == Protocol.Ice1)
            {
                throw new ArgumentException("a non-fixed ice1 proxy requires at least one endpoint",
                                            nameof(options));
            }

            if (options.InvocationTimeoutOverride is TimeSpan invocationTimeoutOverride &&
                invocationTimeoutOverride == TimeSpan.Zero)
            {
                throw new ArgumentException("0 is not a valid value for the invocation timeout", nameof(options));
            }

            CacheConnection = options.CacheConnection;
            Communicator = options.Communicator!;
            Context = options.Context ?? ImmutableDictionary<string, string>.Empty;
            Encoding = options.Encoding ?? options.Protocol.GetEncoding();
            Endpoints = options.Endpoints;
            InvocationInterceptors = options.InvocationInterceptors ?? ImmutableList<InvocationInterceptor>.Empty;
            IsFixed = options.IsFixed;
            IsOneway = options.IsOneway;
            Label = options.Label;
            LocationResolver = options.LocationResolver;
            Protocol = options.Protocol;
            _connection = options.Connection;
            _invocationTimeoutOverride = options.InvocationTimeoutOverride;
            _preferExistingConnectionOverride = options.PreferExistingConnectionOverride;
            _preferNonSecureOverride = options.PreferNonSecureOverride;

            if (Protocol == Protocol.Ice1)
            {
                if (options is InteropServicePrxOptions interopOptions)
                {
                    Facet = interopOptions.Facet;
                    Identity = interopOptions.Identity;
                }

                if (options.Path.Length > 0)
                {
                    if (Identity != Identity.Empty)
                    {
                        throw new ArgumentException("cannot specify both path and identity", nameof(options));
                    }

                    Path = UriParser.NormalizePath(options.Path);
                    Identity = Identity.FromPath(Path);

                    if (Identity.Name.Length == 0)
                    {
                        throw new ArgumentException("cannot create ice1 service proxy with an empty identity name",
                                                    nameof(options));
                    }
                }
                else
                {
                    if (Identity.Name.Length == 0)
                    {
                        throw new ArgumentException("cannot create ice1 service proxy with an empty identity name",
                                                    nameof(options));
                    }
                    Path = Identity.ToPath();
                }
            }
            else
            {
                Path = UriParser.NormalizePath(options.Path);
            }
        }

        /// <summary>Creates a new proxy with the same type as <c>this</c> and with the provided options. Derived
        /// proxy classes must override this method.</summary>
        protected virtual ServicePrx IceClone(ServicePrxOptions options) => new(options);

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

        /// <summary>Creates a new proxy with the same type as this proxy and the provided options.</summary>
        internal ServicePrx Clone(ServicePrxOptions options) => IceClone(options);

        /// <summary>Returns a fresh copy of the underlying options.</summary>
        internal ServicePrxOptions CloneOptions()
        {
            if (Protocol == Protocol.Ice1)
            {
                return new InteropServicePrxOptions()
                {
                    CacheConnection = CacheConnection,
                    Communicator = Communicator,
                    Connection = _connection,
                    Context = Context,
                    Encoding = Encoding,
                    Endpoints = Endpoints,
                    Facet = Facet,
                    Identity = Identity,
                    InvocationInterceptors = InvocationInterceptors,
                    InvocationTimeoutOverride = _invocationTimeoutOverride,
                    IsFixed = IsFixed,
                    IsOneway = IsOneway,
                    Label = Label,
                    LocationResolver = LocationResolver,
                    Path = "",
                    PreferExistingConnectionOverride = _preferExistingConnectionOverride,
                    PreferNonSecureOverride = _preferNonSecureOverride,
                    Protocol = Protocol.Ice1
                };
            }
            else
            {
                return new()
                {
                    CacheConnection = CacheConnection,
                    Communicator = Communicator,
                    Connection = _connection,
                    Context = Context,
                    Encoding = Encoding,
                    Endpoints = Endpoints,
                    InvocationInterceptors = InvocationInterceptors,
                    InvocationTimeoutOverride = _invocationTimeoutOverride,
                    IsFixed = IsFixed,
                    IsOneway = IsOneway,
                    Label = Label,
                    LocationResolver = LocationResolver,
                    Path = Path,
                    PreferExistingConnectionOverride = _preferExistingConnectionOverride,
                    PreferNonSecureOverride = _preferNonSecureOverride,
                    Protocol = Protocol
                };
            }
        }

        /// <summary>Provides the implementation of <see cref="Proxy.GetCachedConnection"/>.</summary>
        internal Connection? GetCachedConnection() => _connection;

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
            TimeSpan endpointsAge = TimeSpan.Zero;
            TimeSpan endpointsMaxAge = TimeSpan.MaxValue;
            List<Endpoint>? endpoints = null;
            if ((connection == null || (!IsFixed && !connection.IsActive)) && PreferExistingConnection)
            {
                // No cached connection, so now check if there is an existing connection that we can reuse.
                (endpoints, endpointsAge) =
                    await ComputeEndpointsAsync(endpointsMaxAge, IsOneway, cancel).ConfigureAwait(false);
                connection = Communicator.GetConnection(endpoints, PreferNonSecure, Label);
                if (CacheConnection)
                {
                    _connection = connection;
                }
            }

            var options = Communicator.ConnectionOptions.Clone();
            options.Label = Label;
            options.PreferNonSecure = PreferNonSecure;

            while (connection == null)
            {
                if (endpoints == null)
                {
                    (endpoints, endpointsAge) = await ComputeEndpointsAsync(endpointsMaxAge,
                                                                            IsOneway,
                                                                            cancel).ConfigureAwait(false);
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
                            if (IsIndirect && endpointsAge != TimeSpan.Zero && endpointsMaxAge == TimeSpan.MaxValue)
                            {
                                // If the first lookup for an indirect reference returns an endpoint from the cache, set
                                // endpointsMaxAge to force a new locator lookup for a fresher endpoint.
                                endpointsMaxAge = endpointsAge;
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

                if (_invocationTimeoutOverride is TimeSpan invocationTimeout)
                {
                    // For ice2 the invocation timeout is included in the URI
                    properties[$"{prefix}.InvocationTimeout"] = invocationTimeout.ToPropertyValue();
                }
                if (Label?.ToString() is string label && label.Length > 0)
                {
                    properties[$"{prefix}.Label"] = label;
                }
                if (_preferExistingConnectionOverride is bool preferExistingConnection)
                {
                    properties[$"{prefix}.PreferExistingConnection"] = preferExistingConnection ? "true" : "false";
                }
                if (_preferNonSecureOverride is NonSecure preferNonSecure)
                {
                    properties[$"{prefix}.PreferNonSecure"] = preferNonSecure.ToString();
                }
            }
            // else, only a single property in the dictionary

            return properties;
        }

        private void ClearConnection(Connection connection)
        {
            Debug.Assert(!IsFixed);
            Interlocked.CompareExchange(ref _connection, null, connection);
        }

        private async ValueTask<(List<Endpoint> Endpoints, TimeSpan EndpointsAge)> ComputeEndpointsAsync(
            TimeSpan endpointsMaxAge,
            bool oneway,
            CancellationToken cancel)
        {
            Debug.Assert(!IsFixed);

            if (LocalServerRegistry.GetColocatedEndpoint(this) is Endpoint colocatedEndpoint)
            {
                return (new List<Endpoint>() { colocatedEndpoint }, TimeSpan.Zero);
            }

            IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan endpointsAge = TimeSpan.Zero;

            // Get the proxy's endpoint or query the location resolver to get endpoints.

            if (IsIndirect)
            {
                if (LocationResolver is ILocationResolver locationService)
                {
                    (endpoints, endpointsAge) =
                        await locationService.ResolveAsync(Endpoints[0], endpointsMaxAge, cancel).ConfigureAwait(false);

                }
                // else endpoints remains empty.
            }
            else if (Endpoints.Count > 0)
            {
                endpoints = Endpoints.ToList();
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
                if (Protocol == Protocol.Ice1 && PreferNonSecure == NonSecure.Never && !endpoint.IsAlwaysSecure)
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
            return (filteredEndpoints, endpointsAge);
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
            TimeSpan endpointsMaxAge = TimeSpan.MaxValue;
            TimeSpan endpointsAge = TimeSpan.Zero;
            List<Endpoint>? endpoints = null;

            if (connection != null && !oneway && connection.Endpoint.IsDatagram)
            {
                throw new InvalidOperationException(
                    "cannot make two-way invocation using a cached datagram connection");
            }

            if ((connection == null || (!IsFixed && !connection.IsActive)) && PreferExistingConnection)
            {
                // No cached connection, so now check if there is an existing connection that we can reuse.
                (endpoints, endpointsAge) =
                    await ComputeEndpointsAsync(endpointsMaxAge, oneway, cancel).ConfigureAwait(false);
                connection = Communicator.GetConnection(endpoints, PreferNonSecure, Label);
                if (CacheConnection)
                {
                    _connection = connection;
                }
            }

            var connectionOptions = Communicator.ConnectionOptions.Clone();
            connectionOptions.Label = Label;
            connectionOptions.PreferNonSecure = PreferNonSecure;

            ILogger protocolLogger = Communicator.ProtocolLogger;
            int nextEndpoint = 0;
            int attempt = 1;
            bool triedAllEndpoints = false;
            List<Endpoint>? excludedEndpoints = null;
            IncomingResponseFrame? response = null;
            Exception? exception = null;

            bool tryAgain;
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
                            (endpoints, endpointsAge) = await ComputeEndpointsAsync(endpointsMaxAge,
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

                    using var socketScope = connection.Socket.StartScope();
                    using var requestScope = protocolLogger.StartRequestScope(request);

                    // Create the outgoing stream.
                    stream = connection.CreateStream(!oneway);

                    // Send the request and wait for the sending to complete.
                    await stream.SendRequestFrameAsync(request, cancel).ConfigureAwait(false);

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
                    response?.Dispose();

                    if (oneway)
                    {
                        return IncomingResponseFrame.WithVoidReturnValue(request.Protocol, request.PayloadEncoding);
                    }

                    // TODO: create the scope when the stream is started rather than after the request creation.
                    using var streamScope = stream.StartScope();

                    // Wait for the reception of the response.
                    response = await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);

                    if (protocolLogger.IsEnabled(LogLevel.Information))
                    {
                        protocolLogger.LogReceivedResponse(response);
                    }

                    // If success, just return the response!
                    if (response.ResultType == ResultType.Success)
                    {
                        return response;
                    }
                    observer?.RemoteException();
                }
                catch (NoEndpointException ex) when (endpointsAge == TimeSpan.Zero)
                {
                    // If we get NoEndpointException while using non cached endpoints, either all endpoints
                    // have been excluded or the proxy has no endpoints. we cannot retry, return here to
                    // preserve any previous exceptions that might have been throw.
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
                        // nextendpoint == 0 indicates that we already tried all the endpoints.
                        if (endpointsAge != TimeSpan.Zero)
                        {
                            // If we were using cached endpoints, we clear the endpoints, and set endpointsMaxAge to
                            // request a fresher endpoint.
                            endpoints = null;
                            endpointsMaxAge = endpointsAge;
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
                    if (protocolLogger.IsEnabled(LogLevel.Debug))
                    {
                        using var socketScope = connection?.Socket.StartScope();
                        using var requestScope = protocolLogger.StartRequestScope(request);
                        if (connection != null)
                        {
                            protocolLogger.LogRetryRequestInvocation(retryPolicy,
                                                                     attempt,
                                                                     Communicator.InvocationMaxAttempts,
                                                                     exception);
                        }
                        else if (triedAllEndpoints)
                        {
                            protocolLogger.LogRetryConnectionEstablishment(retryPolicy,
                                                                           attempt,
                                                                           Communicator.InvocationMaxAttempts,
                                                                           exception);
                        }
                        else
                        {
                            protocolLogger.LogRetryConnectionEstablishment(exception);
                        }
                    }

                    if (connection != null || triedAllEndpoints)
                    {
                        // Only count an attempt if the connection was established or if all the endpoints were tried
                        // at least once. This ensures that we don't end up into an infinite loop for connection
                        // establishment failures which don't result in endpoint exclusion.
                        attempt++;
                    }

                    if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                    {
                        // The delay task can be canceled either by the user code using the provided cancellation
                        // token or if the communicator is destroyed.
                        await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                    }

                    observer?.Retried();

                    // If an indirect proxy is using an endpoint from the cache, set endpointsMaxAge to force a new
                    // locator lookup.
                    if (IsIndirect && endpointsAge != TimeSpan.Zero)
                    {
                        endpointsMaxAge = endpointsAge;
                        endpoints = null;
                    }

                    if (!IsFixed && connection != null)
                    {
                        // Retry with a new connection!
                        connection = null;
                    }
                }
            }
            while (tryAgain);

            // TODO cleanup observer logic we report "System.Exception" for all remote exceptions
            observer?.Failed(exception?.GetType().FullName ?? "System.Exception");
            Debug.Assert(response != null || exception != null);
            Debug.Assert(response == null || response.ResultType == ResultType.Failure);
            return response ?? throw ExceptionUtil.Throw(exception!);
        }
    }
}
