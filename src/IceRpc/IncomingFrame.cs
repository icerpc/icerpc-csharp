// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Base class for incoming frames.</summary>
    public abstract class IncomingFrame
    {
        /// <summary>The connection that received this frame.</summary>
        public Connection Connection
        {
            get => _connection ?? throw new InvalidOperationException("connection not set");
            internal set => _connection = value;
        }

        /// <summary>The features of this request.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Returns the fields of this frame.</summary>
        public abstract IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Fields { get; }

        /// <summary>The payload of this frame. The bytes inside the data should not be written to; they are writable
        /// because of the <see cref="System.Net.Sockets.Socket"/> methods for sending.</summary>
        /// <value>The payload array segment. If the payload was never set, an empty array segment is returned.</value>
        public ArraySegment<byte> Payload
        {
            get =>
                _payload is ArraySegment<byte> value ? value : throw new InvalidOperationException("payload not set");
            set
            {
                _payload = value;
                PayloadSize = value.Count;
            }
        }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public abstract Encoding PayloadEncoding { get; private protected set; }

        /// <summary>Returns the number of bytes in the payload.</summary>
        public int PayloadSize { get; private protected set; }

        /// <summary>The Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        private protected bool IsPayloadSet => _payload != null;

        private Connection? _connection;
        private ArraySegment<byte>? _payload;

        /// <summary>Retrieves the payload of this frame.</summary>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The payload.</returns>
        public virtual ValueTask<ArraySegment<byte>> GetPayloadAsync(CancellationToken cancel = default) =>
            IsPayloadSet ? new(Payload) : throw new NotImplementedException();

        /// <summary>Constructs a new <see cref="IncomingFrame"/>.</summary>
        /// <param name="protocol">The protocol of this frame.</param>
        protected IncomingFrame(Protocol protocol) => Protocol = protocol;
    }
}
