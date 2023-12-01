// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceProviderExtensionsTests
{
    [Test]
    public void Create_protobuf_client_with_no_params()
    {
        var serviceCollection = 
            new ServiceCollection()
                .AddSingleton(InvalidInvoker.Instance)
                .AddSingleton<IMyOperations>(provider => provider.CreateProtobufClient<MyOperationsClient>());

        var provider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var client = (IProtobufClient?)provider.GetService<IMyOperations>();
        Assert.That(client, Is.Not.Null);
        Assert.That(client.Invoker, Is.EqualTo(InvalidInvoker.Instance));
        Assert.That(client.ServiceAddress.Path, Is.EqualTo(MyOperationsClient.DefaultServicePath));
        Assert.That(client.EncodeOptions, Is.Null);
    }

    [Test]
    public void Create_protobuf_client_without_invoker_fails()
    {
        var serviceCollection =
            new ServiceCollection()
                .AddSingleton<IMyOperations>(provider => provider.CreateProtobufClient<MyOperationsClient>());

        var provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        Assert.That(() => provider.GetService<IMyOperations>(), Throws.InvalidOperationException);
    }
}
