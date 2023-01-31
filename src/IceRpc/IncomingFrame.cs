// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Base class for incoming frames.</summary>
public class IncomingFrame
{
    /// <summary>Gets or sets the connection context.</summary>
    public IConnectionContext ConnectionContext { get; set; }

    /// <summary>Gets or sets the payload of this frame.</summary>
    /// <value>The payload of this frame. The default value is an empty <see cref="PipeReader" />.</value>
    public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

    /// <summary>Gets the protocol of this frame.</summary>
    public Protocol Protocol { get; }

    /// <summary>Constructs an incoming frame.</summary>
    /// <param name="protocol">The protocol of this frame.</param>
    /// <param name="connectionContext">The connection context of the connection that received this frame.</param>
    protected IncomingFrame(Protocol protocol, IConnectionContext connectionContext)
    {
        ConnectionContext = connectionContext;
        Protocol = protocol;
    }
}
