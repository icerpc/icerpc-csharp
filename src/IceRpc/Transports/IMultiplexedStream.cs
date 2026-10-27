// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
/// <remarks><para>A bidirectional stream provides both <see cref="Input" /> and <see cref="Output" />, while a
/// unidirectional stream provides only an input (remote stream) or an output (local stream); getting the other one
/// throws <see cref="InvalidOperationException" />.</para>
/// <para>The owner of this stream must always complete the input and output. Until then, the stream can hold onto
/// transport resources and count against the connection's stream limits. Closing or disposing the connection fails
/// the pending and future read and write operations of the stream and completes <see cref="WritesClosed" />.</para>
/// </remarks>
public interface IMultiplexedStream
{
    /// <summary>Gets the stream ID.</summary>
    /// <value>The stream ID.</value>
    /// <exception cref="InvalidOperationException">Thrown when the stream is not started. A remote stream is always
    /// started; depending on the transport implementation, a local stream is started at construction or by the first
    /// write.</exception>
    /// <remarks>The stream IDs have the same format as the QUIC stream IDs
    /// <see href="https://datatracker.ietf.org/doc/html/rfc9000#name-stream-types-and-identifier"/>.</remarks>
    ulong Id { get; }

    /// <summary>Gets the input of this stream, which reads the bytes written by the peer.</summary>
    /// <value>The input of this stream.</value>
    /// <exception cref="InvalidOperationException">Thrown when the stream is a local unidirectional stream.
    /// </exception>
    /// <remarks>IceRPC completes the input with <see cref="PipeReader.Complete(Exception?)" />, never with <see
    /// cref="PipeReader.CompleteAsync(Exception?)" />; the implementation of <see
    /// cref="PipeReader.Complete(Exception?)" /> must not block.</remarks>
    PipeReader Input { get; }

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
    /// <remarks>Remote streams are always started after construction. Depending on the transport implementation, a
    /// local stream is started at construction or by the first write.</remarks>
    bool IsStarted { get; }

    /// <summary>Gets the output of this stream, which writes bytes to the peer.</summary>
    /// <value>The output of this stream.</value>
    /// <exception cref="InvalidOperationException">Thrown when the stream is a remote unidirectional stream.
    /// </exception>
    IMultiplexedStreamWriter Output { get; }

    /// <summary>Gets a task that completes when all write network activity ceases for this stream. This occurs when:
    /// <list type="bullet">
    /// <item><description>this stream's <see cref="Output" /> is completed with <see
    /// cref="IMultiplexedStreamWriter.CompleteAsync" /> or <see cref="IMultiplexedStreamWriter.Abort" />.
    /// </description></item>
    /// <item><description>the peer calls <see cref="PipeReader.Complete(Exception?)"/> on the stream's <see
    /// cref="Input" />.</description></item>
    /// <item><description>the implementation detects a network failure that prevents further writes on the underlying
    /// network stream.</description></item></list>The task is never faulted or canceled.</summary>
    /// <value>The writes closed task.</value>
    Task WritesClosed { get; }
}
