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

        /// <summary>The features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Returns the fields of this frame.</summary>
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Fields { get; init; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The payload of this frame.</summary>
        public PipeReader Payload
        {
            get =>
                _payload is PipeReader value ? value : throw new InvalidOperationException("payload not set");

            set => _payload = value;
        }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public Encoding PayloadEncoding { get; init; } = Encoding.Unknown;

        /// <summary>The Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        private Connection? _connection;
        private PipeReader? _payload;

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="protocol">The protocol used to receive the frame.</param>
        protected IncomingFrame(Protocol protocol) => Protocol = protocol;
    }
}
