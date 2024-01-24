// Copyright (c) ZeroC, Inc.

using IceRpc;
using VisitorCenter;
using ZeroC.Slice; // for the Result<TSuccess, TFailure> generic type

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeter = new GreeterProxy(connection);

string[] names = [ "", "jimmy", "billy bob", "alice", Environment.UserName ];

foreach (string name in names)
{
    Console.Write($"The greeting for '{name}' is ");

    // Use the Dunet-generated MatchXxx methods to process the result and the GreeterError.
    string message = await greeter.GreetAsync(name).MatchAsync(
        success => $"'{success.Value}'",
        failure => failure.Value.MatchAway(
            away => $"Away until {away.Until.ToLocalTime()}",
            () => $"{failure.Value}"));

    Console.WriteLine(message);
}

await connection.ShutdownAsync();
