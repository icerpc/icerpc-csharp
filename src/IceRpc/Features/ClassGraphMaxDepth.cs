// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features
{
    /// <summary>A feature used to override the class graph max depth used when decoding Slice.</summary>
    public sealed class ClassGraphMaxDepth
    {
        /// <summary>.The maximum depth for a graph of Slice class instances.</summary>
        // TODO: replace by a max depth property that is not class specific.
        // TODO: consider moving to IceRpc.Slice - or do we need to put feature classes in the Features subnamespace?
        public int Value
        {
            get => _value;
            set => _value = value > 1 ? value : throw new ArgumentException("value must be at least 1", nameof(value));
        }

        private int _value = 100;
    }
}
