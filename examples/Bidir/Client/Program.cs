using Demo;
using IceRpc;
using IceRpc.Configure;

ConnectionOptions options = new ConnectionOptions
{
    Dispatcher = new AlertRecipient(),
    RemoteEndpoint = "icerpc://127.0.0.1?tls=false",
};

await using var connection = new Connection(options);

AlertSystemPrx alertSystem = AlertSystemPrx.FromConnection(connection);
AlertRecipientPrx alertRecipient = AlertRecipientPrx.FromPath("/");

Console.WriteLine("Waiting for Alert ...");
await alertSystem.AddObserverAsync(alertRecipient);

// Destroy the server on Ctrl+C or Ctrl+Break
TaskCompletionSource tcs = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    tcs.SetResult();
};

await tcs.Task;
