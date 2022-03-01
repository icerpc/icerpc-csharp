using IceRpc.Slice;

namespace Demo;

public class AlertSystem: Service, IAlertSystem
{
    public async ValueTask AddObserverAsync(ClientPrx client, Dispatch dispatch, CancellationToken cancel)
    {
        System.Threading.Thread.Sleep(3000);
        bool response = await client.AlertAsync();
        string didHandle = (response ? "did" : "did not");
        Console.WriteLine($"Client {didHandle} accept the alert");
    }
}
