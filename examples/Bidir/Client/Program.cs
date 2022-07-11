// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

var options = new ClientConnectionOptions
{
    Dispatcher = new AlertObserver(),
    Endpoint = "icerpc://127.0.0.1",
};

await using var connection = new ClientConnection(options);

var alertSystem = new AlertSystemProxy(connection);
var alertObserver = new AlertObserverProxy("/");

Console.WriteLine("Waiting for Alert ...");
await alertSystem.AddObserverAsync(alertObserver);

// Destroy the client on Ctrl+C or Ctrl+Break
var completionSource = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    completionSource.SetResult();
};

await completionSource.Task;
