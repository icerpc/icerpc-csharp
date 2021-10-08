// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>An options class for configuring Slic based transports.</summary>
    public class SlicOptions
    {
        /// <summary>Configures the bidirectional stream maximum count to limit the number of concurrent
        /// bidirectional streams opened on a connection. When this limit is reached, trying to open a new
        /// bidirectional stream will be delayed until a bidirectional stream is closed. Since an
        /// bidirectional stream is opened for each two-way proxy invocation, the sending of the two-way
        /// invocation will be delayed until another two-way invocation on the connection completes. It can't
        /// be less than 1 and the default value is 100.</summary>
        /// <value>The bidirectional stream maximum count.</value>
        public int BidirectionalStreamMaxCount
        {
            get => _bidirectionalStreamMaxCount;
            set => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        /// <summary>The packet maximum size in bytes. It can't be less than 1KB and the default value is
        /// 32KB.</summary>
        /// <value>The packet maximum size in bytes.</value>
        public int PacketMaxSize
        {
            get => _packetMaxSize;
            set => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>The stream buffer maximum size in bytes. The stream buffer is used when streaming data
        /// with a stream Slice parameter. It can't be less than 1KB and the default value is twice the packet
        /// maximum size.</summary>
        /// <value>The stream buffer maximum size in bytes.</value>
        public int StreamBufferMaxSize
        {
            get => _streamBufferMaxSize ?? 2 * PacketMaxSize;
            set => _streamBufferMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(StreamBufferMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>Configures the unidirectional stream maximum count to limit the number of concurrent
        /// unidirectional streams opened on a connection. When this limit is reached, trying to open a new
        /// unidirectional stream will be delayed until an unidirectional stream is closed. Since an
        /// unidirectional stream is opened for each one-way proxy invocation, the sending of the one-way
        /// invocation will be delayed until another one-way invocation on the connection completes. It can't
        /// be less than 1 and the default value is 100.</summary>
        /// <value>The unidirectional stream maximum count.</value>
        public int UnidirectionalStreamMaxCount
        {
            get => _unidirectionalStreamMaxCount;
            set => _unidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(UnidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        private int _bidirectionalStreamMaxCount = 100;
        private int _packetMaxSize = 32 * 1024;
        private int? _streamBufferMaxSize;
        private int _unidirectionalStreamMaxCount = 100;
    }
}
