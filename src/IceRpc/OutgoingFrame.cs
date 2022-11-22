// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Base class for outgoing frames.</summary>
public abstract class OutgoingFrame
{
    /// <summary>Gets or sets the payload of this frame. The payload is sent together with the frame header and the
    /// sending operation awaits until the payload is fully sent.</summary>
    /// <value>The payload of this frame. The default is an empty pipe reader.</value>
    public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

    /// <summary>Gets or sets the payload continuation of this frame. The payload continuation is sent after the payload, in the
    /// background: the sending operation does not await it.</summary>
    public PipeReader? PayloadContinuation
    {
        get => _payloadContinuation;
        set
        {
            if (!Protocol.HasStreaming && value is not null)
            {
                throw new NotSupportedException($"streaming is not supported with the '{Protocol}' protocol");
            }
            _payloadContinuation = value;
        }
    }

    /// <summary>Gets the protocol of this frame.</summary>
    public Protocol Protocol { get; }

    private PipeReader? _payloadContinuation;

    /// <summary>Installs a payload writer interceptor in this outgoing frame. This interceptor is executed just
    /// before sending <see cref="Payload" />, and is typically used to compress both <see cref="Payload" /> and
    /// <see cref="PayloadContinuation" />.</summary>
    /// <param name="payloadWriterInterceptor">The payload writer interceptor to install.</param>
    /// <returns>This outgoing frame.</returns>
    public OutgoingFrame Use(Func<PipeWriter, PipeWriter> payloadWriterInterceptor)
    {
        _payloadWriterInterceptorStack ??= new();
        _payloadWriterInterceptorStack.Push(payloadWriterInterceptor);
        return this;
    }

    private Stack<Func<PipeWriter, PipeWriter>>? _payloadWriterInterceptorStack;

    /// <summary>Constructs an outgoing frame.</summary>
    /// <param name="protocol">The protocol used to send the frame.</param>
    protected OutgoingFrame(Protocol protocol) => Protocol = protocol;

    /// <summary>Returns the payload writer to use when sending the payload.</summary>
    internal PipeWriter GetPayloadWriter(PipeWriter writer)
    {
        if (_payloadWriterInterceptorStack is not null)
        {
            foreach (Func<PipeWriter, PipeWriter> interceptor in _payloadWriterInterceptorStack)
            {
                writer = interceptor(writer);
            }
        }
        return writer;
    }
}
