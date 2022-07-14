// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Tests.Common;

/// <summary>Provides a fake implementation of <see cref="IConnectionContext"/>.</summary>
public sealed class FakeConnectionContext : IConnectionContext
{
    public static IConnectionContext Ice { get; } = new FakeConnectionContext(Protocol.Ice);
    public static IConnectionContext IceRpc { get; } = new FakeConnectionContext(Protocol.IceRpc);

    public IInvoker Invoker => NotImplementedInvoker.Instance;

    public TransportConnectionInformation TransportConnectionInformation => default;

    public Protocol Protocol { get; }

    public static IConnectionContext FromProtocol(Protocol protocol) =>
        protocol == Protocol.Ice ? Ice :
            (protocol == Protocol.IceRpc ? IceRpc : throw new NotSupportedException());

    public void OnAbort(Action<Exception> callback)
    {
    }

    public void OnShutdown(Action<string> callback)
    {
    }

    private FakeConnectionContext(IceRpc.Protocol protocol) => Protocol = protocol;
}
