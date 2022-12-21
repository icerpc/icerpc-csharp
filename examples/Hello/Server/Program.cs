// Copyright (c) ZeroC, Inc. All rights reserved.

using HelloExample;
using IceRpc;

await using var server = new Server(new Hello());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();

// The implementation of CancelKeyPressed.
public static partial class Program
{
    public static Task CancelKeyPressed => _cancelKeyPressedTcs.Task;

    private static readonly TaskCompletionSource _cancelKeyPressedTcs = new();

    static Program() =>
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _ = _cancelKeyPressedTcs.TrySetResult();
        };
}
