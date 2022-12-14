// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A property bag used to configure client and server connections.</summary>
public record class ConnectionOptions
{
    /// <summary>Gets or sets the connection establishment timeout.</summary>
    /// <value>The connection establishment timeout value. The default is 10s.</value>
    public TimeSpan ConnectTimeout
    {
        get => _connectTimeout;
        set => _connectTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the dispatcher that dispatches requests received by this connection.</summary>
    /// <value>The dispatcher that dispatches requests received by this connection, or null if this connection does
    /// not accept requests.</value>
    public IDispatcher? Dispatcher { get; set; }

    /// <summary>Gets or sets the action to execute when a task that IceRPC starts and does not await completes due to
    /// an unhandled exception. This unhandled exception can correspond to a bug in IceRPC itself or to an exception
    /// thrown by the application code called by IceRPC. For example, when a dispatch task sends a response provided by
    /// the application and the reading of this response throws <c>MyException</c>, this action will be called with this
    /// exception instance.</summary>
    /// <value>The default action calls Assert and includes the exception in the Assert message.</value>
    /// <seealso cref="TaskScheduler.UnobservedTaskException" />
    public Action<Exception> FaultedTaskAction { get; set; } = _defaultFaultedTaskAction;

    /// <summary>Gets or sets the idle timeout. This timeout is used to gracefully shutdown the connection if it's
    /// idle for longer than this timeout. A connection is considered idle when there's no invocations or dispatches
    /// in progress.</summary>
    /// <value>The connection idle timeout value. The default is 60s.</value>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set => _idleTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the maximum number of requests that a connection can dispatch concurrently. Once this
    /// limit is reached, the connection stops reading new requests off its underlying transport connection.</summary>
    /// <value>The maximum number of requests that a connection can dispatch concurrently. 0 means no maximum. The
    /// default value is 100 requests.</value>
    /// <remarks>With the icerpc protocol, you may also need to set <see cref="MaxIceRpcBidirectionalStreams" /> and
    /// <see cref="MaxIceRpcUnidirectionalStreams" />. A typical two-way dispatch holds onto one bidirectional stream
    /// while a typical oneway dispatch quickly releases its unidirectional stream and then executes without consuming
    /// any stream.</remarks>
    public int MaxDispatches
    {
        get => _maxDispatches;
        set => _maxDispatches = value >= 0 ? value :
            throw new ArgumentOutOfRangeException(nameof(value), "value must be 0 or greater");
    }

    /// <summary>Gets or sets the maximum size of a frame received over the ice protocol.</summary>
    /// <value>The maximum size of an incoming frame, in bytes. This value must be at least 256. The default value
    /// is 1 MB.</value>
    public int MaxIceFrameSize
    {
        get => _maxIceFrameSize;
        set => _maxIceFrameSize = value >= IceMinFrameSize ? value :
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"{nameof(MaxIceFrameSize)} must be at least {IceMinFrameSize}");
    }

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote bidirectional streams that can be
    /// accepted on an icerpc connection. When this limit is reached, the peer is not allowed to open any new
    /// bidirectional stream. Since an bidirectional stream is opened for each two-way invocation, the sending of the
    /// two-way invocation will be delayed until another two-way invocation's stream completes.</summary>
    /// <value>The maximum number of bidirectional streams. It can't be less than 1 and the default value is 100.
    /// </value>
    public int MaxIceRpcBidirectionalStreams
    {
        get => _maxIceRpcBidirectionalStreams;
        set => _maxIceRpcBidirectionalStreams = value > 0 ? value :
            throw new ArgumentException(
                $"{nameof(MaxIceRpcBidirectionalStreams)} can't be less than 1",
                nameof(value));
    }

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote unidirectional streams that can be
    /// accepted on an icerpc connection. When this limit is reached, the peer is not allowed to open any new
    /// unidirectional stream. Since an unidirectional stream is opened for each one-way invocation, the sending of the
    /// one-way invocation will be delayed until another one-way invocation's stream completes.</summary>
    /// <value>The maximum number of unidirectional streams. It can't be less than 1 and the default value is 100.
    /// </value>
    public int MaxIceRpcUnidirectionalStreams
    {
        get => _maxIceRpcUnidirectionalStreams;
        set => _maxIceRpcUnidirectionalStreams = value > 0 ? value :
            throw new ArgumentException(
                $"{nameof(MaxIceRpcUnidirectionalStreams)} can't be less than 1",
                nameof(value));
    }

    /// <summary>Gets or sets the maximum size of icerpc protocol header.</summary>
    /// <value>The maximum size of the header of an incoming request, response or control frame, in bytes. The
    /// default value is 16,383, and the range of this value is 63 to 1,048,575.</value>
    public int MaxIceRpcHeaderSize
    {
        get => _maxIceRpcHeaderSize;
        set => _maxIceRpcHeaderSize = IceRpcCheckMaxHeaderSize(value);
    }

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
    public int MinSegmentSize
    {
        get => _minSegmentSize;
        set => _minSegmentSize = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(MinSegmentSize)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used by the connection for allocating memory
    /// blocks.</summary>
    /// <value>A pool of memory blocks used for buffer management.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>Gets or sets the connection shutdown timeout. This timeout is used when gracefully shutting down a
    /// connection to wait for the peer to shut down. If the peer doesn't close its side of the connection within the
    /// timeout time frame, the connection is forcefully closed.</summary>
    /// <value>The shutdown timeout value. The default is 10s.</value>
    public TimeSpan ShutdownTimeout
    {
        get => _shutdownTimeout;
        set => _shutdownTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ShutdownTimeout)}", nameof(value));
    }

    /// <summary>The default value for <see cref="MaxIceRpcHeaderSize" />.</summary>
    internal const int DefaultMaxIceRpcHeaderSize = 16_383;

    private const int IceMinFrameSize = 256;
    private static readonly Action<Exception> _defaultFaultedTaskAction =
        exception => Debug.Fail($"IceRpc task completed due to an unhandled exception: {exception}");

    private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
    private int _maxDispatches = 100;
    private int _maxIceFrameSize = 1024 * 1024;
    private int _maxIceRpcBidirectionalStreams = MultiplexedConnectionOptions.DefaultMaxBidirectionalStreams;
    private int _maxIceRpcHeaderSize = DefaultMaxIceRpcHeaderSize;
    private int _maxIceRpcUnidirectionalStreams = MultiplexedConnectionOptions.DefaultMaxUnidirectionalStreams;
    private int _minSegmentSize = 4096;
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);

    internal static int IceRpcCheckMaxHeaderSize(long value) => value is >= 63 and <= 1_048_575 ? (int)value :
        throw new ArgumentOutOfRangeException(nameof(value), "value must be between 63 and 1,048,575");
}
