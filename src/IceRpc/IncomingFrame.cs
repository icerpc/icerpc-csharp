// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for incoming frames.</summary>
    public abstract class IncomingFrame
    {
        /// <summary>The connection that received this frame.</summary>
        public Connection Connection { get; }

        /// <summary>Gets or sets the payload of this frame.</summary>
        /// <value>The payload of this frame. The default value is an empty <see cref="PipeReader"/>.</value>
        public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

        /// <summary>Returns the protocol of this frame.</summary>
        public Protocol Protocol => Connection.Endpoint.Protocol;

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="connection">The connection used to receive the frame.</param>
        protected IncomingFrame(Connection connection) => Connection = connection;
    }
}
