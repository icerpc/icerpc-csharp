// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal static class TaskExtensions
{
    /// <summary>Converts this task into a linked cancellation token.</summary>
    /// <param name="task">The task that upon successful completion cancels the linked token.</param>
    /// <param name="token">The source token.</param>
    /// <returns>A cancellation token that is canceled when <paramref name="task" /> completes successfully or
    /// <paramref name="token" /> is canceled. If the task is canceled or fails, the returned token is not canceled.
    /// </returns>
    internal static CancellationToken AsCancellationToken(this Task task, CancellationToken token)
    {
#pragma warning disable CA2000
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
#pragma warning restore CA2000

        CancellationToken linkedToken = linkedCts.Token;
        _ = Task.Run(CancelOnSuccessAsync, token);
        return linkedToken;

        async Task CancelOnSuccessAsync()
        {
            using CancellationTokenSource cts = linkedCts; // takes ownership of linkedCts
            await task.ConfigureAwait(false);
            cts.Cancel();
        }
    }
}
