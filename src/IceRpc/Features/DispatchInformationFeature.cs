// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IDispatchInformationFeature"/>.</summary>
public sealed class DispatchInformationFeature : IDispatchInformationFeature
{
    /// <inheritdoc/>
    public string Fragment { get; }

    /// <inheritdoc/>
    public IInvoker Invoker { get; }

    /// <inheritdoc/>
    public bool IsOneway { get; }

    /// <inheritdoc/>
    public NetworkConnectionInformation NetworkConnectionInformation { get; }

    /// <inheritdoc/>
    public string Operation { get; }

    /// <inheritdoc/>
    public string Path { get; }

    /// <inheritdoc/>
    public Protocol Protocol { get; }

    /// <summary>Constructs a dispatch information feature using an incoming request.</summary>
    /// <param name="request">The incoming request.</param>
    public DispatchInformationFeature(IncomingRequest request)
    {
        Fragment = request.Fragment;
        Invoker = request.Invoker;
        IsOneway = request.IsOneway;
        NetworkConnectionInformation = request.NetworkConnectionInformation;
        Operation = request.Operation;
        Path = request.Path;
        Protocol = request.Protocol;
    }
}
