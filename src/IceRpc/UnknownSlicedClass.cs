// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

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
        protected override void IceRead(BufferReader istr, bool firstSlice) => IceSlicedData = istr.SlicedData;

        /// <inheritdoc/>
        protected override SlicedData? IceSlicedData { get; set; }

        /// <inheritdoc/>
        protected override void IceWrite(BufferWriter writer, bool firstSlice) =>
            writer.WriteSlicedData(IceSlicedData!.Value, Array.Empty<string>());

        internal UnknownSlicedClass()
        {
        }
    }
}
