// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IDispatchInformationFeature"/>.</summary>
public sealed class DispatchInformationFeature : IDispatchInformationFeature
{
    /// <inheritdoc/>
    public IConnection Connection { get; }

    /// <inheritdoc/>
    public string Fragment { get; }

    /// <inheritdoc/>
    public bool IsOneway { get; }

    /// <inheritdoc/>
    public string Operation { get; }

    /// <inheritdoc/>
    public string Path { get; }

    /// <summary>Constructs a dispatch information feature using an incoming request.</summary>
    /// <param name="request">The incoming request.</param>
    public DispatchInformationFeature(IncomingRequest request)
    {
        Connection = request.Connection;
        Fragment = request.Fragment;
        IsOneway = request.IsOneway;
        Operation = request.Operation;
        Path = request.Path;
    }
}
