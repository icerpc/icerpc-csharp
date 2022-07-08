// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Base class for incoming frames.</summary>
public class IncomingFrame
{
    /// <summary>Gets the connection context.</summary>
    public IConnectionContext ConnectionContext { get; }

    /// <summary>Gets or sets the payload of this frame.</summary>
    /// <value>The payload of this frame. The default value is an empty <see cref="PipeReader"/>.</value>
    public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

    /// <summary>Gets the protocol of this frame.</summary>
    public Protocol Protocol => ConnectionContext.Protocol;

    /// <summary>Constructs an incoming frame.</summary>
    /// <param name="connectionContext">The connection that received this frame.</param>
    protected IncomingFrame(IConnectionContext connectionContext) => ConnectionContext = connectionContext;
}
