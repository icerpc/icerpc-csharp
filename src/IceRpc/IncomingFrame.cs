// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Base class for incoming frames.</summary>
public class IncomingFrame
{
    /// <summary>Gets or sets the connection context.</summary>
    /// <value>The <see cref="IConnectionContext"/> of this frame.</value>
    public IConnectionContext ConnectionContext { get; set; }

    /// <summary>Gets or sets the payload of this frame.</summary>
    /// <value>The payload of this frame. Defaults to a <see cref="PipeReader" /> that returns an empty
    /// sequence.</value>
    /// <remarks>IceRPC completes the payload <see cref="PipeReader" /> with the <see
    /// cref="PipeReader.Complete(Exception?)" /> method. It never calls <see
    /// cref="PipeReader.CompleteAsync(Exception?)" />. The implementation of <see
    /// cref="PipeReader.Complete(Exception?)" /> should not block.</remarks>
    public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

    /// <summary>Gets the protocol of this frame.</summary>
    /// <value>The <see cref="IceRpc.Protocol" /> of this frame.</value>
    public Protocol Protocol { get; }

    /// <summary>Constructs an incoming frame.</summary>
    /// <param name="protocol">The protocol of this frame.</param>
    /// <param name="connectionContext">The connection context of the connection that received this frame.</param>
    private protected IncomingFrame(Protocol protocol, IConnectionContext connectionContext)
    {
        ConnectionContext = connectionContext;
        Protocol = protocol;
    }
}
