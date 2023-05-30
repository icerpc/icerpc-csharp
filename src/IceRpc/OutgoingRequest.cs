// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using System.Collections.Immutable;

namespace IceRpc;

/// <summary>Represents a request frame sent by the application.</summary>
public sealed class OutgoingRequest : OutgoingFrame, IDisposable
{
    /// <summary>Gets or sets the features of this request.</summary>
    /// <value>The <see cref="IFeatureCollection" /> of this request. Defaults to <see
    /// cref="FeatureCollection.Empty" />.</value>
    public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;

    /// <summary>Gets or sets the fields of this request.</summary>
    /// <value>The fields of this request. Defaults to <see cref="ImmutableDictionary{TKey, TValue}.Empty" />.</value>
    public IDictionary<RequestFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<RequestFieldKey, OutgoingFieldValue>.Empty;

    /// <summary>Gets a value indicating whether this request is one-way or two-way.</summary>
    /// <value><see langword="true" /> for one-way requests; otherwise, <see langword="false" />. The default is
    /// <see langword="false" />.</value>
    public bool IsOneway { get; init; }

    /// <summary>Gets or initializes the name of the operation to call on the target service.</summary>
    /// <value>The name of the operation. The default is the empty string.</value>
    public string Operation { get; init; } = "";

    /// <summary>Gets the address of the target service.</summary>
    /// <value>The <see cref="ServiceAddress" /> of this request.</value>
    public ServiceAddress ServiceAddress { get; }

    /// <summary>Gets or sets the latest response for this request.</summary>
    /// <value>The request's latest response or <see langword="null"/> if the response is not set yet.</value>
    /// <remarks>Setting a response completes the previous response when there is one.</remarks>
    internal IncomingResponse? Response
    {
        get => _response;
        set
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _response?.Dispose();
            _response = value;
        }
    }

    // OutgoingRequest is not thread-safe and should not receive a response after it is disposed.
    private bool _isDisposed;

    private IncomingResponse? _response;

    /// <summary>Constructs an outgoing request.</summary>
    /// <param name="serviceAddress">The address of the target service.</param>
    public OutgoingRequest(ServiceAddress serviceAddress)
        : base(serviceAddress.Protocol ??
            throw new ArgumentException(
                "An outgoing request requires a service address with a protocol such as icerpc or ice.",
                nameof(serviceAddress))) =>
        ServiceAddress = serviceAddress;

    /// <summary>Disposes this outgoing request. This completes the payload and payload continuation of this request,
    /// and the response associated with this request (if already received).</summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Payload.Complete();
            PayloadContinuation?.Complete();
            _response?.Dispose();
        }
    }

    /// <summary>Returns a string that represents this outgoing request.</summary>
    /// <returns>A string that represents this outgoing request.</returns>
    public override string ToString() => $"'{Operation}' on '{ServiceAddress}'";
}
