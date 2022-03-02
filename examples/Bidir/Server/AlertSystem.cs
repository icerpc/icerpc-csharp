// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace Demo;

public class AlertSystem: Service, IAlertSystem
{
    public async ValueTask AddObserverAsync(AlertObserverPrx alertObserver, Dispatch dispatch, CancellationToken cancel)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancel);
        string didHandle = await alertObserver.AlertAsync() ? "did" : "did not";
        Console.WriteLine($"Alert Recipient {didHandle} accept the alert");
    }
}
