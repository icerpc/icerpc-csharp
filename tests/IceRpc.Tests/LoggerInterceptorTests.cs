// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

public sealed class LoggerInterceptorTests
{
    [Test]
    public async Task Log_successful_request()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        using var loggerFactory = new TestLoggerFactory();
        var prx = new Proxy(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(prx) { Operation = "operation" };
        var sut = new LoggerInterceptor(invoker, loggerFactory);

        await sut.InvokeAsync(request, default);

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.SendingRequest));
        CheckEntryState(entries[0]);
        Assert.That(entries[1].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.ReceivedResponse));
        CheckEntryState(entries[1]);
    }

    [Test]
    public async Task Log_failed_request()
    {
        var invoker = new InlineInvoker((request, cancel) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        var prx = new Proxy(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(prx) { Operation = "operation" };
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
        CheckEntryState(entries[0]);
        Assert.That(entries[1].EventId.Id, Is.EqualTo((int)LoggerInterceptorEventIds.InvokeException));
        CheckEntryState(entries[1]);
    }

    private static void CheckEntryState(TestLoggerEntry entry)
    {
        Assert.That(entry.State["LocalEndpoint"], Is.EqualTo("undefined"));
        Assert.That(entry.State["RemoteEndpoint"], Is.EqualTo("undefined"));
        Assert.That(entry.State["Operation"], Is.EqualTo("operation"));
        Assert.That(entry.State["Path"], Is.EqualTo("/path"));
    }
}
