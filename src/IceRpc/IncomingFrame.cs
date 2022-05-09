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
        public Protocol Protocol { get; }

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="connection">The <see cref="Connection"/> that received the frame.</param>
        protected IncomingFrame(Connection connection)
        {
            Connection = connection;
            Protocol = connection.Endpoint?.Protocol ??
                throw new ArgumentException(
                    "cannot create an incoming frame from connection with a null Endpoint",
                    nameof(connection));
        }
    }
}
