// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class AlertSystem : Service, IAlertSystem
{
    public async ValueTask AddObserverAsync(
        AlertObserverProxy observer,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        string didHandle = await observer.AlertAsync(cancellationToken: cancellationToken) ? "did" : "did not";
        Console.WriteLine($"Alert Recipient {didHandle} accept the alert");
    }
}
