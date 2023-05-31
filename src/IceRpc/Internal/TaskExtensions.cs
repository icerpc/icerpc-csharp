// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal static class TaskExtensions
{
    /// <summary>Converts this task into a cancellation token that is canceled when the task completes successfully or
    /// the cancellation token argument is canceled prior to the task's completion. If the task is canceled or fails,
    /// the returned token is not canceled.</summary>
    internal static CancellationToken AsCancellationToken(this Task task, CancellationToken cancellationToken)
    {
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        _ = CancelOnSuccessAsync();
        return token;

        async Task CancelOnSuccessAsync()
        {
            try
            {
                using CancellationTokenRegistration tokenRegistration = cancellationToken.UnsafeRegister(
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
