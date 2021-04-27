// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>Information about the current method dispatch for servers. Each method on the server has a
    /// Dispatch as a parameter.</summary>
    public sealed class Dispatch
    {
        /// <summary>The binary context carried by the incoming request frame.</summary>
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> BinaryContext => IncomingRequest.BinaryContext;

        /// <summary>The communicator.</summary>
        public Communicator Communicator => Server.Communicator!;

        /// <summary>The <see cref="Connection"/> over which the request was dispatched.</summary>
        public Connection Connection => IncomingRequest.Connection!;

        /// <summary>The request context, as received from the client.</summary>
        public SortedDictionary<string, string> Context => IncomingRequest.Context;

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The Ice client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with ice2 requests but not
        /// with ice1 requests. As a result, the deadline for an ice1 request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline => IncomingRequest.Deadline;

        /// <summary>The encoding used by the request.</summary>
        public Encoding Encoding => IncomingRequest.PayloadEncoding;

        /// <summary><c>True</c> if the operation was marked as idempotent, <c>False</c> otherwise.</summary>
        public bool IsIdempotent => IncomingRequest.IsIdempotent;

        /// <summary><c>True</c> for oneway requests, <c>False</c> otherwise.</summary>
        public bool IsOneway => !IncomingRequest.Stream!.IsBidirectional;

        /// <summary>The operation name.</summary>
        public string Operation => IncomingRequest.Operation;

        /// <summary>The path (percent-escaped).</summary>
        public string Path => IncomingRequest.Path;

        /// <summary>The protocol used by the request.</summary>
        public Protocol Protocol => IncomingRequest.Protocol;

        /// <summary>The server.</summary>
        public Server Server => Connection.Server!;

        /// <summary>The stream ID</summary>
        public long StreamId => IncomingRequest.Stream!.Id;

        /// <summary>The incoming request frame.</summary>
        internal IncomingRequest IncomingRequest { get; }

        public Dispatch(IncomingRequest request) => IncomingRequest = request;
    }
}
