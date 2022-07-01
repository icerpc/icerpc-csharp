// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents an ice or icerpc request frame sent by the application.</summary>
public sealed class OutgoingRequest : OutgoingFrame
{
    /// <summary>Gets or sets the connection associated with this request. This connection is used by the
    /// <see cref="Proxy.DefaultInvoker"/> to send the request. While this connection is usually the
    /// connection used to send this request and receive the corresponding response, an invoker can use a different
    /// connection without setting this property.</summary>
    /// <value>The connection associated with this request. Its initial value is <see cref="Proxy.Connection"/>.
    /// </value>
    public IConnection? Connection { get; set; }

    /// <summary>Gets or sets the features of this request.</summary>
    public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;

    /// <summary>Gets or sets the fields of this request.</summary>
    public IDictionary<RequestFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>Gets a value indicating whether this request is oneway or two-way.</summary>
    /// <value><c>true</c> for oneway requests, <c>false</c> otherwise. The default is <c>false</c>.</value>
    public bool IsOneway { get; init; }

    /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
    /// <value>The name of the operation. The default is the empty string.</value>
    public string Operation { get; init; } = "";

    /// <summary>Gets the proxy that is sending this request.</summary>
    public Proxy Proxy { get; }

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
    /// <param name="proxy">The <see cref="Proxy"/> used to send the request.</param>
    public OutgoingRequest(Proxy proxy)
        : base(proxy.Protocol ??
            throw new ArgumentException("cannot recreate outgoing request for relative proxy", nameof(proxy)))
    {
        Connection = proxy.Connection;
        Proxy = proxy;
    }

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
