// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IceRpc.Logger.Tests;

public sealed class LoggerMiddlewareTests
{
    [Test]
    public async Task Log_successful_request()
    {
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        using var loggerFactory = new TestLoggerFactory();
        await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
        using var request = new IncomingRequest(FakeConnectionContext.IceRpc) { Path = "/path", Operation = "doIt" };
        var sut = new LoggerMiddleware(dispatcher, loggerFactory.CreateLogger<LoggerMiddleware>());

        // Act
        await sut.DispatchAsync(request, default);

        // Assert
        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventId.Dispatch));
        Assert.That(entries[0].State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entries[0].State["Path"], Is.EqualTo("/path"));
        Assert.That(entries[0].State["ResultType"], Is.EqualTo(ResultType.Success));
        Assert.That(
            entries[0].State["LocalNetworkAddress"],
            Is.EqualTo(FakeConnectionContext.IceRpc.TransportConnectionInformation.LocalNetworkAddress));
        Assert.That(
            entries[0].State["RemoteNetworkAddress"],
            Is.EqualTo(FakeConnectionContext.IceRpc.TransportConnectionInformation.RemoteNetworkAddress));

    }

    [Test]
    public async Task Log_failed_request()
    {
        var dispatcher = new InlineDispatcher((request, cancellationToken) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
        using var request = new IncomingRequest(FakeConnectionContext.IceRpc) { Path = "/path", Operation = "doIt" };
        var sut = new LoggerMiddleware(dispatcher, loggerFactory.CreateLogger<LoggerMiddleware>());

        try
        {
            await sut.DispatchAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(loggerFactory.Logger, Is.Not.Null);

        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventId.DispatchException));
        Assert.That(entries[0].State["Path"], Is.EqualTo("/path"));
        Assert.That(entries[0].State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entries[0].Exception, Is.InstanceOf<InvalidOperationException>());
    }
}
