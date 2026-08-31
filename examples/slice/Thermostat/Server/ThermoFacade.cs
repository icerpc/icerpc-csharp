// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using Igloo;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ThermostatServer;

/// <summary>Implements Slice interface `Thermostat` by forwarding calls to the device or by returning data reported
/// by the device.</summary>
/// <remarks>Most of the server-side logic is implemented in this class.</remarks>
[Service]
internal sealed partial class ThermoFacade : IThermostatService
{
    private readonly LinkedList<ChannelWriter<Reading>> _channelWriters = new();

    private Reading? _latestReading;

    // Protects all read-write fields.
    private readonly Lock _mutex = new();

    private CancellationTokenSource? _publishCts;

    private readonly CancellationToken _shutdownToken;

    private readonly IThermoControl _thermoControl;

    /// <summary>Changes the target temperature by forwarding the call to the device.</summary>
    public async ValueTask ChangeSetPointAsync(
        float setPoint,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        try
        {
            await _thermoControl.ChangeSetPointAsync(setPoint, cancellationToken: cancellationToken);
        }
        catch (DispatchException exception) when (exception.StatusCode == StatusCode.ApplicationError)
        {
            // It could be because the new setPoint is out of range. We want to keep this error as-is.
            exception.ConvertToInternalError = false;
            throw;
        }
    }

    /// <summary>Returns the readings reported by the device.</summary>
    public ValueTask<IAsyncEnumerable<Reading>> MonitorAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        return new(ReadAsync(CancellationToken.None));

        // The injected cancellation token is canceled when the client disconnects or stops reading.
        async IAsyncEnumerable<Reading> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // We stop yielding new values when the server shuts down or the client disconnects or stops reading.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken, cancellationToken);

            // Each enumeration gets its own bounded channel holding a single reading: when the client falls behind, the
            // channel keeps only the latest reading and drops the older ones.
            var channel = Channel.CreateBounded<Reading>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

            // Register the writer when the iteration starts and unregister it in the finally block when the iteration
            // ends or the enumerator is disposed.
            LinkedListNode<ChannelWriter<Reading>> node = AddChannelWriter(channel.Writer);

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    Reading reading;
                    try
                    {
                        reading = await channel.Reader.ReadAsync(cts.Token);
                    }
                    catch
                    {
                        break; // while
                    }
                    yield return reading;
                }
            }
            finally
            {
                // The consumer can dispose the enumerator while it's suspended at a yield return; only the
                // finally block runs then.
                RemoveChannelWriter(node);
            }
        }
    }

    internal ThermoFacade(IThermoControl thermoControl, CancellationToken shutdownToken)
    {
        _shutdownToken = shutdownToken;
        _thermoControl = thermoControl;
    }

    /// <summary>Publishes the readings to the clients currently monitoring the device.</summary>
    internal async Task PublishAsync(IAsyncStream<Reading> readings)
    {
        // This method "owns" readings and its underlying multiplexed transport stream.
        using IAsyncStream<Reading> _ = readings;

        if (_shutdownToken.IsCancellationRequested)
        {
            return;
        }

        var publishCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);
        CancellationToken cancellationToken = publishCts.Token;

        CancellationTokenSource? oldPublishCts;
        lock (_mutex)
        {
            oldPublishCts = _publishCts;
            _publishCts = publishCts;
        }

        // Cancel previous publish task.
        if (oldPublishCts is not null)
        {
            oldPublishCts.Cancel();
            oldPublishCts.Dispose();
        }

        try
        {
            await foreach (Reading reading in readings.WithCancellation(cancellationToken))
            {
                Console.WriteLine($"Publishing: {reading}");

                lock (_mutex)
                {
                    _latestReading = reading;

                    foreach (ChannelWriter<Reading> writer in _channelWriters)
                    {
                        // This always succeeds.
                        writer.TryWrite(reading);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown or when superseded by a newer publish task.
        }
        catch (Exception exception)
        {
            // This method's task is not awaited, so we must not let any exception escape. An IceRpcException is
            // expected when the device connection is lost.
            Console.WriteLine($"Stopped publishing readings: {exception.Message}");
        }
        finally
        {
            lock (_mutex)
            {
                // Cleanup unless a new publish task has already disposed publishCts.
                if (_publishCts == publishCts)
                {
                    publishCts.Dispose();
                    _publishCts = null;
                }
            }
        }
    }

    private LinkedListNode<ChannelWriter<Reading>> AddChannelWriter(ChannelWriter<Reading> writer)
    {
        lock (_mutex)
        {
            // We return immediately the latest reading, then wait for the next one.
            if (_latestReading is Reading reading)
            {
                writer.TryWrite(reading);
            }
            return _channelWriters.AddLast(writer);
        }
    }

    private void RemoveChannelWriter(LinkedListNode<ChannelWriter<Reading>> node)
    {
        lock (_mutex)
        {
            _channelWriters.Remove(node);
            node.Value.Complete();
        }
    }
}
