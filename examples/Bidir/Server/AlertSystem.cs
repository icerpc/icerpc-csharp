using IceRpc.Slice;

namespace Demo;

public class AlertSystem: Service, IAlertSystem
{
    public async ValueTask AddObserverAsync(AlertRecipientPrx alertRecipient, Dispatch dispatch, CancellationToken cancel)
    {
        System.Threading.Thread.Sleep(3000);
        bool response = await alertRecipient.AlertAsync();
        string didHandle = (response ? "did" : "did not");
        Console.WriteLine($"Alert Recipient {didHandle} accept the alert");
    }
}
