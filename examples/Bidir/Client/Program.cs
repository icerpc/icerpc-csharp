using Demo;
using IceRpc;

await using var connection = new Connection
{
    RemoteEndpoint = "icerpc://127.0.0.1:10000?tls=false",
    Dispatcher = new AlertRecipient()
};

AlertSystemPrx alertSystem = AlertSystemPrx.FromConnection(connection);
AlertRecipientPrx alertRecipient = AlertRecipientPrx.FromPath("/");

await alertSystem.AddObserverAsync(alertRecipient);

// Destroy the server on Ctrl+C or Ctrl+Break
TaskCompletionSource tcs = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    tcs.SetResult();
};

await tcs.Task;
