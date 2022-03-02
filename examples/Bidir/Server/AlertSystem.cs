using IceRpc.Slice;

namespace Demo;

public class AlertSystem: Service, IAlertSystem
{
    public async ValueTask AddObserverAsync(AlertRecipientPrx alertRecipient, Dispatch dispatch, CancellationToken cancel)
    {
        await Task.Delay(3000, cancel);
        bool response = await alertRecipient.AlertAsync();
        string didHandle = response ? "did" : "did not";
        Console.WriteLine($"Alert Recipient {didHandle} accept the alert");
    }
}
