// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Logger.Tests;

public sealed class LoggerInterceptorTests
{
    [Test]
    public async Task Log_successful_request()
    {
        var invoker = new InlineInvoker(
            (request, cancel) => Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc)));
        using var loggerFactory = new TestLoggerFactory();
        var serviceAddress = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(serviceAddress) { Operation = "operation" };
        var sut = new LoggerInterceptor(invoker, loggerFactory);

        await sut.InvokeAsync(request, default);

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.Invoke));
            Assert.That(entries[0].State["ServiceAddress"], Is.EqualTo(serviceAddress));
            Assert.That(entries[0].State["Operation"], Is.EqualTo("operation"));
            Assert.That(entries[0].State["IsOneway"], Is.False);
            Assert.That(entries[0].State["ResultType"], Is.EqualTo(ResultType.Success));
            Assert.That(entries[0].State["LocalNetworkAddress"], Is.Null);
            Assert.That(entries[0].State["RemoteNetworkAddress"], Is.Null);
            Assert.That(entries[0].State["Latency"], Is.Not.Null);
        });
    }

    [Test]
    public async Task Log_failed_request()
    {
        var invoker = new InlineInvoker((request, cancel) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        var serviceAddress = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(serviceAddress) { Operation = "operation" };
        var sut = new LoggerInterceptor(invoker, loggerFactory);

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(loggerFactory.Logger, Is.Not.Null);

        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.InvokeException));
            Assert.That(entries[0].State["ServiceAddress"], Is.EqualTo(serviceAddress));
            Assert.That(entries[0].State["Operation"], Is.EqualTo("operation"));
            Assert.That(entries[0].State["IsOneway"], Is.False);
            Assert.That(entries[0].State["Latency"], Is.Not.Null);
            Assert.That(entries[0].Exception, Is.InstanceOf<InvalidOperationException>());
        });
    }
}
