// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents an ice or icerpc request frame sent by the application.</summary>
public sealed class OutgoingRequest : OutgoingFrame
{
    /// <summary>Gets or sets the features of this request.</summary>
    public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;

    /// <summary>Gets or sets the fields of this request.</summary>
    public IDictionary<RequestFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>Gets a value indicating whether this request is oneway or two-way.</summary>
    /// <value><see langword="true" /> for oneway requests, <see langword="false" /> otherwise. The default is
    /// <see langword="false" />.</value>
    public bool IsOneway { get; init; }

    /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
    /// <value>The name of the operation. The default is the empty string.</value>
    public string Operation { get; init; } = "";

    /// <summary>Gets the address of the target service.</summary>
    public ServiceAddress ServiceAddress { get; }

    /// <summary>Gets or sets the latest response to this request.</summary>
    /// <remarks>Setting a response completes the previous response when there is one.</remarks>
    internal IncomingResponse? Response
    {
        get => _response;
        set
        {
            _response?.Complete();
            _response = value;
        }
    }

    private IncomingResponse? _response;

    /// <summary>Constructs an outgoing request.</summary>
    /// <param name="serviceAddress">The address of the target service.</param>
    public OutgoingRequest(ServiceAddress serviceAddress)
        : base(serviceAddress.Protocol ??
            throw new ArgumentException(
                "cannot create an outgoing request with a relative service address",
                nameof(serviceAddress))) =>
        ServiceAddress = serviceAddress;

    /// <summary>Completes the payload and payload stream of this request, and the response associated with this
    /// request (if any).</summary>
    /// <param name="exception">The exception that caused this completion.</param>
    public void Complete(Exception? exception = null)
    {
        Payload.Complete(exception);
        PayloadStream?.Complete(exception);
        _response?.Complete(exception);
    }
}
