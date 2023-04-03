// Copyright (c) ZeroC, Inc.

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
            (request, cancellationToken) => Task.FromResult(
                new IncomingResponse(request, FakeConnectionContext.Instance)));
        using var loggerFactory = new TestLoggerFactory();
        var serviceAddress = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        using var request = new OutgoingRequest(serviceAddress) { Operation = "doIt" };
        var sut = new LoggerInterceptor(invoker, loggerFactory.CreateLogger<LoggerInterceptor>());

        await sut.InvokeAsync(request, default);

        Assert.That(loggerFactory.Logger, Is.Not.Null);
        TestLoggerEntry entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)LoggerInterceptorEventId.Invoke));
        Assert.That(entry.State["ServiceAddress"], Is.EqualTo(serviceAddress));
        Assert.That(entry.State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entry.State["StatusCode"], Is.EqualTo(StatusCode.Success));
        Assert.That(
            entry.State["LocalNetworkAddress"],
            Is.EqualTo(FakeConnectionContext.Instance.TransportConnectionInformation.LocalNetworkAddress));
        Assert.That(
            entry.State["RemoteNetworkAddress"],
            Is.EqualTo(FakeConnectionContext.Instance.TransportConnectionInformation.RemoteNetworkAddress));
    }

    [Test]
    public async Task Log_failed_request()
    {
        var invoker = new InlineInvoker((request, cancellationToken) => throw new InvalidOperationException());
        using var loggerFactory = new TestLoggerFactory();
        var serviceAddress = new ServiceAddress(Protocol.IceRpc) { Path = "/path" };
        using var request = new OutgoingRequest(serviceAddress) { Operation = "doIt" };
        var sut = new LoggerInterceptor(invoker, loggerFactory.CreateLogger<LoggerInterceptor>());

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(loggerFactory.Logger, Is.Not.Null);

        TestLoggerEntry entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)LoggerInterceptorEventId.InvokeException));
        Assert.That(entry.State["ServiceAddress"], Is.EqualTo(serviceAddress));
        Assert.That(entry.State["Operation"], Is.EqualTo("doIt"));
        Assert.That(entry.Exception, Is.InstanceOf<InvalidOperationException>());
    }
}
