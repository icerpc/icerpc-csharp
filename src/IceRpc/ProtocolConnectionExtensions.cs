// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="IProtocolConnection" />.</summary>
public static class ProtocolConnectionExtensions
{
    /// <summary>Shuts down a connection that is still connecting, but first waiting for the connection establishment
    /// to complete. If the connection establishment fails, the shutdown is deemed successful.</summary>
    /// <param name="connection">The connection to shutdown.</param>
    /// <param name="connectTask">The task that represents the connection establishment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the shutdown is completed.</returns>
    public static async Task ShutdownPendingAsync(
        this IProtocolConnection connection,
        Task connectTask,
        CancellationToken cancellationToken)
    {
        // First wait for the ConnectAsync to complete
        try
        {
            await connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch
        {
            // connectTask failed = successful shutdown
            return;
        }

        await connection.ShutdownAsync(cancellationToken).ConfigureAwait(false);
    }
}
