// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>UnknownSlicedClass represents a fully sliced class instance. The local Ice runtime does not known this
    /// type or any of its base classes (other than AnyClass).</summary>
    public sealed class UnknownSlicedClass : AnyClass
    {
        /// <summary>Returns the most derived type ID this class instance.</summary>
        /// <value>The type ID.</value>
        public string TypeId => IceSlicedData!.Value.Slices[0].TypeId;

        /// <inheritdoc/>
        protected override void IceDecode(Ice11Decoder decoder, bool firstSlice) => IceSlicedData = decoder.SlicedData;

        /// <inheritdoc/>
        protected override SlicedData? IceSlicedData { get; set; }

        /// <inheritdoc/>
        protected override void IceEncode(Ice11Encoder encoder, bool firstSlice) =>
            encoder.EncodeSlicedData(IceSlicedData!.Value, Array.Empty<string>());

        internal UnknownSlicedClass()
        {
        }
    }
}
