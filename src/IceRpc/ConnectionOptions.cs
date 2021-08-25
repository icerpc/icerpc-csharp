// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>An options base class for configuring IceRPC connections.</summary>
    public class ConnectionOptions
    {
        /// <summary>Gets or sets the activator used by <see cref="Ice11Decoder"/>.</summary>
        public IActivator<Ice11Decoder>? Activator11 { get; set; }

        /// <summary>Gets or sets the activator used by <see cref="Ice20Decoder"/>.</summary>
        public IActivator<Ice20Decoder>? Activator20 { get; set; }

        /// <summary>Configures the maximum depth for a graph of Slice class instances to unmarshal. When the limit is
        /// reached, the IceRpc run time throws <see cref="InvalidDataException"/>.</summary>
        /// <value>The maximum depth for a graph of Slice class instances to unmarshal.</value>
        public int ClassGraphMaxDepth
        {
            get => _classGraphMaxDepth;
            set => _classGraphMaxDepth = value < 1 ? int.MaxValue : value;
        }

        /// <summary>The connection close timeout. This timeout is used when gracefully closing a connection to
        /// wait for the peer connection closure. If the peer doesn't close its side of the connection within the
        /// timeout timeframe, the connection is forcefully closed. It can't be 0 and the default value is 10s.
        /// </summary>
        /// <value>The close timeout value.</value>
        public TimeSpan CloseTimeout
        {
            get => _closeTimeout;
            set => _closeTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(CloseTimeout)}", nameof(value));
        }

        /// <summary>The connection establishment timeout. It can't be 0 and the default value is 10s.</summary>
        /// <value>The connection establishment timeout value.</value>
        public TimeSpan ConnectTimeout
        {
            get => _connectTimeout;
            set => _connectTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
        }

        /// <summary>The features of the connection.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>The connection idle timeout. This timeout is used to monitor the connection. If the connection
        /// is idle within this timeout period, the connection is gracefully closed. It can't be 0 and the default
        /// value is 60s.</summary>
        /// <value>The connection idle timeout value.</value>
        public TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            set => _idleTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
        }

        /// <summary>The maximum size in bytes of an incoming Ice1 or Ice2 protocol frame. It's important to specify
        /// a reasonable value for this size since it limits the size of the buffer allocated by IceRPC to receive
        /// a request or response. It can't be less than 1KB and the default value is 1MB.</summary>
        /// <value>The maximum size of incoming frame in bytes.</value>
        public int IncomingFrameMaxSize
        {
            get => _incomingFrameMaxSize;
            set => _incomingFrameMaxSize = value >= 1024 ? value :
                value <= 0 ? int.MaxValue :
                throw new ArgumentException($"{nameof(IncomingFrameMaxSize)} cannot be less than 1KB ", nameof(value));
        }

        /// <summary>Configures whether or not connections are kept alive. If a connection is kept alive, the
        /// connection monitoring will send keep alive frames to ensure the peer doesn't close the connection
        /// in the period defined by its idle timeout. How often keep alive frames are sent depends on the
        /// peer's IdleTimeout configuration. The default value is false.</summary>
        /// <value>Enables connection keep alive.</value>
        public bool KeepAlive { get; set; }

        private int _classGraphMaxDepth = 100;
        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int _incomingFrameMaxSize = 1024 * 1024;
    }
}
