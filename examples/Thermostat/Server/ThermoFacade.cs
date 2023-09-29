// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Runtime.CompilerServices;

namespace ThermostatServer;

internal class ThermoFacade : Service, IThermostatService
{
    private CancellationTokenSource? _cts;

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
            catch (DispatchException exception) when (exception.StatusCode == StatusCode.ApplicationError)
            {
                // It could be because the new setPoint is out of range.
                exception.ConvertToInternalError = false;
                throw;
            }
            catch (ObjectDisposedException)
            {
                // The connection to the device was disposed.
                _thermoControl = null;
                throw new DispatchException(StatusCode.NotFound, "The device is not connected.");
            }
        }
        else
        {
            throw new DispatchException(StatusCode.NotFound, "The device is not connected.");
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

    internal void Cancel() => Interlocked.Exchange(ref _cts, null)?.Cancel();

    internal async Task DeviceConnectedAsync(IThermoControl thermoControl, IAsyncEnumerable<Reading> readings)
    {
        _thermoControl = thermoControl;

        // If we are already reading from a previous connection, we stop reading from it.
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _cts, cts)?.Cancel();

        await foreach (Reading reading in readings.WithCancellation(cts.Token))
        {
            Console.WriteLine(reading);

            var nextTcs = new TaskCompletionSource<(Reading, Task)>();

            // In the unlikely event we have multiple concurrent calls (from different connections) the first call wins.
            if (_tcs.TrySetResult((reading, nextTcs.Task)))
            {
                _tcs = nextTcs;
            }
        }
    }
}
