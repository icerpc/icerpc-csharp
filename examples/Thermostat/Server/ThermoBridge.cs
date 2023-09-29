// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using Igloo;
using System.Diagnostics;

namespace ThermostatServer;

internal class ThermoBridge : Service, IThermoHomeService
{
    // With a more realistic service, the ThermoBridge singleton would manage multiple devices, each with an associated
    // ThermoFacade instance.
    private readonly ThermoFacade _thermoFacade;

    public ValueTask ReportAsync(
        IAsyncEnumerable<Reading> readings,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        IDispatchInformationFeature? dispatchInfo = features.Get<IDispatchInformationFeature>();
        Debug.Assert(dispatchInfo is not null);

        // Notifies the ThermoFacade its device is connected.
        // We let the async-iteration over readings execute in the background.
        _ = _thermoFacade.DeviceConnectedAsync(
            new ThermoControlProxy(dispatchInfo.ConnectionContext.Invoker),
            readings);

        return default;
    }

    internal ThermoBridge(ThermoFacade thermoFacade) => _thermoFacade = thermoFacade;
}
