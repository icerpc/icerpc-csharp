// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using IceRpc.Tests;

namespace IceRpc.Logger.Tests;

public sealed class LoggerMiddlewareTests
{
    [Test]
    public async Task Log_successful_request()
    {
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        using var loggerFactory = new TestLoggerFactory();
        await using var connection = new Connection("icerpc://127.0.0.1");
        var request = new IncomingRequest(connection)
        {
            Path = "/path",
            Operation = "operation"
        };
        var sut = new LoggerMiddleware(dispatcher, loggerFactory);

        await sut.DispatchAsync(request, default);

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventIds.ReceivedRequest));
        CheckEntryState(entries[0]);
        Assert.That(entries[1].EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventIds.SendingResponse));
        CheckEntryState(entries[1]);
    }

    [Test]
    public async Task Log_failed_request()
    {
        var dispatcher = new InlineDispatcher((request, cancel) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        await using var connection = new Connection("icerpc://127.0.0.1");
        var request = new IncomingRequest(connection)
        {
            Path = "/path",
            Operation = "operation"
        };
        var sut = new LoggerMiddleware(dispatcher, loggerFactory);

        try
        {
            await sut.DispatchAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        List<TestLoggerEntry> entries = loggerFactory.Logger.Entries;
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries[0].EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventIds.ReceivedRequest));
        CheckEntryState(entries[0]);
        Assert.That(entries[1].EventId.Id, Is.EqualTo((int)LoggerMiddlewareEventIds.DispatchException));
        CheckEntryState(entries[1]);
    }

    private static void CheckEntryState(TestLoggerEntry entry)
    {
        Assert.That(entry.State["LocalEndPoint"], Is.EqualTo("undefined"));
        Assert.That(entry.State["RemoteEndPoint"], Is.EqualTo("undefined"));
        Assert.That(entry.State["Operation"], Is.EqualTo("operation"));
        Assert.That(entry.State["Path"], Is.EqualTo("/path"));
    }
}
