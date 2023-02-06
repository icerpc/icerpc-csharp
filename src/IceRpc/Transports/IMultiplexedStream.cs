// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
public interface IMultiplexedStream : IDuplexPipe
{
    /// <summary>Gets the stream ID.</summary>
    /// <exception cref="InvalidOperationException">Thrown if the stream is not started. Local streams are not started
    /// until data is written. A remote stream is always started.</exception>
    ulong Id { get; }

    /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
    bool IsBidirectional { get; }

    /// <summary>Gets a value indicating whether the stream is remote. A remote stream is a stream initiated by the peer
    /// and it's returned by <see
    /// cref="IMultiplexedConnection.AcceptStreamAsync(CancellationToken)" />.</summary>
    bool IsRemote { get; }

    /// <summary>Gets a value indicating whether the stream is started.</summary>
    bool IsStarted { get; }

    /// <summary>Gets a task that completes when all read network activity ceases for this stream. This occurs when:
    /// <list type="bullet">
    /// <item><description><see cref="PipeReader.Complete(Exception?)" /> is called on this stream's <see
    /// cref="IDuplexPipe.Input" />.</description></item>
    /// <item><description>the implementation detects that the peer wrote an "end stream" to mark a successful
    /// write completion.</description></item>
    /// <item><description>the peer aborts writes by calling <see cref="PipeWriter.Complete(Exception?)" /> with a
    /// non-null exception on the stream's <see cref="IDuplexPipe.Output" />.</description></item>
    /// <item><description>the implementation detects a network failure that prevents further reads on the underlying
    /// network stream.</description></item></list>The task is never faulted or canceled.</summary>
    Task ReadsClosed { get; }

    /// <summary>Gets a task that completes when all write network activity ceases for this stream. This occurs when:
    /// <list type="bullet">
    /// <item><description><see cref="PipeWriter.Complete(Exception?)" /> is called on this stream's <see
    /// cref="IDuplexPipe.Output" />.</description></item>
    /// <item><description>the peer calls <see cref="PipeReader.Complete(Exception?)"/> on the stream's <see
    /// cref="IDuplexPipe.Input" />.</description></item>
    /// <item><description>the implementation detects a network failure that prevents further writes on the underlying
    /// network stream.</description></item></list>The task is never faulted or canceled.</summary>
    Task WritesClosed { get; }
}
