// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure the ice protocol.</summary>
    public sealed record class IceProtocolOptions
    {
        /// <summary>Gets or sets the maximum number of requests that the connection can dispatch concurrently.
        /// </summary>
        /// <value>The maximum number of requests that the connection can dispatch concurrently. 0 means a connection
        /// can dispatch any number of requests concurrently. The default value is 100.</value>
        public int MaxConcurrentDispatches
        {
            get => _maxConcurrentDispatches;
            set => _maxConcurrentDispatches = value >= 0 ? value :
                throw new ArgumentOutOfRangeException(nameof(value), "value must be 0 or greater");
        }

        /// <summary>Gets or sets the maximum size of an incoming ice frame.</summary>
        /// <value>The maximum size of an incoming ice frame, in bytes. This value must be between at least 256. The
        /// default value is 1 MB.</value>
        public int MaxIncomingFrameSize
        {
            get => _maxIncomingFrameSize;
            set => _maxIncomingFrameSize = value >= MinIncomingFrameSizeValue ? value :
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"{nameof(MaxIncomingFrameSize)} must be at least {MinIncomingFrameSizeValue}");
        }

        /// <summary>A shared instance that holds the default options.</summary>
        /// <remarks>It's internal to avoid accidental changes to these shared default options.</remarks>
        internal static IceProtocolOptions Default { get; } = new();

        private const int MinIncomingFrameSizeValue = 256;
        private int _maxConcurrentDispatches = 100;
        private int _maxIncomingFrameSize = 1024 * 1024;
    }
}
