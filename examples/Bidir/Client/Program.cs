using Demo;
using IceRpc;

await using var server = new Server(new AlertRecipient(), "icerpc://127.0.0.1?tls=false");

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
