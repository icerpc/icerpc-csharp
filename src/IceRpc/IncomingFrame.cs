// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for incoming frames.</summary>
    public abstract class IncomingFrame
    {
        /// <summary>Gets the connection that received this frame.</summary>
        public IConnection Connection { get; }

        /// <summary>Gets or sets the payload of this frame.</summary>
        /// <value>The payload of this frame. The default value is an empty <see cref="PipeReader"/>.</value>
        public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

        /// <summary>Gets the protocol of this frame.</summary>
        public Protocol Protocol => Connection.Protocol;

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="connection">The <see cref="IConnection"/> that received the frame.</param>
        protected IncomingFrame(IConnection connection) => Connection = connection;
    }
}
