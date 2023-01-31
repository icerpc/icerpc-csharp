// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Metrics.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class MetricsMiddlewareTests
{
    [Test]
    public async Task Canceled_dispatch_publishes_total_current_and_canceled_measurements()
    {
        const string meterName = "Test.Canceled.IceRpc.Dispatch";
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

        using var dispatchMetrics = new DispatchMetrics(meterName);
        var dispatcher = new InlineDispatcher((request, cancellationToken) => throw new OperationCanceledException());
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance);
        var sut = new MetricsMiddleware(dispatcher, dispatchMetrics);

        try
        {
            await sut.DispatchAsync(request, default);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(canceled, Is.EqualTo(new long[] { 1 }));
        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task Failed_dispatch_publishes_total_current_and_failed_measurements()
    {
        const string meterName = "Test.Failed.IceRpc.Dispatch";
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

        using var dispatchMetrics = new DispatchMetrics(meterName);
        var dispatcher = new InlineDispatcher((request, cancellationToken) => throw new InvalidOperationException());
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance);
        var sut = new MetricsMiddleware(dispatcher, dispatchMetrics);

        try
        {
            await sut.DispatchAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(failed, Is.EqualTo(new long[] { 1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task Successful_dispatch_publishes_total_and_current_measurements()
    {
        const string meterName = "Test.Successful.IceRpc.Dispatch";
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

        using var dispatchMetrics = new DispatchMetrics(meterName);
        using var dispatcher = new TestDispatcher();
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance);
        var sut = new MetricsMiddleware(dispatcher, dispatchMetrics);

        await sut.DispatchAsync(request, default);

        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }
}
