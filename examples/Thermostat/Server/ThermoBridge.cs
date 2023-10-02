// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Diagnostics;

namespace ThermostatServer;

/// <summary>Implements Slice interface `ThermoHome`.</summary>

internal class ThermoBridge : Service, IThermoHomeService
{
    private readonly DeviceConnection _deviceConnection;
    private readonly ThermoFacade _thermoFacade;

    public ValueTask ReportAsync(
        IAsyncEnumerable<Reading> readings,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        IDispatchInformationFeature? dispatchInfo = features.Get<IDispatchInformationFeature>();
        Debug.Assert(dispatchInfo is not null);
        _deviceConnection.SetInvoker(dispatchInfo.ConnectionContext.Invoker);

        // Notifies the ThermoFacade its device is connected.
        // We let the async-iteration over readings execute in the background. Note that it must complete to get a
        // clean shutdown.
        _ = _thermoFacade.PublishAsync(readings);

        return default;
    }

    internal ThermoBridge(ThermoFacade thermoFacade, DeviceConnection deviceConnection)
    {
        _deviceConnection = deviceConnection;
        _thermoFacade = thermoFacade;
    }
}
