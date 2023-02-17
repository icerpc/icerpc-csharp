// Copyright (c) ZeroC, Inc.

using System.Diagnostics.Metrics;

namespace IceRpc.Tests.Common;

/// <summary>A class used by tests to listen to <see cref="Instrument"/> measurement events.</summary>
/// <typeparam name="T">The instrument measurement type.</typeparam>
public sealed class TestMeterListener<T> : IDisposable where T : struct
{
    /// <summary>The meter listener.</summary>
    public MeterListener MeterListener { get; }

    /// <summary>Constructs a meter listener.</summary>
    /// <param name="meterName">The name of the meter to listen.</param>
    /// <param name="measurementCallback">The callback to call on measurement events.</param>
    public TestMeterListener(string meterName, MeasurementCallback<T>? measurementCallback)
    {
        MeterListener = new MeterListener();
        MeterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        MeterListener.SetMeasurementEventCallback(measurementCallback);
        MeterListener.Start();
    }

    /// <inheritdoc/>
    public void Dispose() => MeterListener.Dispose();
}
