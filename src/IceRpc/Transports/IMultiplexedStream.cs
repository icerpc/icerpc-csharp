// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
public interface IMultiplexedStream : IDuplexPipe
{
    /// <summary>Gets the stream ID.</summary>
    /// <exception cref="InvalidOperationException">Raised if the stream is not started. Local streams are not
    /// started until data is written. A remote stream is always started.</exception>
    long Id { get; }

    /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
    bool IsBidirectional { get; }

    /// <summary>Gets a value indicating whether the stream is remote. A remote stream is a
    /// stream initiated by the peer and it's returned by <see
    /// cref="IMultiplexedNetworkConnection.AcceptStreamAsync(CancellationToken)"/>.</summary>
    bool IsRemote { get; }

    /// <summary>Gets a value indicating whether the stream is started.</summary>
    bool IsStarted { get; }

    /// <summary>Sets the action which is called when the stream is shutdown.</summary>
    /// <param name="action">The callback to register.</param>
    /// <remarks>If the stream is already shutdown, the callback is called synchronously by this method.</remarks>
    void OnShutdown(Action action);

    /// <summary>Sets the action which is called when the peer <see cref="IDuplexPipe.Input"/> completes.</summary>
    /// <param name="action">The callback to register.</param>
    /// <remarks>If the he peer <see cref="IDuplexPipe.Input"/> is already completed, the callback is called
    /// synchronously by this method.</remarks>
    void OnPeerInputCompleted(Action action);

    /// <summary>Shuts down the stream. This will cause the stream <see cref="IDuplexPipe.Input"/> and <see
    /// cref="IDuplexPipe.Output"/> pipe read and write methods to throw the given exception. The shutdown is silent to
    /// stream frames are sent to notify the peer.</summary>
    /// <param name="exception">The completion exception.</param>
    void Shutdown(Exception exception);
}
