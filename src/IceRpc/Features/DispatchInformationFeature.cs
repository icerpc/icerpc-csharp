// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IDispatchInformationFeature"/>.</summary>
public sealed class DispatchInformationFeature : IDispatchInformationFeature
{
    /// <inheritdoc/>
    public IConnection Connection => _request.Connection;

    /// <inheritdoc/>
    public string Fragment => _request.Fragment;

    /// <inheritdoc/>
    public bool IsOneway => _request.IsOneway;

    /// <inheritdoc/>
    public string Operation => _request.Operation;

    /// <inheritdoc/>
    public string Path => _request.Path;

    private readonly IncomingRequest _request;

    /// <summary>Constructs a dispatch information feature using an incoming request.</summary>
    /// <param name="request">The incoming request.</param>
    public DispatchInformationFeature(IncomingRequest request) => _request = request;
}
