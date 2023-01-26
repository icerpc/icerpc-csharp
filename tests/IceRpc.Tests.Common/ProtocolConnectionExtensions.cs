// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

public static class ProtocolConnectionExtensions
{
    public static async Task ShutdownWhenAsync(this IProtocolConnection connection, Task shutdownRequested)
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
