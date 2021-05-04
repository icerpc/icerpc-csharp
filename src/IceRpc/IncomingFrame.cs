// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>Base class for incoming frames.</summary>
    public abstract class IncomingFrame
    {
        /// <summary>Returns the binary context of this frame.</summary>
        public abstract IReadOnlyDictionary<int, ReadOnlyMemory<byte>> BinaryContext { get; }

        /// <summary>The connection that received this frame.</summary>
        public Connection Connection
        {
            get => _connection ?? throw new InvalidOperationException("connection not set");
            internal set => _connection = value;
        }

        /// <summary>The features of this request.</summary>
        public FeatureCollection Features { get; set; } = new();

        /// <summary>Returns true when the payload is compressed; otherwise, returns false.</summary>
        public bool HasCompressedPayload => PayloadCompressionFormat != CompressionFormat.Decompressed;

        /// <summary>The payload of this frame. The bytes inside the data should not be written to;
        /// they are writable because of the <see cref="System.Net.Sockets.Socket"/> methods for sending.</summary>
        public ArraySegment<byte> Payload { get; set; }

        /// <summary>Returns the payload's compression format.</summary>
        public CompressionFormat PayloadCompressionFormat { get; set; }

        /// <summary>Returns the number of bytes in the payload.</summary>
        /// <remarks>Provided for consistency with <see cref="OutgoingFrame.PayloadSize"/>.</remarks>
        public int PayloadSize => Payload.Count;

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public abstract Encoding PayloadEncoding { get; }

        /// <summary>The Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        private Connection? _connection;

        /// <summary>Constructs a new <see cref="IncomingFrame"/>.</summary>
        /// <param name="protocol">The protocol of this frame.</param>
        protected IncomingFrame(Protocol protocol) => Protocol = protocol;
    }
}
