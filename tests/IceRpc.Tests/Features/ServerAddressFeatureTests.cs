// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Features.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerAddressFeatureTests
{
    [Test]
    public void Main_server_address_becomes_null_when_removed_and_alt_addresses_is_empty()
    {
        var removedServerAddresses = new ServerAddress[] { new ServerAddress(new Uri("icerpc://127.0.0.1:10001")) };
        var serviceAddress = new ServiceAddress(new Uri("icerpc://127.0.0.1:10001/hello"));
        var serverAddressFeature = new ServerAddressFeature(serviceAddress);

        serverAddressFeature.RemoveServerAddress(removedServerAddresses.First());

        Assert.That(serverAddressFeature.ServerAddress, Is.Null);
        Assert.That(serverAddressFeature.AltServerAddresses, Is.Empty);
        Assert.That(serverAddressFeature.RemovedServerAddresses, Is.EqualTo(removedServerAddresses));
    }

    [Test]
    public void First_non_removed_alt_address_becomes_main_server_address_when_main_server_address_is_removed()
    {
        var removedServerAddresses = new ServerAddress[] { new ServerAddress(new Uri("icerpc://127.0.0.1:10001")) };
        var serviceAddress = new ServiceAddress(new Uri("icerpc://127.0.0.1:10001/hello?alt-server=127.0.0.1:10002"));
        var serverAddressFeature = new ServerAddressFeature(serviceAddress);

        serverAddressFeature.RemoveServerAddress(removedServerAddresses.First());

        Assert.That(serverAddressFeature.ServerAddress, Is.EqualTo(new ServerAddress(new Uri("icerpc://127.0.0.1:10002"))));
        Assert.That(serverAddressFeature.AltServerAddresses, Is.Empty);
        Assert.That(serverAddressFeature.RemovedServerAddresses, Is.EqualTo(removedServerAddresses));
    }

    [Test]
    public void Remove_addresses_when_server_address_feature_has_a_null_server_address()
    {
        var removedServerAddresses = new ServerAddress[] { new ServerAddress(new Uri("icerpc://127.0.0.1:10001")) };
        var serviceAddress = new ServiceAddress(Protocol.IceRpc)
        {
            Path = "/hello"
        };
        var serverAddressFeature = new ServerAddressFeature(serviceAddress);

        serverAddressFeature.RemoveServerAddress(removedServerAddresses.First());

        Assert.That(serverAddressFeature.ServerAddress, Is.Null);
        Assert.That(serverAddressFeature.AltServerAddresses, Is.Empty);
        Assert.That(serverAddressFeature.RemovedServerAddresses, Is.EqualTo(removedServerAddresses));
    }
}
