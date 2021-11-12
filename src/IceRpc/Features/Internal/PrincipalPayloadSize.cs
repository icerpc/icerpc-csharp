// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features.Internal
{
    /// <summary>A feature that represents the size of the principal payload of a request or response. The principal
    /// payload designates the encoded arguments, return value or remote exception without any stream parameter or
    /// return value.</summary>
    internal sealed class PrincipalPayloadSize
    {
        internal int Value { get; }

        internal PrincipalPayloadSize(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentException("value must be greater than 0", nameof(value));
            }
            Value = value;
        }
    }
}
