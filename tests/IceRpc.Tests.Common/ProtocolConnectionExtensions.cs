// Copyright (c) ZeroC, Inc.

namespace IceRpc.Tests.Common;

/// <summary>A class that provide extensions methods for <see cref="IProtocolConnection"/>.</summary>
public static class ProtocolConnectionExtensions
{
    extension(IProtocolConnection connection)
    {
        /// <summary>Shuts down this connection when shutdownRequested completes.</summary>
        public async Task ShutdownWhenRequestedAsync(Task shutdownRequested)
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
}
