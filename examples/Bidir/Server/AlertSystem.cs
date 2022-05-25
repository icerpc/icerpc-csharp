// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class AlertSystem : Service, IAlertSystem
{
    public async ValueTask AddObserverAsync(
        AlertObserverPrx observer,
        IFeatureCollection features,
        CancellationToken cancel)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancel);
        string didHandle = await observer.AlertAsync(cancel: cancel) ? "did" : "did not";
        Console.WriteLine($"Alert Recipient {didHandle} accept the alert");
    }
}
