// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for incoming frames.</summary>
    public abstract class IncomingFrame
    {
        /// <summary>The connection that received this frame.</summary>
        public Connection Connection
        {
            get => _connection ?? throw new InvalidOperationException("connection not set");
            set => _connection = value;
        }

        /// <summary>Returns the fields of this frame.</summary>
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Fields { get; init; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The payload of this frame.</summary>
        public PipeReader Payload { get; set; }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public Encoding PayloadEncoding { get; }

        /// <summary>The Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        private Connection? _connection;

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="protocol">The protocol used to receive the frame.</param>
        /// <param name="payload">The payload of the new frame.</param>
        /// <param name="payloadEncoding">The encoding of the payload.</param>
        protected IncomingFrame(Protocol protocol, PipeReader payload, Encoding payloadEncoding)
        {
            Payload = payload;
            PayloadEncoding = payloadEncoding;
            Protocol = protocol;
        }
    }
}
