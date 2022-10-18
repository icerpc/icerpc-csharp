// Copyright (c) ZeroC, Inc. All rights reserved.

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

    /// <summary>Gets a task that completes when reads are closed. Reads are closed when either:
    /// <list type="bullet">
    /// <item><description><see cref="PipeReader.Complete(Exception?)" /> is called on the stream's <see
    /// cref="IDuplexPipe.Input" />.</description></item>
    /// <item><description>the stream's <see cref="IDuplexPipe.Input" /> returns a completed <see cref="ReadResult" />
    /// and all the data is consumed.</description></item>
    /// <item><description>the peer aborts writes by calling <see cref="PipeWriter.Complete(Exception?)" /> with a
    /// non-null exception on the stream's <see cref="IDuplexPipe.Output" />.</description></item>
    /// </list>
    /// <item><description>the stream is aborted with <see cref="Abort" />.</description></item></summary>
    Task ReadsClosed { get; }

    /// <summary>Gets a task that completes when writes are closed. Writes are closed when either:
    /// <list type="bullet">
    /// <item><description><see cref="PipeReader.Complete(Exception?)" /> is called on the stream's <see
    /// cref="IDuplexPipe.Input" />.</description></item>
    /// <item><description>the peer calls <see cref="PipeReader.Complete(Exception?)"/> on the stream's <see
    /// cref="IDuplexPipe.Input" />.</description></item>
    /// <item><description>the stream is aborted with <see cref="Abort" />.</description></item></list></summary>
    Task WritesClosed { get; }

    /// <summary>Aborts the stream. The implementation converts <paramref name="exception"/> into an error code using
    /// the configured <see cref="IMultiplexedStreamErrorCodeConverter" /> and transmits this error code to the remote
    /// peer. Outstanding and subsequent calls to <see cref="PipeReader.ReadAsync(CancellationToken)" />,
    /// <see cref="PipeWriter.WriteAsync" /> and <see cref="PipeWriter.FlushAsync(CancellationToken)" /> on
    /// <see cref="IDuplexPipe.Input" /> and <see cref="IDuplexPipe.Output" /> throw this exception as well.</summary>
    /// <param name="exception">The abort exception.</param>
    /// <remarks><see cref="Abort" /> can be called while other threads are reading or writing
    /// <see cref="IDuplexPipe.Input" /> respectively <see cref="IDuplexPipe.Output" />. <see cref="Abort" /> can be
    /// called multiple times, and even concurrently. Only the first call has an effect.</remarks>
    /// <seealso cref="MultiplexedConnectionOptions" />
    void Abort(Exception exception);
}
