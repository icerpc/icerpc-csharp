// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents a request frame received by the application.</summary>
public sealed class IncomingRequest : IncomingFrame, IDisposable
{
    /// <summary>Gets or sets the features of this request.</summary>
    public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;

    /// <summary>Gets or sets the fields of this incoming request.</summary>
    public IDictionary<RequestFieldKey, ReadOnlySequence<byte>> Fields { get; set; } =
        ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;

    /// <summary>Gets or initializes the fragment of the target service.</summary>
    /// <value>The fragment of the target service. The default is the empty string, and it is always the empty
    /// string with the icerpc protocol.</value>
    public string Fragment
    {
        get => _fragment;
        init => _fragment = Protocol == Protocol.Ice || value.Length == 0 ? value :
            throw new InvalidOperationException("Cannot create an icerpc request with a non-empty fragment.");
    }

    /// <summary>Gets a value indicating whether this request is oneway or two-way.</summary>
    /// <value><see langword="true" /> for oneway requests, <see langword="false" /> otherwise. The default is
    /// <see langword="false" />.</value>
    public bool IsOneway { get; init; }

    /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
    /// <value>The name of the operation. The default is the empty string.</value>
    public string Operation { get; init; } = "";

    /// <summary>Gets or initializes the path of the target service.</summary>
    /// <value>The path of the target service. The default is <c>/</c>.</value>
    public string Path { get; init; } = "/";

    /// <summary>Gets or sets the latest response to this request.</summary>
    /// <remarks>Setting a response completes the previous response when there is one.</remarks>
    internal OutgoingResponse? Response
    {
        get => _response;
        set
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _response?.Payload.Complete();
            _response?.PayloadContinuation?.Complete();
            _response = value;
        }
    }

    private readonly string _fragment = "";

    // IncomingRequest is not thread-safe and does not accept a response after it is disposed.
    private bool _isDisposed;

    private OutgoingResponse? _response;

    /// <summary>Constructs an incoming request.</summary>
    /// <param name="protocol">The protocol of this request.</param>
    /// <param name="connectionContext">The connection context of the connection that received this request.</param>
    public IncomingRequest(Protocol protocol, IConnectionContext connectionContext)
        : base(protocol, connectionContext)
    {
    }

    /// <summary>Disposes this incoming request. This completes the payload of this request and the payload(s) of the
    /// response associated with this request (if set).</summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Payload.Complete();
            _response?.Payload.Complete();
            _response?.PayloadContinuation?.Complete();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Fragment.Length == 0 ?
        $"'{Operation}' on '{Path}'" :
        $"'{Operation}' on '{Path}#{Fragment}'";
}
