// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Diagnostics;

namespace ThermostatServer;

/// <summary>Implements Slice interface `ThermoHome`.</summary>
[SliceService]
internal partial class ThermoBridge : IThermoHomeService
{
    private readonly DeviceConnection _deviceConnection;
    private readonly ThermoFacade _thermoFacade;

    public ValueTask ReportAsync(
        IAsyncEnumerable<Reading> readings,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        IDispatchInformationFeature? dispatchInfo = features.Get<IDispatchInformationFeature>();

        // We installed the DispatchInformation middleware in Router to get this dispatch info.
        Debug.Assert(dispatchInfo is not null);

        // The device connected or reconnected and we give the latest invoker to the DeviceConnection. This allows the
        // thermoFacade to forward changeSetPoint calls over the correct device->server connection.
        _deviceConnection.SetInvoker(dispatchInfo.ConnectionContext.Invoker);

        // Notify the ThermoFacade its device is connected. We let the async-iteration over readings execute in the
        // background.
        _ = _thermoFacade.PublishAsync(readings);

        return default;
    }

    internal ThermoBridge(ThermoFacade thermoFacade, DeviceConnection deviceConnection)
    {
        _deviceConnection = deviceConnection;
        _thermoFacade = thermoFacade;
    }
}
