// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

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
    /// <value>The dispatcher that dispatches requests received by this connection.</value>
    public IDispatcher Dispatcher { get; set; } = NullDispatcher.Instance;

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

    /// <summary>Gets or sets the maximum number of requests that an ice connection can dispatch concurrently.
    /// </summary>
    /// <value>The maximum number of requests that an ice connection can dispatch concurrently. 0 means no maximum.
    /// The default value is 100 requests.</value>
    public int MaxIceConcurrentDispatches
    {
        get => _iceConcurrentDispatches;
        set => _iceConcurrentDispatches = value >= 0 ? value :
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

    /// <summary>Gets or sets the maximum size of icerpc protocol header.</summary>
    /// <value>The maximum size of the header of an incoming request, response or control frame, in bytes. The
    /// default value is 16,383, and the range of this value is 63 to 1,048,575.</value>
    public int MaxIceRpcHeaderSize
    {
        get => _maxIceRpcHeaderSize;
        set => _maxIceRpcHeaderSize = IceRpcCheckMaxHeaderSize(value);
    }

    /// <summary>Gets or sets the connection shutdown timeout. This timeout is used when gracefully shutting down a
    /// connection to wait for the remote peer to shut down. If the peer doesn't close its side of the connection
    /// within the timeout time frame, the connection is forcefully closed.</summary>
    /// <value>The shutdown timeout value. The default is 10s.</value>
    public TimeSpan ShutdownTimeout
    {
        get => _shutdownTimeout;
        set => _shutdownTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ShutdownTimeout)}", nameof(value));
    }

    /// <summary>The default value for <see cref="MaxIceRpcHeaderSize"/>.</summary>
    internal const int DefaultMaxIceRpcHeaderSize = 16_383;

    private const int IceMinFrameSize = 256;
    private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
    private int _iceConcurrentDispatches = 100;
    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
    private int _maxIceFrameSize = 1024 * 1024;
    private int _maxIceRpcHeaderSize = DefaultMaxIceRpcHeaderSize;
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);

    internal static int IceRpcCheckMaxHeaderSize(long value) => value is >= 63 and <= 1_048_575 ? (int)value :
        throw new ArgumentOutOfRangeException(nameof(value), "value must be between 63 and 1,048,575");
}
