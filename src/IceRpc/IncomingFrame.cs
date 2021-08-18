// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

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
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Fields { get; init; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The payload of this frame.</summary>
        public ReadOnlyMemory<byte> Payload
        {
            get =>
                _payload is ReadOnlyMemory<byte> value ? value : throw new InvalidOperationException("payload not set");

            set
            {
                _payload = value;
                PayloadSize = value.Length;
            }
        }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public Encoding PayloadEncoding { get; init; } = Encoding.Unknown;

        /// <summary>Returns the number of bytes in the payload.</summary>
        public int PayloadSize { get; private set; }

        /// <summary>The Ice protocol of this frame.</summary>
        public Protocol Protocol { get; init; }

        private protected bool IsPayloadSet => _payload != null;

        private Connection? _connection;
        private ReadOnlyMemory<byte>? _payload;

        /// <summary>Retrieves the payload of this frame.</summary>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The payload.</returns>
        public virtual ValueTask<ReadOnlyMemory<byte>> GetPayloadAsync(CancellationToken cancel = default) =>
            IsPayloadSet ? new(Payload) : throw new NotImplementedException();
    }
}
