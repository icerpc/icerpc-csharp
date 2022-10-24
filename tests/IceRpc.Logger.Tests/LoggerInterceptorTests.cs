// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IceRpc.Logger.Tests;

public sealed class LoggerInterceptorTests
{
    [Test]
    public async Task Log_successful_request()
    {
        var invoker = new InlineInvoker(
            (request, cancellationToken) => Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc)));
        using var loggerFactory = new TestLoggerFactory();
        var serviceAddress = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(serviceAddress) { Operation = "doIt" };
        var sut = new LoggerInterceptor(invoker, loggerFactory.CreateLogger<LoggerInterceptor>());

        await sut.InvokeAsync(request, default);

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventId.Invoke));
        Assert.That(entries[0].State["ServiceAddress"], Is.EqualTo(serviceAddress));
        Assert.That(entries[0].State["Operation"], Is.EqualTo("doIt"));
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
        var invoker = new InlineInvoker((request, cancellationToken) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        var serviceAddress = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(serviceAddress) { Operation = "doIt" };
        var sut = new LoggerInterceptor(invoker, loggerFactory.CreateLogger<LoggerInterceptor>());

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(loggerFactory.Logger, Is.Not.Null);

        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventId.InvokeException));
        Assert.That(entries[0].State["ServiceAddress"], Is.EqualTo(serviceAddress));
        Assert.That(entries[0].State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entries[0].Exception, Is.InstanceOf<InvalidOperationException>());
    }
}
