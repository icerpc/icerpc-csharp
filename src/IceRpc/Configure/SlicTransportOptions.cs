// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Configure
{
    /// <summary>The base options class for Slic.</summary>
    public record class SlicTransportOptions : MultiplexedTransportOptions
    {
        /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
        /// <value>A pool of memory blocks used for buffer management.</value>
        public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

        /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
        /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
        public int MinimumSegmentSize
        {
            get => _minimumSegmentSize;
            set => _minimumSegmentSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(MinimumSegmentSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>The maximum packet size in bytes.</summary>
        /// <value>The maximum packet size in bytes. It can't be less than 1KB and the default value is
        /// 32KB.</value>
        public int PacketMaxSize
        {
            get => _packetMaxSize;
            set => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>Gets or sets the number of bytes when writes on a Slic stream starts blocking.</summary>
        /// <value>The pause writer threshold.</value>
        public int PauseWriterThreshold
        {
            get => _pauseWriterThreshold;
            set => _pauseWriterThreshold = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PauseWriterThreshold)} can't be less than 1KB", nameof(value));
        }

        /// <summary>Gets or sets the number of bytes when writes on a Slic stream stops blocking.</summary>
        /// <value>The resume writer threshold.</value>
        public int ResumeWriterThreshold
        {
            get => _resumeWriterThreshold;
            set => _resumeWriterThreshold =
                value < 1024 ? throw new ArgumentException(
                    @$"{nameof(ResumeWriterThreshold)} can't be less than 1KB", nameof(value)) :
                value > _pauseWriterThreshold ? throw new ArgumentException(
                    @$"{nameof(ResumeWriterThreshold)
                        } can't be greater can't be greater than {nameof(PauseWriterThreshold)}", nameof(value)) :
                value;
        }

        private int _minimumSegmentSize = 4096;
        // The default packet size matches the SSL record maximum data size to avoid fragmentation of the Slic packet
        // when using SSL.
        private int _packetMaxSize = 16384;
        private int _pauseWriterThreshold = 65536;
        private int _resumeWriterThreshold = 32768;
    }

    /// <summary>An options class for configuring a <see cref="SlicClientTransport"/>.</summary>
    public sealed record class SlicClientTransportOptions : SlicTransportOptions
    {
        /// <summary>Gets or sets the underlying simple client transport.</summary>
        public IClientTransport<ISimpleNetworkConnection>? SimpleClientTransport { get; set; }
    }

    /// <summary>An options class for configuring a <see cref="SlicServerTransport"/>.</summary>
    public sealed record class SlicServerTransportOptions : SlicTransportOptions
    {
        /// <summary>Gets or sets the underlying simple server transport.</summary>
        public IServerTransport<ISimpleNetworkConnection>? SimpleServerTransport { get; set; }
    }
}
