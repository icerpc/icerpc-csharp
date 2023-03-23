// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc;

/// <summary>Base class for outgoing frames.</summary>
public abstract class OutgoingFrame
{
    /// <summary>Gets or sets the payload of this frame.</summary>
    /// <value>The payload of this frame. Defaults to a <see cref="PipeReader" /> that returns an empty
    /// sequence.</value>
    /// <remarks>IceRPC completes the payload <see cref="PipeReader" /> with the <see
    /// cref="PipeReader.Complete(Exception?)" /> method. It never calls <see
    /// cref="PipeReader.CompleteAsync(Exception?)" />. The implementation of <see
    /// cref="PipeReader.Complete(Exception?)" /> should not block.</remarks>
    public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

    /// <summary>Gets or sets the payload continuation of this frame. The payload continuation is a continuation of <see
    /// cref="Payload"/>. The receiver cannot distinguish any seam between payload and payload continuation in the <see
    /// cref="IncomingFrame.Payload" /> it receives.</summary>
    /// <value>The payload continuation of this frame. Defaults to <see langword="null" /> meaning no
    /// continuation.</value>
    /// <remarks>IceRPC completes the payload continuation <see cref="PipeReader" /> with the <see
    /// cref="PipeReader.Complete(Exception?)" /> method. It never calls <see
    /// cref="PipeReader.CompleteAsync(Exception?)" />. The implementation of <see
    /// cref="PipeReader.Complete(Exception?)" /> should not block.</remarks>
    public PipeReader? PayloadContinuation
    {
        get => _payloadContinuation;
        set
        {
            _payloadContinuation = Protocol.SupportsPayloadContinuation || value is null ?
                value : throw new NotSupportedException(
                    $"The '{Protocol}' protocol does not support payload continuation.");
        }
    }

    /// <summary>Gets the protocol of this frame.</summary>
    /// <value>The <see cref="IceRpc.Protocol" /> value of this frame.</value>
    public Protocol Protocol { get; }

    private PipeReader? _payloadContinuation;

    private Stack<Func<PipeWriter, PipeWriter>>? _payloadWriterInterceptorStack;

    /// <summary>Installs a payload writer interceptor in this outgoing frame. This interceptor is executed just
    /// before sending <see cref="Payload" />, and is typically used to compress both <see cref="Payload" /> and
    /// <see cref="PayloadContinuation" />.</summary>
    /// <param name="payloadWriterInterceptor">The payload writer interceptor to install.</param>
    /// <returns>This outgoing frame.</returns>
    /// <remarks>IceRPC completes the payload writer <see cref="PipeWriter" /> with the <see
    /// cref="PipeWriter.Complete(Exception?)" /> method. It never calls <see
    /// cref="PipeWriter.CompleteAsync(Exception?)" />. The implementation of <see
    /// cref="PipeWriter.Complete(Exception?)" /> should not block.</remarks>
    public OutgoingFrame Use(Func<PipeWriter, PipeWriter> payloadWriterInterceptor)
    {
        if (!Protocol.SupportsPayloadWriterInterceptors)
        {
            throw new NotSupportedException(
                $"The '{Protocol}' protocol does not support payload writer interceptors.");
        }
        _payloadWriterInterceptorStack ??= new();
        _payloadWriterInterceptorStack.Push(payloadWriterInterceptor);
        return this;
    }

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

    /// <summary>Constructs an outgoing frame.</summary>
    /// <param name="protocol">The protocol used to send the frame.</param>
    private protected OutgoingFrame(Protocol protocol) => Protocol = protocol;

}
