// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Configure
{
    /// <summary>An options class for configuring Slic based transports.</summary>
    public class SlicOptions
    {
        private int _bidirectionalStreamMaxCount = 100;
        private int _minimumSegmentSize = 4 * 1024;
        private int _packetMaxSize = 32 * 1024;
        private int _pauseWriterThreshold = 64 * 1024;
        private int _resumeWriterThreshold = 32 * 1024;
        private int _unidirectionalStreamMaxCount = 100;

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
            init => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        /// <summary>Gets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
        /// <value>A pool of memory blocks used for buffer management.</value>
        public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

        /// <summary>Gets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
        /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
        public int MinimumSegmentSize
        {
            get => _minimumSegmentSize;
            set => _minimumSegmentSize = value >= 1024 ? value:
                throw new ArgumentException($"{nameof(MinimumSegmentSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>The packet maximum size in bytes. It can't be less than 1KB and the default value is
        /// 32KB.</summary>
        /// <value>The packet maximum size in bytes.</value>
        public int PacketMaxSize
        {
            get => _packetMaxSize;
            init => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>Gets the number of bytes when writes on a Slic stream starts blocking.</summary>
        /// <value>The pause writer threshold.</value>
        public int PauseWriterThreshold
        {
            get => _pauseWriterThreshold;
            init => _pauseWriterThreshold = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PauseWriterThreshold)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>Gets the number of bytes when writes on a Slic stream stops blocking.</summary>
        /// <value>The resume writer threshold.</value>
        public int ResumeWriterThreshold
        {
            get => _resumeWriterThreshold;
            init => _resumeWriterThreshold = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ResumeWriterThreshold)} cannot be less than 1KB", nameof(value));
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
            init => _unidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(UnidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        internal void Check()
        {
            if (_resumeWriterThreshold > _pauseWriterThreshold)
            {
                throw new ArgumentException(@$"invalid {nameof(SlicOptions)}, {nameof(ResumeWriterThreshold)
                    } can't be greather than the value of {nameof(PauseWriterThreshold)}");
            }
        }
    }
}
