// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Metrics;

namespace IceRpc.Tests.Common;

public sealed class TestMeterListener<T> : IDisposable where T : struct
{
    private readonly MeterListener _listener;

    public TestMeterListener(string meterName, MeasurementCallback<T>? measurementCallback)
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback(measurementCallback);
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();
}
