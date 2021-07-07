// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The base class for classes defined in Slice.</summary>
    public abstract class AnyClass
    {
        /// <summary>An Ice reader for non-nullable class instances.</summary>
        public static readonly IceReader<AnyClass> IceReader =
            decoder => decoder.ReadClass<AnyClass>(formalTypeId: null);

        /// <summary>An Ice writer for non-nullable class instances.</summary>
        public static readonly IceWriter<AnyClass> IceWriter = (encoder, value) => encoder.WriteClass(value, null);

        /// <summary>An Ice reader for nullable class instances.</summary>
        public static readonly IceReader<AnyClass?> NullableIceReader =
            decoder => decoder.ReadNullableClass<AnyClass>(formalTypeId: null);

        /// <summary>An Ice writer for nullable class instances.</summary>
        public static readonly IceWriter<AnyClass?> NullableIceWriter =
            (encoder, value) => encoder.WriteNullableClass(value, null);

        /// <summary>Returns the sliced data if the class has a preserved-slice base class and has been sliced during
        /// unmarshaling, otherwise <c>null</c>.</summary>
        protected virtual SlicedData? IceSlicedData
        {
            get => null;
            set => Debug.Assert(false);
        }

        internal SlicedData? SlicedData
        {
            get => IceSlicedData;
            set => IceSlicedData = value;
        }

        /// <summary>Reads this instance by reading its data members from the <see cref="IceDecoder"/>.
        /// </summary>
        /// <param name="iceDecoder">The Ice decoder.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceRead(IceDecoder iceDecoder, bool firstSlice);
        internal void Read(IceDecoder iceDecoder) => IceRead(iceDecoder, true);

        /// <summary>Writes this instance by writing its data to the <see cref="IceEncoder"/>.</summary>
        /// <param name="writer">The buffer writter.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceWrite(IceEncoder writer, bool firstSlice);
        internal void Write(IceEncoder writer) => IceWrite(writer, true);
    }

    /// <summary>Provides public extensions methods for AnyClass instances.</summary>
    public static class AnyClassExtensions
    {
        /// <summary>During unmarshaling, Ice can slice off derived slices that it does not know how to read, and it can
        /// optionally preserve those "unknown" slices. See the Slice preserve metadata directive and class
        /// <see cref="UnknownSlicedClass"/>.</summary>
        /// <returns>A SlicedData value that provides the list of sliced-off slices.</returns>
        public static SlicedData? GetSlicedData(this AnyClass obj) => obj.SlicedData;
    }
}
