// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure the ice protocol.</summary>
    public sealed record class IceProtocolOptions
    {
        /// <summary>Gets or sets the maximum size of an incoming ice frame.</summary>
        /// <value>The maximum size of an incoming ice frame, in bytes. This value must be between at least 256. The
        /// default value is 1 MB.</value>
        public int MaxIncomingFrameSize
        {
            get => _maxIncomingFrameSize;
            set => _maxIncomingFrameSize = value >= MinValue ? value :
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"{nameof(MaxIncomingFrameSize)} must be at least {MinValue}");
        }

        /// <summary>A shared instance that holds the default options.</summary>
        /// <remarks>It's internal to avoid accidental changes to these shared default options.</remarks>
        internal static IceProtocolOptions Default { get; } = new();

        private const int DefaultValue = 1024 * 1024;
        private const int MinValue = 256;
        private int _maxIncomingFrameSize = DefaultValue;
    }
}
