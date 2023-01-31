// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Tests.Common;

/// <summary>Provides a fake implementation of <see cref="IConnectionContext" />.</summary>
public sealed class FakeConnectionContext : IConnectionContext
{
    public static IConnectionContext Instance { get; } = new FakeConnectionContext();

    public IInvoker Invoker => NotImplementedInvoker.Instance;

    public TransportConnectionInformation TransportConnectionInformation =>
        new(IPEndPoint.Parse(LocalAddress), IPEndPoint.Parse(RemoteAddress), null);

    private const string LocalAddress = "192.168.7.7:10000";
    private const string RemoteAddress = "10.10.10.10:11000";

    private FakeConnectionContext()
    {
    }
}
