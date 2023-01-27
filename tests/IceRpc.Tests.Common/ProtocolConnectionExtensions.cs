// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

public static class ProtocolConnectionExtensions
{
    /// <summary>Shuts down this connection when shutdownRequested completes.</summary>
    public static async Task ShutdownWhenRequestedAsync(this IProtocolConnection connection, Task shutdownRequested)
    {
        await shutdownRequested;
        try
        {
            await connection.ShutdownAsync();
        }
        catch
        {
            // ignore all exceptions
        }
        // we leave the DisposeAsync to the test.
    }
}
