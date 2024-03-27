// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Runtime.CompilerServices;

namespace ThermostatDevice;

/// <summary>Implements all the logic of our thermostat device, without any actual electronics or a real AC unit.
/// </summary>
[SliceService]
internal partial class ThermoBot : IThermoControlService
{
    internal Task ReadCompleted => _readTcs.Task;

    private Stage _cooling;

    // Protects all read-write fields.
    private readonly object _mutex = new();

    // Used to signal when the server has stopped reading.
    private readonly TaskCompletionSource _readTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private float _setPoint = 75.0F;
    private float _temperature = 74.0F;

    /// <inheritdoc />
    public ValueTask ChangeSetPointAsync(
        float setPoint,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (setPoint < 65.0F)
            {
                throw new DispatchException(
                    StatusCode.ApplicationError,
                    "Invalid set point: the set point must be greater than or equal to 65°F.");
            }
            _setPoint = setPoint;
            if (_temperature > setPoint)
            {
                _cooling = _temperature - setPoint > 5.0F ? Stage.Stage2 : Stage.Stage1;
            }
            else
            {
                _cooling = Stage.Off;
            }
        }
        return default;
    }

    /// <summary>Generates a new reading every 5 seconds.</summary>
    internal async IAsyncEnumerable<Reading> ProduceReadingsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lock (_mutex)
            {
                if (_cooling != Stage.Off)
                {
                    _temperature -= 0.1F * (byte)_cooling;

                    if (_temperature <= _setPoint)
                    {
                        // A real thermostat would turn off only after running for a while to avoid short-cycling.
                        _cooling = Stage.Off;
                    }
                }
                else
                {
                    // The temperature slowly increases until it hits the set point.
                    _temperature += 0.04F;
                    if (_temperature >= _setPoint)
                    {
                        _cooling = Stage.Stage1;
                    }
                }

                yield return new Reading
                {
                    TimeStamp = DateTime.Now, // encoded as UTC time stamp
                    Temperature = _temperature,
                    Cooling = _cooling,
                    SetPoint = _setPoint
                };
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Server doesn't want more data.
                _readTcs.SetResult();
                yield break;
            }
        }
    }
}
