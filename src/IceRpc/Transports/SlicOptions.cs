// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>An options class for configuring Slic based transports.</summary>
    public class SlicOptions
    {
        /// <summary>The Slic packet maximum size in bytes. It can't be less than 1KB and the default value is 32KB.
        /// Slic is only used for the Ice2 protocol, this setting is ignored when using the Ice1 protocol.</summary>
        /// <value>The Slic packet maximum size in bytes.</value>
        public int SlicPacketMaxSize
        {
            get => _slicPacketMaxSize;
            set => _slicPacketMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SlicPacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>The Slic stream buffer maximum size in bytes. The stream buffer is used when streaming data with
        /// a stream Slice parameter. It can't be less than 1KB and the default value is twice the Slic packet maximum
        /// size. Slic is only used for the Ice2 protocol, this setting is ignored when using the Ice1 protocol.
        /// </summary>
        /// <value>The Slic stream buffer maximum size in bytes.</value>
        public int SlicStreamBufferMaxSize
        {
            get => _slicStreamBufferMaxSize ?? 2 * SlicPacketMaxSize;
            set => _slicStreamBufferMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SlicStreamBufferMaxSize)} cannot be less than 1KB", nameof(value));
        }

        private int _slicPacketMaxSize = 32 * 1024;
        private int? _slicStreamBufferMaxSize;
    }
}
