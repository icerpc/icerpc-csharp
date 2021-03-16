// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Describes a special endpoint that needs to be resolved with a location resolver. See
    /// <see cref="ILocationResolver"/>.</summary>
    internal sealed class LocEndpoint : Endpoint
    {
        public override string? this[string option] =>
            option == "category" && Protocol == Protocol.Ice1 ?
                (Data.Options.Length > 0 ? Data.Options[0] : null) : base[option];

        protected internal override ushort DefaultPort => DefaultLocPort;
        protected internal override bool HasOptions => Data.Options.Length > 0;

        internal const ushort DefaultLocPort = 0;

        private int _hashCode; // 0 is a special value that means not initialized.

        public override IAcceptor Acceptor(Server server) =>
            throw new InvalidOperationException();

        // There is no Equals as it's identical to the base.

        // Only for caching, same value as base.
        public override int GetHashCode()
        {
            if (_hashCode != 0)
            {
                return _hashCode;
            }
            else
            {
                int hashCode = base.GetHashCode();
                if (hashCode == 0)
                {
                    hashCode = 1;
                }
                _hashCode = hashCode;
                return _hashCode;
            }
        }

        // There is currently no support for server-side loc endpoints
        public override bool IsLocal(Endpoint endpoint) => false;

        public override Connection CreateDatagramServerConnection(Server server) =>
            throw new InvalidOperationException();

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator) =>
            Debug.Assert(false);

        protected internal override Task<Connection> ConnectAsync(
            NonSecure preferNonSecure,
            object? label,
            CancellationToken cancel) =>
            throw new NotSupportedException("cannot create a connection to a loc endpoint");

        protected internal override Endpoint GetPublishedEndpoint(string publishedHost) =>
            throw new NotSupportedException("cannot create published endpoint for a loc endpoint");

        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        internal static LocEndpoint Create(EndpointData data, Communicator communicator, Protocol protocol) =>
            new(data, communicator, protocol);

        // There is no ParseIce1Endpoint: in ice1 string format, loc is never represented as an endpoint.

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Performance",
            "CA1801: Review unused parameters",
            Justification = "Must match signature of Ice2EndpointParser")]
        internal static LocEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> options,
            Communicator communicator,
            bool serverEndpoint)
        {
            Debug.Assert(transport == Transport.Loc);

            if (serverEndpoint)
            {
                throw new NotSupportedException("cannot create a server-side loc endpoint");
            }

            return new(new EndpointData(transport, host, port, Array.Empty<string>()), communicator, Protocol.Ice2);
        }

        // Constructor
        private LocEndpoint(EndpointData data, Communicator communicator, Protocol protocol)
            : base(data, communicator, protocol)
        {
        }
    }
}
