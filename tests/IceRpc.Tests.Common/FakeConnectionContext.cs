// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Tests.Common;

/// <summary>Provides a fake implementation of <see cref="IConnectionContext" />.</summary>
public sealed class FakeConnectionContext : IConnectionContext
{
    /// <summary>The fake connection context singleton instance.</summary>
    public static IConnectionContext Instance { get; } = new FakeConnectionContext();

    /// <inheritdoc/>
    public IInvoker Invoker => NotImplementedInvoker.Instance;

    /// <inheritdoc/>
    public TransportConnectionInformation TransportConnectionInformation =>
        new(IPEndPoint.Parse(LocalAddress), IPEndPoint.Parse(RemoteAddress), null);

    private const string LocalAddress = "192.168.7.7:10000";
    private const string RemoteAddress = "10.10.10.10:11000";

    private FakeConnectionContext()
    {
    }
}
