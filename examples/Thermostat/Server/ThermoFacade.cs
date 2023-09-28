// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Runtime.CompilerServices;

namespace ThermostatServer;

internal class ThermoFacade : Service, IThermostatService
{
    private volatile IThermoControl? _thermoControl;
    private volatile TaskCompletionSource<(Reading, Task)> _tcs = new();

    public async ValueTask ChangeSetPointAsync(
        float setPoint,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        if (_thermoControl is IThermoControl thermoControl)
        {
            // Forwards call to device.
            try
            {
                await thermoControl.ChangeSetPointAsync(setPoint, cancellationToken: cancellationToken);
            }
            catch (DispatchException exception)
            {
                // It could be because the device disconnected, or because the new setPoint is out of range.
                exception.ConvertToInternalError = false;
                throw;
            }
        }
        else
        {
            throw new DispatchException(StatusCode.ApplicationError, "The device is not connected.");
        }
    }

    public ValueTask<IAsyncEnumerable<Reading>> MonitorAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // Multiple MonitorAsync can execute concurrently on behalf of multiple client applications; as a result we
        // can't use a simple single producer / single consumer setup.
        return new(ReadAsync(CancellationToken.None));

        async IAsyncEnumerable<Reading> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Task<(Reading, Task)> task = _tcs.Task;

            while (!cancellationToken.IsCancellationRequested)
            {
                Reading reading;
                try
                {
                    (reading, Task plainTask) = await task.WaitAsync(cancellationToken);
                    task = (Task<(Reading, Task)>)plainTask;
                }
                catch
                {
                    break; // while
                }
                yield return reading;
            }
        }
    }

    internal async Task DeviceConnectedAsync(IThermoControl thermoControl, IAsyncEnumerable<Reading> readings)
    {
        _thermoControl = thermoControl;

        await foreach (Reading reading in readings)
        {
            Console.WriteLine(reading);

            var nextTcs = new TaskCompletionSource<(Reading, Task)>();

            // In the unlikely event we get concurrent calls (= multiple concurrent re-connections), the first call
            // wins.
            if (_tcs.TrySetResult((reading, nextTcs.Task)))
            {
                _tcs = nextTcs;
            }
        }
    }
}
