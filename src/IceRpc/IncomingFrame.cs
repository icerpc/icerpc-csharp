// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
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

        /// <summary>Gets or initializes the fields of this frame.</summary>
        /// <value>The fields of this frame.</value>
        public IDictionary<int, ReadOnlySequence<byte>> Fields { get; init; } =
            ImmutableDictionary<int, ReadOnlySequence<byte>>.Empty;

        /// <summary>Gets or sets the payload of this frame.</summary>
        /// <value>The payload of this frame. Its default is an empty <see cref="PipeReader"/>.</value>
        public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

        /// <summary>Returns the protocol of this frame.</summary>
        public Protocol Protocol { get; }

        private Connection? _connection;

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="protocol">The protocol used to receive the frame.</param>
        protected IncomingFrame(Protocol protocol) => Protocol = protocol;

        /// <summary>Completes the frame payload pipe reader.</summary>
        internal ValueTask CompleteAsync(Exception? exception = null) => Payload.CompleteAsync(exception);
    }
}
