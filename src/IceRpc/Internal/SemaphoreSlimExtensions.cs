// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal;

internal static class SemaphoreSlimExtensions
{
    /// <summary>Acquires a semaphore lock. The acquisition waits to enter the semaphore and returns a lock that will
    /// release the semaphore when disposed.</summary>
    /// <param name="semaphore">The semaphore.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The semaphore lock.</returns>
    internal static async ValueTask<SemaphoreLock> AcquireAsync(
        this SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLock(semaphore);
    }
}

/// <summary>A simple helper for releasing a semaphore.</summary>
internal struct SemaphoreLock : IDisposable
{
    private bool _isDisposed;
    private readonly SemaphoreSlim _semaphore;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _semaphore.Release();
        }
    }

    internal SemaphoreLock(SemaphoreSlim semaphore) => _semaphore = semaphore;
}
