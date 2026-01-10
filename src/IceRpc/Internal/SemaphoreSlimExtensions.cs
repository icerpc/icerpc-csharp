// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal static class SemaphoreSlimExtensions
{
    /// <summary>Extension methods for <see cref="SemaphoreSlim" />.</summary>
    /// <param name="semaphore">The semaphore.</param>
    extension(SemaphoreSlim semaphore)
    {
        /// <summary>Acquires a semaphore lock. The acquisition waits to enter the semaphore and returns a lock
        /// that will release the semaphore when disposed.</summary>
        /// <returns>The semaphore lock.</returns>
        internal SemaphoreLock Acquire()
        {
            semaphore.Wait();
            return new SemaphoreLock(semaphore);
        }

        /// <summary>Acquires a semaphore lock. The acquisition waits to enter the semaphore and returns a lock
        /// that will release the semaphore when disposed.</summary>
        /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The semaphore lock.</returns>
        internal async ValueTask<SemaphoreLock> AcquireAsync(
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SemaphoreLock(semaphore);
        }
    }
}

/// <summary>A simple helper for releasing a semaphore.</summary>
/// <remarks>The caller must be extremely careful to call Dispose at most once.</remarks>
internal readonly struct SemaphoreLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public void Dispose() => _semaphore.Release();

    internal SemaphoreLock(SemaphoreSlim semaphore) => _semaphore = semaphore;
}
