// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure a client <see cref="Connection"/>.</summary>
    public record class ConnectionOptions
    {
        /// <summary>Returns the default value for <see cref="Dispatcher"/>.</summary>
        public static IDispatcher DefaultDispatcher { get; } = new InlineDispatcher((request, cancel) =>
            throw new DispatchException(DispatchErrorCode.ServiceNotFound, RetryPolicy.OtherReplica));

        /// <summary>Gets or sets the connection close timeout. This timeout is used when gracefully closing a
        /// connection to wait for the peer connection closure. If the peer doesn't close its side of the connection
        /// within the timeout timeframe, the connection is forcefully closed.</summary>
        /// <value>The close timeout value. The default is 10s.</value>
        public TimeSpan CloseTimeout
        {
            get => _closeTimeout;
            set => _closeTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(CloseTimeout)}", nameof(value));
        }

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
        public IDispatcher Dispatcher { get; set; } = DefaultDispatcher;

        /// <summary>Gets of sets the default features of the new connection.</summary>
        public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the maximum number of requests that an ice connection can dispatch concurrently.
        /// </summary>
        /// <value>The maximum number of requests that an ice connection can dispatch concurrently. 0 means no maximum.
        /// The default value is 100 requests.</value>
        public int IceMaxConcurrentDispatches
        {
            get => _iceMaxConcurrentDispatches;
            set => _iceMaxConcurrentDispatches = value >= 0 ? value :
                throw new ArgumentOutOfRangeException(nameof(value), "value must be 0 or greater");
        }

        /// <summary>Gets or sets the maximum size of a frame received over the ice protocol.</summary>
        /// <value>The maximum size of an incoming frame, in bytes. This value must be at least 256. The default value is
        /// 1 MB.</value>
        public int IceMaxFrameSize
        {
            get => _iceMaxFrameSize;
            set => _iceMaxFrameSize = value >= IceMinFrameSize ? value :
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"{nameof(IceMaxFrameSize)} must be at least {IceMinFrameSize}");
        }

        /// <summary>Gets or sets the maximum size of icerpc protocol header.</summary>
        /// <value>The maximum size of the header of an incoming request, response or control frame, in bytes. The default
        /// value is 16,383, and the range of this value is 63 to 1,048,575.</value>
        public int IceRpcMaxHeaderSize
        {
            get => _iceRpcMaxHeaderSize;
            set => _iceRpcMaxHeaderSize = IceRpcCheckMaxHeaderSize(value);
        }

        /// <summary>Gets or sets the connection's keep alive. If a connection is kept alive, the connection
        /// monitoring will send keep alive frames to ensure the peer doesn't close the connection in the period defined
        /// by its idle timeout. How often keep alive frames are sent depends on the peer's IdleTimeout configuration.
        /// </summary>
        /// <value><c>true</c>to enable connection keep alive. <c>false</c> to disable it. The default is <c>false</c>.
        /// </value>
        public bool KeepAlive { get; set; }

        /// <summary>Gets or set an action that executes when the connection is closed.</summary>
        public Action<Connection, Exception>? OnClose { get; set; }


        private const int IceMinFrameSize = 256;
        /// <summary>The default value for <see cref="IceRpcMaxHeaderSize"/>.</summary>
        internal const int IceRpcDefaultMaxHeaderSize = 16_383;

        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

        private int _iceMaxConcurrentDispatches = 100;
        private int _iceMaxFrameSize = 1024 * 1024;

        private int _iceRpcMaxHeaderSize = IceRpcDefaultMaxHeaderSize;

        internal static int IceRpcCheckMaxHeaderSize(long value) => value is >= 63 and <= 1_048_575 ? (int)value :
            throw new ArgumentOutOfRangeException(nameof(value), "value must be between 63 and 1,048,575");
    }
}
