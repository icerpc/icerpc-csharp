// Copyright (c) ZeroC, Inc.

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
        using var cts = new CancellationTokenSource();
        var invoker = new InlineInvoker((request, cancellationToken) =>
            throw new OperationCanceledException(cancellationToken));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/" });
        var sut = new MetricsInterceptor(invoker, invocationMetrics);
        cts.Cancel();

        try
        {
            await sut.InvokeAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(canceled, Is.EqualTo(new long[] { 1 }));
        Assert.That(current, Is.EqualTo(new long[] { 1, -1 }));
        Assert.That(total, Is.EqualTo(new long[] { 1 }));
    }

    /// <summary>Verifies that an OperationCanceledException carrying a token other than the invocation token is
    /// counted as a failure, not a cancellation.</summary>
    [Test]
    public async Task Operation_canceled_exception_with_unrelated_token_publishes_failed_measurement()
    {
        const string meterName = "Test.UnrelatedOce.IceRpc.Invocation";
        var canceled = new List<long>();
        var current = new List<long>();
        var failed = new List<long>();
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
        using var unrelatedCts = new CancellationTokenSource();
        unrelatedCts.Cancel();
        var invoker = new InlineInvoker((request, cancellationToken) =>
            throw new OperationCanceledException(unrelatedCts.Token));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/" });
        var sut = new MetricsInterceptor(invoker, invocationMetrics);

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await sut.InvokeAsync(request, default));

        Assert.That(canceled, Is.Empty);
        Assert.That(failed, Is.EqualTo(new long[] { 1 }));
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

    /// <summary>Verifies that a non-OK IncomingResponse is counted as a failure, matching the throw-based path.
    /// </summary>
    [Test]
    public async Task Non_ok_response_publishes_total_current_and_failed_measurements()
    {
        const string meterName = "Test.NonOkResponse.IceRpc.Invocation";
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
        var invoker = new InlineInvoker((request, cancellationToken) => Task.FromResult(
            new IncomingResponse(request, FakeConnectionContext.Instance, StatusCode.NotFound, errorMessage: "not found")));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc) { Path = "/path" });
        var sut = new MetricsInterceptor(invoker, invocationMetrics);

        IncomingResponse response = await sut.InvokeAsync(request, default);

        Assert.That(response.StatusCode, Is.EqualTo(StatusCode.NotFound));
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
