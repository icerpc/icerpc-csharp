// Copyright (c) ZeroC, Inc.

// TODO: temporary, for paramref. See #4220.
#pragma warning disable CS1734 // XML comment has a type parameter reference that is not valid.

namespace IceRpc.Internal;

internal static class TaskExtensions
{
    /// <summary>Extension methods for <see cref="Task" />.</summary>
    /// <param name="task">The task that upon completion cancels the linked token.</param>
    extension(Task task)
    {
        /// <summary>Converts this task into a linked cancellation token.</summary>
        /// <param name="token">The source token.</param>
        /// <returns>A cancellation token that is canceled when <paramref name="task" /> completes or
        /// <paramref name="token" /> is canceled.</returns>
        internal CancellationToken AsCancellationToken(CancellationToken token)
        {
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            CancellationToken linkedToken = linkedCts.Token;
            _ = CancelOnCompleteAsync();
            return linkedToken;

            async Task CancelOnCompleteAsync()
            {
                using CancellationTokenSource cts = linkedCts; // takes ownership of linkedCts
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch
                {
                    // It's ok for task to complete with an exception.
                }
                cts.Cancel();
            }
        }
    }
}
