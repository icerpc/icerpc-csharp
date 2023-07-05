// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
/// <remarks>The implementation of the <see cref="IDuplexPipe" /> interface must return a <see
/// cref="ReadOnlySequencePipeWriter" /> for the <see cref="IDuplexPipe.Output" />.</remarks>
public interface IMultiplexedStream : IDuplexPipe
{
    /// <summary>Gets the stream ID.</summary>
    /// <value>The stream ID.</value>
    /// <exception cref="InvalidOperationException">Thrown if the stream is not started. Local streams are not started
    /// until data is written. A remote stream is always started.</exception>
    /// <remarks>The stream Id indentifies a stream within a connection, the least significant bit of a stream ID
    /// identifies the initiator of the stream, for client initiated streams this bit is set to 0 and for server
    /// initiated streams is set to 1. The second least significant bit identifies if the stream is bidirectional with
    /// this bit set to 0, or unidirectional with this bit set to 1. Successive streams of each type are created with
    /// numerically increasing stream IDs. This is the same as the stream Id schema used by QUIC.</remarks>
    ulong Id { get; }

    /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
    /// <value><see langword="true" /> if the stream is a bidirectional stream; otherwise, <see langword="false" />.
    /// </value>
    bool IsBidirectional { get; }

    /// <summary>Gets a value indicating whether the stream is remote. A remote stream is a stream initiated by the peer
    /// and it's returned by <see
    /// cref="IMultiplexedConnection.AcceptStreamAsync(CancellationToken)" />.</summary>
    /// <value><see langword="true" /> if the stream is a remote stream; otherwise, <see langword="false" />.</value>
    bool IsRemote { get; }

    /// <summary>Gets a value indicating whether the stream is started.</summary>
    /// <value><see langword="true" /> if the stream is started; otherwise, <see langword="false" />.</value>
    /// <remarks>Remote streams are always started after construction. A local stream is started after the sending of
    /// the first STREAM frame.</remarks>
    bool IsStarted { get; }

    /// <summary>Gets a task that completes when all write network activity ceases for this stream. This occurs when:
    /// <list type="bullet">
    /// <item><description><see cref="PipeWriter.Complete(Exception?)" /> is called on this stream's <see
    /// cref="IDuplexPipe.Output" />.</description></item>
    /// <item><description>the peer calls <see cref="PipeReader.Complete(Exception?)"/> on the stream's <see
    /// cref="IDuplexPipe.Input" />.</description></item>
    /// <item><description>the implementation detects a network failure that prevents further writes on the underlying
    /// network stream.</description></item></list>The task is never faulted or canceled.</summary>
    /// <value>The writes closed task.</value>
    Task WritesClosed { get; }
}
