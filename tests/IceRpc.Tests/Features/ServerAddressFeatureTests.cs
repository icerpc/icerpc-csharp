// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Features.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerAddressFeatureTests
{
    [Test]
    public void Main_server_address_becomes_null_when_exclude_and_alt_addresses_is_empty()
    {
        var excludedServerAddresses = new ServerAddress[] { new ServerAddress(new Uri("icerpc://127.0.0.1:10001")) };
        var serviceAddress = new ServiceAddress(new Uri("icerpc://127.0.0.1:10001/hello"));

        var serverAddressFeature = new ServerAddressFeature(serviceAddress, excludedServerAddresses);

        Assert.That(serverAddressFeature.ServerAddress, Is.Null);
        Assert.That(serverAddressFeature.AltServerAddresses, Is.Empty);
        Assert.That(serverAddressFeature.ExcludedServerAddresses, Is.EqualTo(excludedServerAddresses));
    }

    [Test]
    public void First_non_excluded_alt_address_becomes_main_server_addres_when_main_server_address_is_excluded()
    {
        var excludedServerAddresses = new ServerAddress[] { new ServerAddress(new Uri("icerpc://127.0.0.1:10001")) };

        var serviceAddress = new ServiceAddress(new Uri("icerpc://127.0.0.1:10001/hello?alt-server=127.0.0.1:10002"));
        var serverAddressFeature = new ServerAddressFeature(serviceAddress, excludedServerAddresses);

        Assert.That(serverAddressFeature.ServerAddress, Is.EqualTo(new ServerAddress(new Uri("icerpc://127.0.0.1:10002"))));
        Assert.That(serverAddressFeature.AltServerAddresses, Is.Empty);
        Assert.That(serverAddressFeature.ExcludedServerAddresses, Is.EqualTo(excludedServerAddresses));
    }

    [Test]
    public void Excluded_addresses_with_null_server_address()
    {
        var excludedServerAddresses = new ServerAddress[] { new ServerAddress(new Uri("icerpc://127.0.0.1:10001")) };

        var serviceAddress = new ServiceAddress(Protocol.IceRpc)
        {
            Path = "/hello"
        };
        var serverAddressFeature = new ServerAddressFeature(serviceAddress, excludedServerAddresses);

        Assert.That(serverAddressFeature.ServerAddress, Is.Null);
        Assert.That(serverAddressFeature.AltServerAddresses, Is.Empty);
        Assert.That(serverAddressFeature.ExcludedServerAddresses, Is.EqualTo(excludedServerAddresses));
    }
}
