// Copyright (c) ZeroC, Inc.

using IceRpc;
using StreamExample;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
var randomGeneratorProxy = new GeneratorProxy(connection);

IAsyncEnumerable<int> randomNumbers = await randomGeneratorProxy.GenerateNumbersAsync();

uint count = 0;
await foreach (int number in randomNumbers)
{
    if (count == 10)
    {
        break;
    }
    else
    {
        Console.WriteLine($"Random number #{count}: {number}");
    }
    ++count;
}

await connection.ShutdownAsync();
