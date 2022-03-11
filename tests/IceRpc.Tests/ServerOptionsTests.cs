// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerOptionsTests
{
    /// <summary>Verifies that a <see cref="ServerOptions"/> instance created with the default constructor has the
    /// expected default values for all its properties.</summary>
    [Test]
    public void Server_options_default_values()
    {
        var options = new ServerOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.AuthenticationOptions, Is.Null);
            Assert.That(options.CloseTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.Dispatcher, Is.EqualTo(ConnectionOptions.DefaultDispatcher));
            Assert.That(options.Endpoint, Is.EqualTo(new Endpoint(Protocol.IceRpc)));
            Assert.That(options.Fields, Is.Empty);
            Assert.That(options.KeepAlive, Is.False);
            Assert.That(options.LoggerFactory, Is.EqualTo(NullLoggerFactory.Instance));
            Assert.That(options.MultiplexedServerTransport, Is.EqualTo(ServerOptions.DefaultMultiplexedServerTransport));
            Assert.That(options.SimpleServerTransport, Is.EqualTo(ServerOptions.DefaultSimpleServerTransport));
        });
    }
}
