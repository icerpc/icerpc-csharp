// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace IceRpc
{
    /// <summary>The base class for all service proxies. Applications should use proxies through interfaces and rarely
    /// use this class directly.</summary>
    public class ServicePrx : IServicePrx, IEquatable<ServicePrx>
    {
        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public bool CacheConnection { get; set; }

        /// <inheritdoc/>
        public Connection? Connection
        {
            get => _connection;
            set => _connection = value;
        }

        /// <inheritdoc/>
        public Encoding Encoding { get; set; }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public IInvoker? Invoker { get; set; }

        /// <inheritdoc/>
        public string Path { get; } = "";

        /// <inheritdoc/>
        public Protocol Protocol { get; }

        ServicePrx IServicePrx.Impl => this;

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

        /// <summary>The facet path that holds the facet. Used only during marshaling/unmarshaling of ice1 proxies.
        /// </summary>
        internal IList<string> FacetPath { get; set; } = ImmutableList<string>.Empty;

        internal bool IsIndirect => _endpoint?.Transport == Transport.Loc || IsWellKnown;
        internal bool IsWellKnown => Protocol == Protocol.Ice1 && _endpoint == null;

        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile Connection? _connection;

        private Endpoint? _endpoint;

        private Identity _identity = Identity.Empty;

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
        public bool Equals(IServicePrx? other) => Equals(other?.Impl);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as ServicePrx);

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

        /// <inheritdoc/>
        public void IceEncode(IceEncoder iceEncoder)
        {
            if (_connection?.IsServer ?? false)
            {
                throw new InvalidOperationException("cannot marshal a proxy bound to a server connection");
            }

            if (iceEncoder.Encoding == Encoding.V11)
            {
                if (Protocol == Protocol.Ice1)
                {
                    Debug.Assert(Identity.Name.Length > 0);
                    Identity.IceEncode(iceEncoder);
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
                            $"cannot marshal proxy with path '{Path}' using encoding 1.1",
                            ex);
                    }
                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"cannot marshal proxy with path '{Path}' using encoding 1.1");
                    }

                    identity.IceEncode(iceEncoder);
                }

                var proxyData = new ProxyData11(
                    FacetPath,
                    Protocol == Protocol.Ice1 && (Endpoint?.IsDatagram ?? false) ?
                        InvocationMode.Datagram : InvocationMode.Twoway,
                    secure: false,
                    Protocol,
                    protocolMinor: 0,
                    Encoding);
                proxyData.IceEncode(iceEncoder);

                if (IsIndirect)
                {
                    iceEncoder.EncodeSize(0); // 0 endpoints
                    iceEncoder.EncodeString(IsWellKnown ? "" : _endpoint!.Host); // adapter ID unless well-known
                }
                else if (_endpoint == null)
                {
                    iceEncoder.EncodeSize(0); // 0 endpoints
                    iceEncoder.EncodeString(""); // empty adapter ID
                }
                else
                {
                    IEnumerable<Endpoint> endpoints = _endpoint.Transport == Transport.Coloc ?
                        _altEndpoints : Enumerable.Empty<Endpoint>().Append(_endpoint).Concat(_altEndpoints);

                    if (endpoints.Any())
                    {
                        iceEncoder.EncodeSequence(endpoints, (iceEncoder, endpoint) => iceEncoder.EncodeEndpoint11(endpoint));
                    }
                    else // marshaled as an endpointless proxy
                    {
                        iceEncoder.EncodeSize(0); // 0 endpoints
                        iceEncoder.EncodeString(""); // empty adapter ID
                    }
                }
            }
            else
            {
                Debug.Assert(iceEncoder.Encoding == Encoding.V20);
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

                proxyData.IceEncode(iceEncoder);
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

        /// <summary>Constructs a new proxy class instance with the specified path and protocol.</summary>
        /// <param name="path">The proxy path.</param>
        /// <param name="protocol">The proxy protocol.</param>
        protected internal ServicePrx(string path, Protocol protocol)
        {
            Protocol = protocol;
            Internal.UriParser.CheckPath(path, nameof(path));
            Path = path;
            Encoding = protocol.IsSupported() ? protocol.GetEncoding() : Encoding.V20;
        }

        /// <summary>Creates a shallow copy of this service proxy.</summary>
        internal ServicePrx Clone() => (ServicePrx)MemberwiseClone();
    }
}
