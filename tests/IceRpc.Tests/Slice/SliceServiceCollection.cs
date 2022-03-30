// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

public class SliceServiceCollection : ServiceCollection
{
    public SliceServiceCollection()
    {
        this.AddScoped(_ => new Connection(Endpoint.FromString("icerpc://localhost/")));
        this.AddScoped(provider => Proxy.FromConnection(provider.GetRequiredService<Connection>(), "/"));
    }
}

public static class SliceServiceProviderExtensions
{
    public static IncomingResponse CreateIncomingResponseWithPayload(
        this ServiceProvider serviceProvider,
        PipeReader payload) =>
        new(new OutgoingRequest(serviceProvider.GetRequiredService<Proxy>()))
        {
            Connection = serviceProvider.GetRequiredService<Connection>(),
            Payload = payload
        };

    public static IncomingRequest CreateIncomingRequestWithPayload(
        this ServiceProvider serviceProvider,
        PipeReader payload) =>
        new(Protocol.IceRpc)
        {
            Connection = serviceProvider.GetRequiredService<Connection>(),
            Payload = payload
        };
}
