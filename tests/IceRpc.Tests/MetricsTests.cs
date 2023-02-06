// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Diagnostics.Metrics;

namespace IceRpc.Tests;

public class MetricsTests
{
    public static IEnumerable<TestCaseData> MetricsTestCases
    {
        get
        {
            string meterName = "IceRpc.Tests.Metrics";

            // Start and connect a connection.
            yield return new TestCaseData(
                meterName,
                (MeterListener listener) =>
                {
                    using var metrics = new Metrics(meterName);
                    metrics.ConnectStart();
                    metrics.ConnectSuccess();
                    metrics.ConnectStop();
                    listener.RecordObservableInstruments();
                },
                (new long[] { 1 }, new long[] { 0 }, new long[] { 1 }, new long[] { 0 }))
                .SetName("Metrics_events(connect_connection))");

            // Start 3 connections one connected, one failed, and one pending.
            yield return new TestCaseData(
                meterName,
                (MeterListener listener) =>
                {
                    using var metrics = new Metrics(meterName);

                    // Connected connection
                    metrics.ConnectStart();
                    metrics.ConnectSuccess();
                    metrics.ConnectStop();

                    // Failed connect connection
                    metrics.ConnectStart();
                    metrics.ConnectionFailure();
                    metrics.ConnectStop();

                    // Pending connect connection
                    metrics.ConnectStart();

                    listener.RecordObservableInstruments();
                },
                (new long[] { 1 }, new long[] { 1 }, new long[] { 3 }, new long[] { 1 }))
                .SetName("Metrics_events(connected_failed_and_pending_connections)");

            // Failed connections
            yield return new TestCaseData(
                meterName,
                (MeterListener listener) =>
                {
                    using var metrics = new Metrics(meterName);

                    // Failed connection
                    metrics.ConnectStart();
                    metrics.ConnectSuccess();
                    metrics.ConnectStop();
                    metrics.ConnectionFailure();
                    metrics.ConnectionDisconnected();

                    listener.RecordObservableInstruments();
                },
                (new long[] { 0 }, new long[] { 0 }, new long[] { 1 }, new long[] { 1 }))
                .SetName("Metrics_events(failed_connection)");
        }
    }

    [Test, TestCaseSource(nameof(MetricsTestCases))]
    public void Metrics_events(
        string meterName,
        Action<MeterListener> metricsCallback,
        (long[] Active, long[] Pending, long[] Total, long[] TotalFailed) expected)
    {

        var active = new List<long>();
        var pending = new List<long>();
        var total = new List<long>();
        var totalFailed = new List<long>();

        using TestMeterListener<long> listener = CreateMeterListener(
            meterName,
            active,
            pending,
            total,
            totalFailed);
        metricsCallback(listener.MeterListener);

        Assert.That(active, Is.EqualTo(expected.Active));
        Assert.That(pending, Is.EqualTo(expected.Pending));
        Assert.That(total, Is.EqualTo(expected.Total));
        Assert.That(totalFailed, Is.EqualTo(expected.TotalFailed));
    }

    private static TestMeterListener<long> CreateMeterListener(
        string meterName,
        List<long> current,
        List<long> pending,
        List<long> total,
        List<long> totalFailed) =>
        new(
            meterName,
            (instrument, measurement, tags, state) =>
            {
                switch (instrument.Name)
                {
                    case "active-connections":
                    {
                        current.Add(measurement);
                        break;
                    }
                    case "pending-connections":
                    {
                        pending.Add(measurement);
                        break;
                    }
                    case "total-connections":
                    {
                        total.Add(measurement);
                        break;
                    }
                    case "total-failed-connections":
                    {
                        totalFailed.Add(measurement);
                        break;
                    }
                }
            });
}
