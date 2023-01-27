// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Metrics;

namespace IceRpc.Tests.Common;

public sealed class TestMeterListener<T> : IDisposable where T : struct
{
    public MeterListener MeterListener { get; }

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

    public void Dispose() => MeterListener.Dispose();
}
