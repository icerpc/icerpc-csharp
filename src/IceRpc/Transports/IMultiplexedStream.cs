// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
public interface IMultiplexedStream : IDuplexPipe
{
    /// <summary>Gets the stream ID.</summary>
    /// <exception cref="InvalidOperationException">Raised if the stream is not started. Local streams are not
    /// started until data is written. A remote stream is always started.</exception>
    ulong Id { get; }

    /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
    bool IsBidirectional { get; }

    /// <summary>Gets a value indicating whether the stream is remote. A remote stream is a stream initiated by the peer
    /// and it's returned by <see
    /// cref="IMultiplexedConnection.AcceptStreamAsync(CancellationToken)"/>.</summary>
    bool IsRemote { get; }

    /// <summary>Gets a value indicating whether the stream is started.</summary>
    bool IsStarted { get; }

    /// <summary>Gets a task that completes when reads are closed.</summary>
    Task ReadsClosed { get; }

    /// <summary>Gets a task that completes when writes are closed.</summary>
    Task WritesClosed { get; }

    /// <summary>Aborts the stream by completing the <see cref="IDuplexPipe.Input"/> and <see
    /// cref="IDuplexPipe.Output"/> with the given exception.</summary>
    /// <param name="exception">The completion exception.</param>
    void Abort(Exception exception);
}
