// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Configure
{
    /// <summary>The base options class for multiplexed transports.</summary>
    public record class MultiplexedTransportOptions
    {
        /// <summary>Configures the bidirectional stream maximum count to limit the number of concurrent
        /// bidirectional streams opened on a connection. When this limit is reached, trying to open a new
        /// bidirectional stream will be delayed until a bidirectional stream is closed. Since an
        /// bidirectional stream is opened for each two-way proxy invocation, the sending of the two-way
        /// invocation will be delayed until another two-way invocation on the connection completes.</summary>
        /// <value>The bidirectional stream maximum count. It can't be less than 1 and the default value is 100.</value>
        public int BidirectionalStreamMaxCount
        {
            get => _bidirectionalStreamMaxCount;
            set => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        /// <summary>Configures the unidirectional stream maximum count to limit the number of concurrent
        /// unidirectional streams opened on a connection. When this limit is reached, trying to open a new
        /// unidirectional stream will be delayed until an unidirectional stream is closed. Since an
        /// unidirectional stream is opened for each one-way proxy invocation, the sending of the one-way
        /// invocation will be delayed until another one-way invocation on the connection completes.</summary>
        /// <value>The unidirectional stream maximum count. It can't be less than 1 and the default value is
        /// 100.</value>
        public int UnidirectionalStreamMaxCount
        {
            get => _unidirectionalStreamMaxCount;
            set => _unidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(UnidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        private int _bidirectionalStreamMaxCount = 100;
        private int _unidirectionalStreamMaxCount = 100;
    }
}
