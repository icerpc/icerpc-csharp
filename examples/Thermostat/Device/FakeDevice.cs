// Copyright (c) ZeroC, Inc.

using Abode;
using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Runtime.CompilerServices;

namespace ThermostatDevice;

/// <summary>Represents a service that emulates a thermostat device.</summary>
internal class FakeDevice : Service, IThermostatService
{
    private Reading _current;
    private readonly object _mutex = new();

    public ValueTask<IAsyncEnumerable<Reading>> MonitorAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        return new(ReadAsync(default));

        async IAsyncEnumerable<Reading> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (_mutex)
                {
                    yield return _current;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Client stopped reading
                    yield break;
                }
            }
        }
    }

    public ValueTask ChangeCoolingSetPointAsync(
        float setPoint,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        lock(_mutex)
        {
            if (setPoint < _current.HeatingSetPoint + 3.0)
            {
                throw new DispatchException(
                    StatusCode.ApplicationError,
                    "The cooling set point must be at least 3°F over the heating set point.");
            }
            _current.CoolingSetPoint = setPoint;
            if (_current.Temperature > setPoint)
            {
                _current.CoolStage = _current.Temperature - setPoint > 5.0F ? Stage2 : Stage1;
            }
        }
    }

    public ValueTask ChangeHeatingSetPointAsync(
        float setPoint,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        lock(_mutex)
        {
            if (setPoint >= _current.CoolingSetPoint - 3.0)
            {
                throw new DispatchException(
                    StatusCode.ApplicationError,
                    "The heating set point must be at least 3°F lower than the cooling set point.");
            }
            _current.HeatingSetPoint = setPoint;
            if (_current.Temperature < setPoint)
            {
                _current.HeatStage = setPoint - _current.Temperature > 5.0F ? Stage2 : Stage1;
            }
        }
    }

    internal FakeDevice()
    {
        _current = new Reading
        {
            Temperature = 74.0F,
            HeatStage = null,
            HeatSetPoint = 72.0F,
            CoolStage = null,
            CoolSetPoint = 76.0F
        };

        Task.Run(UpdateTemperatureAsync);

        async Task UpdateTemperatureAsync()
        {
            while(true)
            {
                lock(_mutex)
                {
                    if (_current.CoolStage is Stage coolStage)
                    {
                        _current.Temperature -= coolStage == Stage.Stage2 ? 0.2 : 0.1;

                        if (_currentTemperature <= _current.CoolSetPoint)
                        {
                            _current.CoolStage = null;
                        }
                    }
                    else if (_current.HeatStage is Stage heatStage)
                    {
                        _current.Temperature += heatStage == Stage.Stage2 ? 0.2 : 0.1;

                        if (_current.Temperature >= _current.HeatSetPoint)
                        {
                            _current.HeatStage = null;
                        }
                    }
                    else
                    {
                        // The temperature slowly increases until it hits the cooling set point.
                        _current.Temperature += 0.01F;
                        if (_current.Temperature >= _current.CoolingSetPoint)
                        {
                            _current.CoolStage = Stage.Stage1;
                        }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
