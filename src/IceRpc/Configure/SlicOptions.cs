// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Transports
{
    /// <summary>An options class for configuring Slic based transports.</summary>
    public class SlicOptions
    {
        private int _bidirectionalStreamMaxCount = 100;
        private int _minimumSegmentSize = 4 * 1024;
        private int _packetMaxSize = 32 * 1024;
        private int _pauseWriterThreeshold = 64 * 1024;
        private int _resumeWriterThreeshold = 32 * 1024;
        private int _unidirectionalStreamMaxCount = 100;

        /// <summary>Constructs Slic options</summary>
        public SlicOptions() => Pool = MemoryPool<byte>.Shared;

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

        /// <summary>Gets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
        /// <value>A pool of memory blocks used for buffer management.</value>
        public MemoryPool<byte> Pool { get; set; }

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
            set => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>Gets the number of bytes when writes on a Slic stream starts blocking.</summary>
        /// <value>The pause writer threeshold.</value>
        public int PauseWriterThreeshold
        {
            get => _pauseWriterThreeshold;
            set => _pauseWriterThreeshold = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PauseWriterThreeshold)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>Gets the number of bytes when writes on a Slic stream stops blocking.</summary>
        /// <value>The resume writer threeshold.</value>
        public int ResumeWriterThreeshold
        {
            get => _resumeWriterThreeshold;
            set => _resumeWriterThreeshold = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ResumeWriterThreeshold)} cannot be less than 1KB", nameof(value));
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

        internal void Check()
        {
            if (_resumeWriterThreeshold > _pauseWriterThreeshold)
            {
                throw new ArgumentException(@$"invalid {nameof(SlicOptions)}, {nameof(ResumeWriterThreeshold)
                    } can't be superior to {nameof(PauseWriterThreeshold)}");
            }
        }
    }
}
