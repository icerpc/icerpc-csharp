// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features.Internal
{
    /// <summary>A feature that represents the size (in bytes) of the Slice-encoded argument(s) of a request, or the
    /// size of the Slice-encoded return value or exception of a response. The stream argument or return value (if any)
    /// is not included in this size.</summary>
    internal sealed class PayloadSize
    {
        internal int Value { get; }

        internal PayloadSize(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentException("value must be greater than 0", nameof(value));
            }
            Value = value;
        }
    }
}
