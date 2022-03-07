// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public async ValueTask<string?> SayHelloAsync(string? greeting, Dispatch dispatch, CancellationToken cancel)
    {
        await Console.Out.WriteLineAsync("Hello World!");
        return $"{greeting}, server!";
    }
}
