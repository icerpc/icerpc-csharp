using Demo;
using IceRpc;

await using var connection = new Connection
{
    RemoteEndpoint = "icerpc://127.0.0.1:10000?tls=false",
    Dispatcher = new Client()
};

AlertSystemPrx alertSystem = AlertSystemPrx.FromConnection(connection);
ClientPrx client = ClientPrx.FromPath("/");

await alertSystem.AddObserverAsync(client);

// Destroy the server on Ctrl+C or Ctrl+Break
TaskCompletionSource tcs = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    tcs.SetResult();
};

await tcs.Task;
