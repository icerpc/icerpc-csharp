// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ThermostatServer;

/// <summary>Implements Slice interface `Thermostat` by forwarding calls to the device or by returning data collected
/// from the device.</summary>
/// <remarks>Most of the server-side logic is implemented in this class.</remarks>
internal sealed class ThermoFacade : Service, IThermostatService
{
    private readonly LinkedList<ChannelWriter<Reading>> _channelWriters = new();

    private Reading? _latestReading;

    // Protects all read-write fields.
    private readonly object _mutex = new();

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
            // It could be because the new setPoint is out of range.
            exception.ConvertToInternalError = false;
            throw;
        }
    }

    /// <summary>Returns the readings reported by the device.</summary>
    public ValueTask<IAsyncEnumerable<Reading>> MonitorAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // Each call to MonitorAsync gets its own bounded channel with a single element.
        var channel = Channel.CreateBounded<Reading>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        LinkedListNode<ChannelWriter<Reading>> node = AddChannelWriter(channel.Writer);

        return new(ReadAsync(CancellationToken.None));

        // The injected cancellation token is canceled when the client disconnects.
        async IAsyncEnumerable<Reading> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // We stop streaming to the client when the server shuts down.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken, cancellationToken);

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

            RemoveChannelWriter(node);
        }
    }

    internal ThermoFacade(IThermoControl thermoControl, CancellationToken shutdownToken)
    {
        _shutdownToken = shutdownToken;
        _thermoControl = thermoControl;
    }

    internal async Task PublishAsync(IAsyncEnumerable<Reading> readings)
    {
        if (_shutdownToken.IsCancellationRequested)
        {
            // Close the stream.
            _ = readings.GetAsyncEnumerator().DisposeAsync().AsTask();
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

        await foreach (Reading reading in readings.WithCancellation(cancellationToken))
        {
            Console.WriteLine($"Publishing: {reading}");

            lock (_mutex)
            {
                _latestReading = reading;

                foreach (ChannelWriter<Reading> writer in _channelWriters)
                {
                    writer.TryWrite(reading);
                }
            }
        }

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
