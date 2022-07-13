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
        var proxy = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(proxy) { Operation = "operation" };
        var sut = new LoggerInterceptor(invoker, loggerFactory);

        await sut.InvokeAsync(request, default);

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.SendingRequest));
        CheckRequestEntryState(entries[0]);
        Assert.That(entries[1].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.ReceivedResponse));
        CheckResponseEntryState(entries[1]);
    }

    [Test]
    public async Task Log_failed_request()
    {
        var invoker = new InlineInvoker((request, cancel) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        var proxy = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(proxy) { Operation = "operation" };
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
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.SendingRequest));
        CheckRequestEntryState(entries[0]);
        Assert.That(entries[1].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.InvokeException));
        CheckRequestEntryState(entries[1]);
    }

    private static void CheckRequestEntryState(TestLoggerEntry entry)
    {
        Assert.That(entry.State["Operation"], Is.EqualTo("operation"));
        Assert.That(entry.State["Path"], Is.EqualTo("/path"));
    }

    private static void CheckResponseEntryState(TestLoggerEntry entry)
    {
        Assert.That(entry.State["LocalNetworkAddress"], Is.EqualTo("undefined"));
        Assert.That(entry.State["RemoteNetworkAddress"], Is.EqualTo("undefined"));
        Assert.That(entry.State["Operation"], Is.EqualTo("operation"));
        Assert.That(entry.State["Path"], Is.EqualTo("/path"));
    }
}
