// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Configure;

var options = new ConnectionOptions
{
    Dispatcher = new AlertObserver(),
    RemoteEndpoint = "icerpc://127.0.0.1",
};

await using var connection = new Connection(options);

var alertSystem = AlertSystemPrx.FromConnection(connection);
var alertObserver = AlertObserverPrx.FromPath("/");

Console.WriteLine("Waiting for Alert ...");
await alertSystem.AddObserverAsync(alertObserver);

// Destroy the client on Ctrl+C or Ctrl+Break
var completionSource = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = connection.ShutdownAsync();
};

await connection.ConnectionClosed;
