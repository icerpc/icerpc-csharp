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
        TestLoggerEntry entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventId.Dispatch));
        Assert.That(entry.State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entry.State["Path"], Is.EqualTo("/path"));
        Assert.That(entry.State["StatusCode"], Is.EqualTo(StatusCode.Success));
        Assert.That(
            entry.State["LocalNetworkAddress"],
            Is.EqualTo(FakeConnectionContext.IceRpc.TransportConnectionInformation.LocalNetworkAddress));
        Assert.That(
            entry.State["RemoteNetworkAddress"],
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

        TestLoggerEntry entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventId.DispatchException));
        Assert.That(entry.State["Path"], Is.EqualTo("/path"));
        Assert.That(entry.State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entry.Exception, Is.InstanceOf<InvalidOperationException>());
    }
}
