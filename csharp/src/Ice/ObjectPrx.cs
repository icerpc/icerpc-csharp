// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using ZeroC.Ice.Instrumentation;

namespace ZeroC.Ice
{
    /// <summary>The base class for all proxies. It's a publicly visible Ice-internal class. Applications should
    /// not use it directly.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class ObjectPrx : IObjectPrx, IEquatable<ObjectPrx>
    {
        public bool CacheConnection { get; } = true;
        public Communicator Communicator { get; }
        public IReadOnlyDictionary<string, string> Context { get; } = ImmutableDictionary<string, string>.Empty;
        public Encoding Encoding { get; }
        public IReadOnlyList<Endpoint> Endpoints { get; } = ImmutableList<Endpoint>.Empty;
        public string Facet { get; } = "";
        public Identity Identity { get; }

        public IReadOnlyList<InvocationInterceptor> InvocationInterceptors { get; } =
            ImmutableList<InvocationInterceptor>.Empty;

        public TimeSpan InvocationTimeout => _invocationTimeoutOverride ?? Communicator.DefaultInvocationTimeout;
        public bool IsFixed { get; }
        internal bool IsIndirect => Endpoints.Count == 0 && !IsFixed;

        public bool IsOneway { get; }

        public bool IsRelative { get; }

        internal bool IsWellKnown => IsIndirect && Location.Count == 0;
        public object? Label { get; }
        public IReadOnlyList<string> Location { get; } = ImmutableList<string>.Empty;

        public TimeSpan LocatorCacheTimeout => _locatorCacheTimeoutOverride ?? Communicator.DefaultLocatorCacheTimeout;

        public ILocatorPrx? Locator => LocatorInfo?.Locator;

        public bool PreferExistingConnection =>
            _preferExistingConnectionOverride ?? Communicator.DefaultPreferExistingConnection;
        public NonSecure PreferNonSecure => _preferNonSecureOverride ?? Communicator.DefaultPreferNonSecure;
        public Protocol Protocol { get; }

        ObjectPrx IObjectPrx.Impl => this;

        internal LocatorInfo? LocatorInfo { get; }

        // Sub-properties for ice1 proxies
        private static readonly string[] _suffixes =
        {
            "CacheConnection",
            "InvocationTimeout",
            "LocatorCacheTimeout",
            "Locator",
            "PreferNonSecure",
            "Relative",
            "Context\\..*"
        };

        private volatile Connection? _connection;
        private int _hashCode; // cached hash code value

        // The various Override fields override the value from the communicator. When null, they are not included in
        // the ToString()/ToProperty() representation.

        private readonly TimeSpan? _invocationTimeoutOverride;
        private readonly TimeSpan? _locatorCacheTimeoutOverride;

        private readonly bool? _preferExistingConnectionOverride;
        private readonly NonSecure? _preferNonSecureOverride;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(ObjectPrx? lhs, ObjectPrx? rhs)
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
        public static bool operator !=(ObjectPrx? lhs, ObjectPrx? rhs) => !(lhs == rhs);

        /// <summary>Creates a proxy from a string and a communicator.</summary>
        public static T Parse<T>(
            string s,
            Communicator communicator,
            ProxyFactory<T> factory,
            string? propertyPrefix = null)
            where T : class, IObjectPrx
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("empty string is invalid");
            }

            bool? cacheConnection = null;
            IReadOnlyDictionary<string, string>? context = null;
            Encoding encoding;
            IReadOnlyList<Endpoint> endpoints;
            string facet;
            Identity identity;
            TimeSpan? invocationTimeout = null;
            object? label = null;
            IReadOnlyList<string> location;
            bool oneway = false;
            bool? preferExistingConnection = null;
            NonSecure? preferNonSecure = null;
            TimeSpan? locatorCacheTimeout = null;
            LocatorInfo? locatorInfo = null;
            Protocol protocol;
            bool? relative = null;

            if (UriParser.IsProxyUri(proxyString))
            {
                List<string> path;
                UriParser.ProxyOptions proxyOptions;
                (endpoints, path, proxyOptions, facet) = UriParser.ParseProxy(proxyString, communicator);

                protocol = proxyOptions.Protocol ?? Protocol.Ice2;
                Debug.Assert(protocol != Protocol.Ice1); // the URI parsing rejects ice1

                encoding = proxyOptions.Encoding ?? Encoding.V20;

                switch (path.Count)
                {
                    case 0:
                        // TODO: should we add a default identity "Default" or "Root" or "Main"?
                        throw new FormatException($"missing identity in proxy `{proxyString}'");
                    case 1:
                        identity = new Identity(category: "", name: path[0]);
                        location = ImmutableArray<string>.Empty;
                        break;
                    case 2:
                        identity = new Identity(category: path[0], name: path[1]);
                        location = ImmutableArray<string>.Empty;
                        break;
                    default:
                        identity = new Identity(category: path[^2], name: path[^1]);
                        path.RemoveRange(path.Count - 2, 2);
                        location = path;
                        break;
                }

                if (identity.Name.Length == 0)
                {
                    throw new FormatException($"invalid identity with empty name in proxy `{proxyString}'");
                }
                if (location.Any(segment => segment.Length == 0))
                {
                    throw new FormatException($"invalid location with empty segment in proxy `{proxyString}'");
                }

                (cacheConnection,
                 context,
                 invocationTimeout,
                 label,
                 locatorCacheTimeout,
                 preferExistingConnection,
                 preferNonSecure,
                 relative) = proxyOptions;

                if (locatorCacheTimeout != null && communicator.DefaultLocator == null)
                {
                    throw new FormatException("cannot set locator-cache-timeout without a Locator");
                }
            }
            else
            {
                protocol = Protocol.Ice1;
                string location0;

                (identity, facet, encoding, location0, endpoints, oneway) =
                    Ice1Parser.ParseProxy(proxyString, communicator);

                // 0 or 1 segment
                location = location0.Length > 0 ? ImmutableArray.Create(location0) : ImmutableArray<string>.Empty;

                // Override the defaults with the proxy properties if a property prefix is defined.
                if (propertyPrefix != null && propertyPrefix.Length > 0)
                {
                    // Warn about unknown properties.
                    if (communicator.WarnUnknownProperties)
                    {
                        CheckForUnknownProperties(propertyPrefix, communicator);
                    }

                    cacheConnection = communicator.GetPropertyAsBool($"{propertyPrefix}.CacheConnection");

                    string property = $"{propertyPrefix}.Context.";
                    context = communicator.GetProperties(forPrefix: property).
                        ToImmutableDictionary(e => e.Key.Substring(property.Length), e => e.Value);

                    property = $"{propertyPrefix}.InvocationTimeout";
                    invocationTimeout = communicator.GetPropertyAsTimeSpan(property);
                    if (invocationTimeout == TimeSpan.Zero)
                    {
                        throw new InvalidConfigurationException($"{property}: 0 is not a valid value");
                    }

                    label = communicator.GetProperty($"{propertyPrefix}.Label");

                    property = $"{propertyPrefix}.Locator";
                    locatorInfo =
                        communicator.GetLocatorInfo(communicator.GetPropertyAsProxy(property, ILocatorPrx.Factory));

                    if (locatorInfo != null && endpoints.Count > 0)
                    {
                        throw new InvalidConfigurationException($"{property}: cannot set a locator on a direct proxy");
                    }

                    property = $"{propertyPrefix}.LocatorCacheTimeout";
                    locatorCacheTimeout = communicator.GetPropertyAsTimeSpan(property);

                    if (locatorCacheTimeout != null)
                    {
                        if (endpoints.Count > 0)
                        {
                            throw new InvalidConfigurationException($"{property}: proxy has endpoints");
                        }
                        if (locatorInfo == null && communicator.DefaultLocator == null)
                        {
                            throw new InvalidConfigurationException(
                                $"{property}: cannot set locator cache timeout without a Locator");
                        }
                    }

                    preferNonSecure = communicator.GetPropertyAsEnum<NonSecure>($"{propertyPrefix}.PreferNonSecure");
                    relative = communicator.GetPropertyAsBool($"{propertyPrefix}.Relative");

                    if (relative == true && endpoints.Count > 0)
                    {
                        throw new InvalidConfigurationException($"{property}: a direct proxy cannot be relative");
                    }
                }
            }

            var options = new ObjectPrxOptions(
                communicator,
                identity,
                protocol,
                cacheConnection: cacheConnection ?? true,
                context: context,
                encoding: encoding,
                endpoints: endpoints,
                facet: facet,
                invocationTimeout: invocationTimeout,
                location: location,
                locatorCacheTimeout: locatorCacheTimeout,
                locatorInfo: locatorInfo ??
                    (endpoints.Count == 0 ? communicator.GetLocatorInfo(communicator.DefaultLocator) : null),
                oneway: oneway,
                preferExistingConnection: preferExistingConnection,
                preferNonSecure: preferNonSecure,
                relative: relative ?? false);

            return factory(options);
        }

        /// <inheritdoc/>
        public bool Equals(ObjectPrx? other)
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

            if (IsFixed)
            {
                // Compare properties and fields specific to fixed references
                if (_connection != other._connection)
                {
                    return false;
                }
            }
            else
            {
                // Compare properties specific to other kinds of references
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
                if (!Location.SequenceEqual(other.Location))
                {
                    return false;
                }
                if (_locatorCacheTimeoutOverride != other._locatorCacheTimeoutOverride)
                {
                    return false;
                }
                if (LocatorInfo != other.LocatorInfo)
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

            // Compare common properties
            if (!Context.DictionaryEqual(other.Context))
            {
                return false;
            }
            if (Encoding != other.Encoding)
            {
                return false;
            }
            if (Facet != other.Facet)
            {
                return false;
            }
            if (Identity != other.Identity)
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
            if (IsRelative != other.IsRelative)
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
        public bool Equals(IObjectPrx? other) => Equals(other?.Impl);

        /// <inheritdoc/>
        public override bool Equals(object? other) => Equals(other as ObjectPrx);

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
                hash.Add(Facet);
                hash.Add(Identity);
                hash.Add(InvocationInterceptors.GetSequenceHashCode());
                hash.Add(_invocationTimeoutOverride);
                hash.Add(IsFixed);
                hash.Add(IsOneway);
                hash.Add(IsRelative);
                hash.Add(Protocol);

                if (IsFixed)
                {
                    hash.Add(_connection);
                }
                else
                {
                    hash.Add(CacheConnection);
                    hash.Add(Endpoints.GetSequenceHashCode());
                    hash.Add(Label);
                    hash.Add(Location.GetSequenceHashCode());
                    hash.Add(_locatorCacheTimeoutOverride);
                    hash.Add(LocatorInfo);
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

            InvocationMode invocationMode = IsOneway ? InvocationMode.Oneway : InvocationMode.Twoway;
            if (Protocol == Protocol.Ice1 && IsOneway && Endpoints.Count > 0 && Endpoints.All(e => e.IsDatagram))
            {
                invocationMode = InvocationMode.Datagram;
            }

            if (ostr.Encoding == Encoding.V11)
            {
                if (IsRelative)
                {
                    throw new NotSupportedException("cannot marshal a relative proxy with the 1.1 encoding");
                }

                Identity.IceWrite(ostr);
                ostr.WriteProxyData11(Facet, invocationMode, Protocol, Encoding);
                ostr.WriteSequence(Endpoints, (ostr, endpoint) => ostr.WriteEndpoint(endpoint));

                if (Endpoints.Count == 0)
                {
                    // If Location holds more than 1 segment, the extra segments are not marshaled.
                    ostr.WriteString(Location.Count == 0 ? "" : Location[0]);
                }
            }
            else
            {
                Debug.Assert(ostr.Encoding == Encoding.V20);

                ostr.Write(Endpoints.Count > 0 ? ProxyKind20.Direct :
                    IsRelative ? ProxyKind20.IndirectRelative : ProxyKind20.Indirect);

                IReadOnlyList<string> location = Location;
                if (IsRelative && location.Count > 1)
                {
                    // Reduce location to its last segment
                    location = ImmutableArray.Create(location[^1]);
                }

                ostr.WriteProxyData20(Identity, Protocol, Encoding, location, invocationMode, Facet);

                if (Endpoints.Count > 0)
                {
                    ostr.WriteSequence(Endpoints, (ostr, endpoint) => ostr.WriteEndpoint(endpoint));
                }
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

                if (Location.Count > 0)
                {
                    Debug.Assert(Location.Count == 1); // at most 1 segment with ice1

                    sb.Append(" @ ");

                    // If the encoded adapter id string contains characters which the reference parser uses as
                    // separators, then we enclose the adapter id string in quotes.
                    string a = StringUtil.EscapeString(Location[0], Communicator.ToStringMode);
                    if (StringUtil.FindFirstOf(a, " :@") != -1)
                    {
                        sb.Append('"');
                        sb.Append(a);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(a);
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
                string path;
                if (Location.Count > 0)
                {
                    var pathBuilder = new StringBuilder();
                    foreach (string s in Location)
                    {
                        pathBuilder.Append(Uri.EscapeDataString(s));
                        pathBuilder.Append('/');
                    }
                    if (Identity.Category.Length == 0)
                    {
                        pathBuilder.Append('/');
                    }
                    pathBuilder.Append(Identity); // Identity.ToString() escapes the string
                    path = pathBuilder.ToString();
                }
                else
                {
                    path = Identity.ToString();
                }

                var sb = new StringBuilder();
                bool firstOption = true;

                if (Endpoints.Count > 0)
                {
                    // direct proxy using ice+transport scheme
                    Endpoint mainEndpoint = Endpoints[0];
                    sb.AppendEndpoint(mainEndpoint, path);
                    firstOption = !mainEndpoint.HasOptions;
                }
                else
                {
                    sb.Append("ice:");
                    sb.Append(path);
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

                if (_locatorCacheTimeoutOverride is TimeSpan locatorCacheTimeout)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("locator-cache-timeout=");
                    sb.Append(TimeSpanExtensions.ToPropertyValue(locatorCacheTimeout));
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

                if (IsRelative)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("relative=true");
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

                if (Facet.Length > 0)
                {
                    sb.Append('#');
                    sb.Append(Uri.EscapeDataString(Facet));
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

        /// <summary>Constructs a new proxy class instance with the specified options. The options must be validated
        /// by the caller and all dictionaries / lists must be safe to reference as-is.</summary>
        protected internal ObjectPrx(ObjectPrxOptions options)
        {
            CacheConnection = options.CacheConnection;
            Communicator = options.Communicator;
            Context = options.Context;
            Encoding = options.Encoding;
            Endpoints = options.Endpoints;
            Facet = options.Facet;
            Identity = options.Identity;
            InvocationInterceptors = options.InvocationInterceptors;
            IsFixed = options.Connection != null; // auto-computed
            IsOneway = options.IsOneway;
            IsRelative = options.IsRelative;
            Label = options.Label;
            Location = options.Location;
            LocatorInfo = options.LocatorInfo;
            Protocol = options.Protocol;
            _connection = options.Connection;
            _invocationTimeoutOverride = options.InvocationTimeoutOverride;
            _locatorCacheTimeoutOverride = options.LocatorCacheTimeoutOverride;
            _preferExistingConnectionOverride = options.PreferExistingConnectionOverride;
            _preferNonSecureOverride = options.PreferNonSecureOverride;
        }

        /// <summary>Creates a new proxy with the same type as <c>this</c> and with the provided options. Derived
        /// proxy classes must override this method.</summary>
        protected virtual ObjectPrx IceClone(ObjectPrxOptions options) => new(options);

        internal static Task<IncomingResponseFrame> InvokeAsync(
            IObjectPrx proxy,
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
                IObjectPrx proxy,
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
                    ObjectPrx impl = proxy.Impl;
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

        /// <summary>Reads a proxy from the input stream.</summary>
        /// <param name="istr">The input stream to read from.</param>
        /// <param name="factory">The proxy factory.</param>
        /// <returns>The proxy read from the stream (can be null).</returns>
        internal static T? Read<T>(InputStream istr, ProxyFactory<T> factory) where T : class, IObjectPrx
        {
            if (istr.Encoding == Encoding.V11)
            {
                var identity = new Identity(istr);
                if (identity.Name.Length == 0)
                {
                    return null;
                }

                var proxyData = new ProxyData11(istr);

                if (proxyData.FacetPath.Length > 1)
                {
                    throw new InvalidDataException(
                        $"received proxy with {proxyData.FacetPath.Length} elements in its facet path");
                }

                if ((byte)proxyData.Protocol == 0)
                {
                    throw new InvalidDataException("received proxy with protocol set to 0");
                }

                if (proxyData.Protocol != Protocol.Ice1 && proxyData.InvocationMode != InvocationMode.Twoway)
                {
                    throw new InvalidDataException(
                        $"received proxy for protocol {proxyData.Protocol.GetName()} with invocation mode set");
                }

                if (proxyData.ProtocolMinor != 0)
                {
                    throw new InvalidDataException(
                        $"received proxy with invalid protocolMinor value: {proxyData.ProtocolMinor}");
                }

                // The min size for an Endpoint with the 1.1 encoding is: transport (short = 2 bytes) + encapsulation
                // header (6 bytes), for a total of 8 bytes.
                Endpoint[] endpoints =
                    istr.ReadArray(minElementSize: 8, istr => istr.ReadEndpoint(proxyData.Protocol));

                string location0 = endpoints.Length == 0 ? istr.ReadString() : "";

                Communicator communicator = istr.Communicator!;

                var options = new ObjectPrxOptions(
                    communicator,
                    identity,
                    proxyData.Protocol,
                    encoding: proxyData.Encoding,
                    endpoints: endpoints,
                    facet: proxyData.FacetPath.Length == 1 ? proxyData.FacetPath[0] : "",
                    location: location0.Length > 0 ? ImmutableList.Create(location0) : ImmutableList<string>.Empty,
                    locatorInfo: communicator.GetLocatorInfo(communicator.DefaultLocator),
                    oneway: proxyData.InvocationMode != InvocationMode.Twoway);

                return factory(options);
            }
            else
            {
                Debug.Assert(istr.Encoding == Encoding.V20);

                ProxyKind20 proxyKind = istr.ReadProxyKind20();
                if (proxyKind == ProxyKind20.Null)
                {
                    return null;
                }

                var proxyData = new ProxyData20(istr);

                if (proxyData.Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received non-null proxy with empty identity name");
                }

                Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;

                if (proxyData.InvocationMode != null && protocol != Protocol.Ice1)
                {
                    throw new InvalidDataException(
                        $"received proxy for protocol {protocol.GetName()} with invocation mode set");
                }

                if (proxyKind == ProxyKind20.IndirectRelative)
                {
                    if (istr.Connection is Connection connection)
                    {
                        if (connection.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"received a relative proxy with invalid protocol {protocol.GetName()}");
                        }

                        // TODO: location is missing

                        var options = new ObjectPrxOptions(
                            connection.Communicator,
                            proxyData.Identity,
                            protocol,
                            encoding: proxyData.Encoding ?? Encoding.V20,
                            facet: proxyData.Facet ?? "",
                            fixedConnection: connection);

                        return factory(options);
                    }
                    else
                    {
                        ObjectPrx? source = istr.SourceProxy;

                        if (source == null)
                        {
                            throw new InvalidOperationException(
                                "cannot read a relative proxy from InputStream created without a connection or proxy");
                        }

                        if (source.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"received a relative proxy with invalid protocol {protocol.GetName()}");
                        }

                        if (proxyData.Location?.Length > 1)
                        {
                            throw new InvalidDataException($"received a relative proxy with an invalid location");
                        }

                        IReadOnlyList<string> location = source.Location;
                        if (proxyData.Location?.Length == 1)
                        {
                            // Replace the last segment of location
                            if (location.Count == 0)
                            {
                                location = ImmutableArray.Create(proxyData.Location[0]);
                            }
                            else
                            {
                                ImmutableArray<string>.Builder builder =
                                    ImmutableArray.CreateBuilder<string>(location.Count);
                                builder.AddRange(location.SkipLast(1));
                                builder.Add(proxyData.Location[0]);
                                location = builder.ToImmutable();
                            }
                        }

                        return source.Clone(factory,
                                            encoding: proxyData.Encoding ?? Encoding.V20,
                                            facet: proxyData.Facet ?? "",
                                            identity: proxyData.Identity,
                                            location: location);
                    }
                }
                else
                {
                    // The min size for an Endpoint with the 2.0 encoding is: transport (short = 2 bytes) + host name
                    // (min 2 bytes as it cannot be empty) + port number (ushort, 2 bytes) + options (1 byte for empty
                    // sequence), for a total of 7 bytes.
                    IReadOnlyList<Endpoint> endpoints = proxyKind == ProxyKind20.Direct ?
                        istr.ReadArray(minElementSize: 7, istr => istr.ReadEndpoint(protocol)) :
                        ImmutableList<Endpoint>.Empty;

                    Communicator communicator = istr.Communicator!;

                    var options = new ObjectPrxOptions(
                        communicator,
                        proxyData.Identity,
                        protocol,
                        encoding: proxyData.Encoding ?? Encoding.V20,
                        endpoints: endpoints,
                        facet: proxyData.Facet ?? "",
                        location: (IReadOnlyList<string>?)proxyData.Location ?? ImmutableList<string>.Empty,
                        locatorInfo: communicator.GetLocatorInfo(communicator.DefaultLocator),
                        oneway: (proxyData.InvocationMode ?? InvocationMode.Twoway) != InvocationMode.Twoway);

                    return factory(options);
                }
            }
        }

        /// <summary>Creates a new proxy with the same type as this proxy and the provided options.</summary>
        internal ObjectPrx Clone(ObjectPrxOptions options) => IceClone(options);

        /// <summary>Computes the options used by the implementation of Proxy.Clone.</summary>
        internal ObjectPrxOptions CreateCloneOptions(
            bool? cacheConnection = null,
            bool clearLabel = false,
            bool clearLocator = false,
            IReadOnlyDictionary<string, string>? context = null, // can be provided by app, needs to be copied
            Encoding? encoding = null,
            IEnumerable<Endpoint>? endpoints = null, // from app, needs to be copied
            string? facet = null,
            Connection? fixedConnection = null,
            Identity? identity = null,
            string? identityAndFacet = null,
            IEnumerable<InvocationInterceptor>? invocationInterceptors = null, // from app, needs to be copied
            TimeSpan? invocationTimeout = null,
            object? label = null,
            IEnumerable<string>? location = null, // from app, needs to be copied
            ILocatorPrx? locator = null,
            TimeSpan? locatorCacheTimeout = null,
            bool? oneway = null,
            bool? preferExistingConnection = null,
            NonSecure? preferNonSecure = null,
            bool? relative = null)
        {
            if (identityAndFacet != null)
            {
                if (facet != null)
                {
                    throw new ArgumentException($"cannot set both {nameof(identityAndFacet)} and {nameof(facet)}",
                                                nameof(facet));
                }

                if (identity != null)
                {
                    throw new ArgumentException($"cannot set both {nameof(identityAndFacet)} and {nameof(identity)}",
                                                nameof(identity));
                }

                (identity, facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            }

            (IReadOnlyList<Endpoint>? newEndpoints, IReadOnlyList<string>? newLocation, LocatorInfo? locatorInfo) =
                ValidateCloneArgs(cacheConnection,
                                  clearLabel,
                                  clearLocator,
                                  endpoints,
                                  fixedConnection,
                                  invocationTimeout,
                                  label,
                                  location,
                                  locator,
                                  locatorCacheTimeout,
                                  preferExistingConnection,
                                  preferNonSecure,
                                  relative);

            if (IsFixed || fixedConnection != null)
            {
                fixedConnection ??= _connection;
                Debug.Assert(fixedConnection != null);

                return new(Communicator,
                           identity ?? Identity,
                           Protocol,
                           context: context?.ToImmutableSortedDictionary() ?? Context,
                           encoding: encoding ?? Encoding,
                           facet: facet ?? Facet,
                           fixedConnection: fixedConnection,
                           invocationInterceptors: invocationInterceptors?.ToImmutableList() ?? InvocationInterceptors,
                           invocationTimeout: invocationTimeout ?? _invocationTimeoutOverride,
                           oneway: fixedConnection.Endpoint.IsDatagram || (oneway ?? IsOneway));
            }
            else
            {
                return new(Communicator,
                           identity ?? Identity,
                           Protocol,
                           cacheConnection: cacheConnection ?? CacheConnection,
                           context: context?.ToImmutableSortedDictionary() ?? Context,
                           encoding: encoding ?? Encoding,
                           endpoints: newEndpoints,
                           facet: facet ?? Facet,
                           invocationInterceptors: invocationInterceptors?.ToImmutableList() ?? InvocationInterceptors,
                           invocationTimeout: invocationTimeout ?? _invocationTimeoutOverride,
                           label: clearLabel ? null : label ?? Label,
                           location: newLocation ?? Location,
                           locatorCacheTimeout:
                                locatorCacheTimeout ?? (locatorInfo != null ? _locatorCacheTimeoutOverride : null),
                           locatorInfo: locatorInfo, // no fallback otherwise breaks clearLocator
                           oneway: oneway ?? IsOneway,
                           preferExistingConnection: preferExistingConnection ?? _preferExistingConnectionOverride,
                           preferNonSecure: preferNonSecure ?? _preferNonSecureOverride,
                           relative: relative ?? IsRelative);
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
                        connection = await Communicator.ConnectAsync(endpoint,
                                                                     PreferNonSecure,
                                                                     Label,
                                                                     cancel).ConfigureAwait(false);
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

        /// <summary>Provides the implementation of <see cref="Proxy.ToProperty(IObjectPrx, string)"/>.</summary>
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
                if (LocatorInfo != null)
                {
                    Dictionary<string, string> locatorProperties = LocatorInfo.Locator.ToProperty(prefix + ".Locator");
                    foreach (KeyValuePair<string, string> entry in locatorProperties)
                    {
                        properties[entry.Key] = entry.Value;
                    }
                }
                if (_locatorCacheTimeoutOverride is TimeSpan locatorCacheTimeout)
                {
                    properties[$"{prefix}.LocatorCacheTimeout"] = locatorCacheTimeout.ToPropertyValue();
                }
                if (_preferExistingConnectionOverride is bool preferExistingConnection)
                {
                    properties[$"{prefix}.PreferExistingConnection"] = preferExistingConnection ? "true" : "false";
                }
                if (_preferNonSecureOverride is NonSecure preferNonSecure)
                {
                    properties[$"{prefix}.PreferNonSecure"] = preferNonSecure.ToString();
                }
                if (IsRelative)
                {
                    properties[$"{prefix}.Relative"] = "true";
                }
            }
            // else, only a single property in the dictionary

            return properties;
        }

        private static void CheckForUnknownProperties(string prefix, Communicator communicator)
        {
            // Do not warn about unknown properties if Ice prefix, i.e. Ice, Glacier2, etc.
            foreach (string name in PropertyNames.ClassPropertyNames)
            {
                if (prefix.StartsWith($"{name}.", StringComparison.Ordinal))
                {
                    return;
                }
            }

            var unknownProps = new List<string>();
            Dictionary<string, string> props = communicator.GetProperties(forPrefix: $"{prefix}.");
            foreach (string prop in props.Keys)
            {
                bool valid = false;
                for (int i = 0; i < _suffixes.Length; ++i)
                {
                    string pattern = "^" + Regex.Escape(prefix + ".") + _suffixes[i] + "$";
                    if (new Regex(pattern).Match(prop).Success)
                    {
                        valid = true;
                        break;
                    }
                }

                if (!valid)
                {
                    unknownProps.Add(prop);
                }
            }

            if (unknownProps.Count != 0 && communicator.Logger.IsEnabled(LogLevel.Warning))
            {
                communicator.Logger.LogUnknownProxyProperty(prefix, unknownProps);
            }
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

            if (ObjectAdapterRegistry.GetColocatedEndpoint(this) is Endpoint colocatedEndpoint)
            {
                return (new List<Endpoint>() { colocatedEndpoint }, TimeSpan.Zero);
            }

            IReadOnlyList<Endpoint>? endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan endpointsAge = TimeSpan.Zero;

            // Get the proxy's endpoint or query the locator to get endpoints
            if (Endpoints.Count > 0)
            {
                endpoints = Endpoints.ToList();
            }
            else if (LocatorInfo != null)
            {
                (endpoints, endpointsAge) =
                    await LocatorInfo.ResolveIndirectProxyAsync(this, endpointsMaxAge, cancel).ConfigureAwait(false);
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
                                                                     PreferNonSecure,
                                                                     Label,
                                                                     cancel).ConfigureAwait(false);

                        if (CacheConnection)
                        {
                            _connection = connection;
                        }
                    }

                    cancel.ThrowIfCancellationRequested();

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

                    // Wait for the reception of the response.
                    response = await stream.ReceiveResponseFrameAsync(cancel).ConfigureAwait(false);

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
                    if (Communicator.Logger.IsEnabled(LogLevel.Debug))
                    {
                        if (connection != null)
                        {
                            Communicator.Logger.LogRetryRequestInvocation(retryPolicy,
                                                                          attempt,
                                                                          Communicator.InvocationMaxAttempts,
                                                                          exception);
                        }
                        else if (triedAllEndpoints)
                        {
                            Communicator.Logger.LogRetryConnectionEstablishment(retryPolicy,
                                                                                attempt,
                                                                                Communicator.InvocationMaxAttempts,
                                                                                exception);
                        }
                        else
                        {
                            Communicator.Logger.LogRetryConnectionEstablishment(exception);
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

                    // If an indirect reference is using a endpoint from the cache, set endpointsMaxAge to force
                    // a new locator lookup.
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

        private (IReadOnlyList<Endpoint> NewEndpoints, IReadOnlyList<string>? NewLocation, LocatorInfo? LocatorInfo) ValidateCloneArgs(
            bool? cacheConnection,
            bool clearLabel,
            bool clearLocator,
            IEnumerable<Endpoint>? endpoints,
            Connection? fixedConnection,
            TimeSpan? invocationTimeout,
            object? label,
            IEnumerable<string>? location,
            ILocatorPrx? locator,
            TimeSpan? locatorCacheTimeout,
            bool? preferExistingConnection,
            NonSecure? preferNonSecure,
            bool? relative)
        {
            // Check for incompatible arguments
            if (locator != null && clearLocator)
            {
                throw new ArgumentException($"cannot set both {nameof(locator)} and {nameof(clearLocator)}");
            }

            if (invocationTimeout != null && invocationTimeout.Value == TimeSpan.Zero)
            {
                throw new ArgumentException("0 is not a valid value for invocationTimeout", nameof(invocationTimeout));
            }

            if (IsFixed || fixedConnection != null)
            {
                // Make sure that all arguments incompatible with fixed references are null
                if (cacheConnection != null)
                {
                    throw new ArgumentException(
                        "cannot change the connection caching configuration of a fixed proxy",
                        nameof(cacheConnection));
                }
                if (endpoints != null)
                {
                    throw new ArgumentException("cannot change the endpoints of a fixed proxy", nameof(endpoints));
                }
                if (clearLabel)
                {
                    throw new ArgumentException("cannot change the label of a fixed proxy", nameof(clearLabel));
                }
                else if (label != null)
                {
                    throw new ArgumentException("cannot change the label of a fixed proxy", nameof(label));
                }
                if (location != null)
                {
                    throw new ArgumentException("cannot change the location of a fixed proxy", nameof(location));
                }
                if (locator != null)
                {
                    throw new ArgumentException("cannot change the locator of a fixed proxy", nameof(locator));
                }
                else if (clearLocator)
                {
                    throw new ArgumentException("cannot change the locator of a fixed proxy", nameof(clearLocator));
                }
                if (locatorCacheTimeout != null)
                {
                    throw new ArgumentException(
                        "cannot set locator cache timeout on a fixed proxy",
                        nameof(locatorCacheTimeout));
                }
                if (preferExistingConnection != null)
                {
                    throw new ArgumentException(
                        "cannot change the prefer-existing-connection configuration of a fixed proxy",
                        nameof(preferExistingConnection));
                }
                if (preferNonSecure != null)
                {
                    throw new ArgumentException(
                        "cannot change the prefer non-secure configuration of a fixed proxy",
                        nameof(preferNonSecure));
                }
                if (relative ?? false)
                {
                    throw new ArgumentException("cannot convert a fixed proxy into a relative proxy", nameof(relative));
                }
                return (ImmutableList<Endpoint>.Empty, null, null);
            }
            else
            {
                // Non-fixed reference
                if (endpoints?.FirstOrDefault(endpoint => endpoint.Protocol != Protocol) is Endpoint endpoint)
                {
                    throw new ArgumentException($"the protocol of endpoint `{endpoint}' is not {Protocol}",
                                                nameof(endpoints));
                }

                if (location != null && location.Any(segment => segment.Length == 0))
                {
                    throw new ArgumentException($"invalid location `{location}' with an empty segment",
                                                nameof(location));
                }

                if (label != null && clearLabel)
                {
                    throw new ArgumentException($"cannot set both {nameof(label)} and {nameof(clearLabel)}");
                }

                if (locator != null && clearLocator)
                {
                    throw new ArgumentException($"cannot set both {nameof(locator)} and {nameof(clearLocator)}");
                }

                if (locatorCacheTimeout != null &&
                    locatorCacheTimeout < TimeSpan.Zero && locatorCacheTimeout != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentException(
                        $"invalid {nameof(locatorCacheTimeout)}: {locatorCacheTimeout}", nameof(locatorCacheTimeout));
                }

                IReadOnlyList<Endpoint>? newEndpoints = endpoints?.ToImmutableArray();
                IReadOnlyList<string>? newLocation = location?.ToImmutableArray();

                if (Protocol == Protocol.Ice1)
                {
                    if (newLocation?.Count > 0 && newEndpoints?.Count > 0)
                    {
                        throw new ArgumentException(
                            @$"cannot set both a non-empty {nameof(location)} and a non-empty {nameof(endpoints)
                            } on an ice1 proxy",
                            nameof(location));
                    }

                    if (newLocation?.Count > 0)
                    {
                        if (newLocation.Count > 1)
                        {
                            throw new ArgumentException(
                                $"{nameof(location)} is limited to a single segment for ice1 proxies",
                                nameof(location));
                        }
                        newEndpoints = ImmutableArray<Endpoint>.Empty; // make sure the clone's endpoints are empty
                    }
                    else if (newEndpoints?.Count > 0)
                    {
                        newLocation = ImmutableArray<string>.Empty; // make sure the clone's location is empty
                    }
                }

                if (relative ?? IsRelative)
                {
                    if (newEndpoints?.Count > 0)
                    {
                        throw new ArgumentException("a relative proxy cannot have endpoints", nameof(relative));
                    }
                    else
                    {
                        newEndpoints = ImmutableArray<Endpoint>.Empty; // make sure the clone's endpoints are empty
                    }
                }

                newEndpoints ??= Endpoints;

                LocatorInfo? locatorInfo = LocatorInfo;
                if (locator != null)
                {
                    if (newEndpoints.Count > 0)
                    {
                        throw new ArgumentException($"cannot set {nameof(locator)} on a direct proxy",
                                                    nameof(locator));
                    }

                    locatorInfo = Communicator.GetLocatorInfo(locator);
                }
                else if (clearLocator || newEndpoints.Count > 0)
                {
                    locatorInfo = null;
                }

                if (locatorCacheTimeout != null)
                {
                    if (newEndpoints.Count > 0)
                    {
                        throw new ArgumentException($"cannot set {nameof(locatorCacheTimeout)} on a direct proxy",
                                                    nameof(locatorCacheTimeout));
                    }
                    if (locatorInfo == null)
                    {
                        throw new ArgumentException($"cannot set {nameof(locatorCacheTimeout)} without a locator",
                                                    nameof(locatorCacheTimeout));
                    }
                }

                return (newEndpoints, newLocation, locatorInfo);
            }
        }
    }
}
