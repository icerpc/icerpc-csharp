// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Configure
{
    /// <summary>The base options class for Slic.</summary>
    public record class SlicOptions
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
            init => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        /// <summary>Gets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
        /// <value>A pool of memory blocks used for buffer management.</value>
        public MemoryPool<byte> Pool { get; init; } = MemoryPool<byte>.Shared;

        /// <summary>Gets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
        /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
        public int MinimumSegmentSize
        {
            get => _minimumSegmentSize;
            init => _minimumSegmentSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(MinimumSegmentSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>The packet maximum size in bytes. It can't be less than 1KB and the default value is
        /// 32KB.</summary>
        /// <value>The packet maximum size in bytes.</value>
        public int PacketMaxSize
        {
            get => _packetMaxSize;
            init => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>Gets the number of bytes when writes on a Slic stream starts blocking.</summary>
        /// <value>The pause writer threshold.</value>
        public int PauseWriterThreshold
        {
            get => _pauseWriterThreshold;
            init => _pauseWriterThreshold = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PauseWriterThreshold)} can't be less than 1KB", nameof(value));
        }

        /// <summary>Gets the number of bytes when writes on a Slic stream stops blocking.</summary>
        /// <value>The resume writer threshold.</value>
        public int ResumeWriterThreshold
        {
            get => _resumeWriterThreshold;
            init => _resumeWriterThreshold =
                value < 1024 ? throw new ArgumentException(
                    @$"{nameof(ResumeWriterThreshold)} can't be less than 1KB", nameof(value)) :
                value > _pauseWriterThreshold ? throw new ArgumentException(
                    @$"{nameof(ResumeWriterThreshold)
                        } can't be greater can't be greater than {nameof(PauseWriterThreshold)}", nameof(value)) :
                value;
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

        private int _bidirectionalStreamMaxCount = 100;
        private int _minimumSegmentSize = 4096;
        // The default packet size matches the SSL record maximum data size to avoid fragramentation of the Slic packet
        // when using SSL.
        private int _packetMaxSize = 16384;
        private int _pauseWriterThreshold = 65536;
        private int _resumeWriterThreshold = 32768;
        private int _unidirectionalStreamMaxCount = 100;
    }

    /// <summary>An options class for configuring a <see cref="SlicClientTransport"/>.</summary>
    public sealed record class SlicClientTransportOptions : SlicOptions
    {
        /// <summary>Gets or initializes the underlying simple client transport.</summary>
        public IClientTransport<ISimpleNetworkConnection>? SimpleClientTransport { get; init; }
    }

    /// <summary>An options class for configuring a <see cref="SlicServerTransport"/>.</summary>
    public sealed record class SlicServerTransportOptions : SlicOptions
    {
        /// <summary>Gets or initializes the underlying simple server transport.</summary>
        public IServerTransport<ISimpleNetworkConnection>? SimpleServerTransport { get; init; }
    }
}
