// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Tests.Common;

/// <summary>Provides a fake implementation of <see cref="IConnectionContext" />.</summary>
public sealed class FakeConnectionContext : IConnectionContext
{
    public static IConnectionContext Ice { get; } = new FakeConnectionContext(Protocol.Ice);
    public static IConnectionContext IceRpc { get; } = new FakeConnectionContext(Protocol.IceRpc);

    public IInvoker Invoker => NotImplementedInvoker.Instance;

    public ServerAddress ServerAddress { get; }

    public TransportConnectionInformation TransportConnectionInformation =>
        new(IPEndPoint.Parse(LocalAddress), IPEndPoint.Parse(RemoteAddress), null);

    private const string LocalAddress = "192.168.7.7:10000";
    private const string RemoteAddress = "10.10.10.10:11000";

    public static IConnectionContext FromProtocol(Protocol protocol) =>
        protocol == Protocol.Ice ? Ice :
            (protocol == Protocol.IceRpc ? IceRpc : throw new NotSupportedException());

    private FakeConnectionContext(Protocol protocol) =>
        ServerAddress = new ServerAddress(new Uri($"{protocol}://{RemoteAddress}"));
}
