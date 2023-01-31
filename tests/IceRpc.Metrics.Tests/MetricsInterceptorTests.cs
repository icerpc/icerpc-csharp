// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Metrics.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class MetricsInterceptorTests
{
    [Test]
    public async Task Canceled_invocation_publishes_total_current_and_canceled_measurements()
    {
        const string meterName = "Test.Canceled.IceRpc.Invocation";

        var canceled = new List<long>();
        var current = new List<long>();
        var total = new List<long>();
        using var meterListener = new TestMeterListener<long>(
            meterName,
            (instrument, measurement, tags, state) =>
            {
                switch (instrument.Name)
                {
                    case "canceled-requests":
                    {
                        canceled.Add(measurement);
                        break;
                    }
                    case "current-requests":
                    {
                        current.Add(measurement);
                        break;
                    }
                    case "total-requests":
                    {
                        total.Add(measurement);
                        break;
                    }
                }
            });

        using var invocationMetrics = new InvocationMetrics(meterName);
        var invoker = new InlineInvoker((request, cancellationToken) => throw new OperationCanceledException());
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/" });
        var sut = new MetricsInterceptor(invoker, invocationMetrics);

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(canceled, Is.EqualTo(new long[] { 1 }));
        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task Failed_invocation_publishes_total_current_and_failed_measurements()
    {
        const string meterName = "Test.Failed.IceRpc.Invocation";
        var current = new List<long>();
        var failed = new List<long>();
        var total = new List<long>();
        using var meterListener = new TestMeterListener<long>(
            meterName,
            (instrument, measurement, tags, state) =>
            {
                switch (instrument.Name)
                {
                    case "current-requests":
                    {
                        current.Add(measurement);
                        break;
                    }
                    case "failed-requests":
                    {
                        failed.Add(measurement);
                        break;
                    }
                    case "total-requests":
                    {
                        total.Add(measurement);
                        break;
                    }
                }
            });

        using var invocationMetrics = new InvocationMetrics(meterName);
        var invoker = new InlineInvoker((request, cancellationToken) => throw new InvalidOperationException());
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" });
        var sut = new MetricsInterceptor(invoker, invocationMetrics);

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(failed, Is.EqualTo(new long[] { 1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task Successful_invocation_publishes_total_and_current_measurements()
    {
        const string meterName = "Test.Successful.IceRpc.Invocation";
        var current = new List<long>();
        var total = new List<long>();
        using var meterListener = new TestMeterListener<long>(
            meterName,
            (instrument, measurement, tags, state) =>
            {
                switch (instrument.Name)
                {
                    case "current-requests":
                    {
                        current.Add(measurement);
                        break;
                    }
                    case "total-requests":
                    {
                        total.Add(measurement);
                        break;
                    }
                }
            });

        using var invocationMetrics = new InvocationMetrics(meterName);
        var invoker = new InlineInvoker(
            (request, cancellationToken) => Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" });
        var sut = new MetricsInterceptor(invoker, invocationMetrics);

        await sut.InvokeAsync(request, default);

        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }
}
