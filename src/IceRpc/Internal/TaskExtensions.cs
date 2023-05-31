// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal static class TaskExtensions
{
    /// <summary>Converts this task into a cancellation token.</summary>
    /// <param name="task">The task to "convert" into a cancellation token.</param>
    /// <param name="token">The token to observe while the task is running.</param>
    /// <returns>A cancellation token that is canceled when <paramref name="task" /> completes successfully or
    /// <paramref name="token" /> is canceled prior to the task's completion. If the task is canceled or fails, the
    /// returned token is not canceled.</returns>
    internal static CancellationToken AsCancellationToken(this Task task, CancellationToken token)
    {
        var cts = new CancellationTokenSource();
        CancellationToken linkedToken = cts.Token;
        _ = CancelOnSuccessAsync();
        return linkedToken;

        async Task CancelOnSuccessAsync()
        {
            try
            {
                using CancellationTokenRegistration tokenRegistration = token.UnsafeRegister(
                    cts => ((CancellationTokenSource)cts!).Cancel(),
                    cts);

                await task.ConfigureAwait(false);
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
