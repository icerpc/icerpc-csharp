// Copyright (c) ZeroC, Inc.

using IceRpc;
using StreamExample;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://localhost"));
var generatorProxy = new GeneratorProxy(connection);

IAsyncEnumerable<int> numbers = await generatorProxy.GenerateNumbersAsync();

uint count = 0;
await foreach (int number in numbers)
{
    if (count == 10)
    {
        break;
    }
    else
    {
        Console.WriteLine($"Number #{count}: {number}");
    }
    ++count;
}

await connection.ShutdownAsync();
