// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Configure;

ConnectionOptions options = new ConnectionOptions
{
    Dispatcher = new AlertObserver(),
    RemoteEndpoint = "icerpc://127.0.0.1",
};

await using var connection = new Connection(options);

AlertSystemPrx alertSystem = AlertSystemPrx.FromConnection(connection);
AlertObserverPrx alertObserver = AlertObserverPrx.FromPath("/");

Console.WriteLine("Waiting for Alert ...");
await alertSystem.AddObserverAsync(alertObserver);

// Destroy the client on Ctrl+C or Ctrl+Break
TaskCompletionSource completionSource = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    completionSource.SetResult();
};

await completionSource.Task;
