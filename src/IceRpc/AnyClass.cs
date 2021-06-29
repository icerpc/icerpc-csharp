// Copyright (c) ZeroC, Inc. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The base class for classes defined in Slice.</summary>
    public abstract class AnyClass
    {
        /// <summary>A decoder used to decode non nullable class instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<AnyClass> Decoder =
            reader => reader.ReadClass<AnyClass>(formalTypeId: null);

        /// <summary>A decoder used to decode nullable class instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<AnyClass?> NullableDecoder =
            reader => reader.ReadNullableClass<AnyClass>(formalTypeId: null);

        /// <summary>An encoder used to encode non nullable class instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<AnyClass> Encoder = (writer, value) => writer.WriteClass(value, null);

        /// <summary>An encoder used to encode nullable class instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<AnyClass?> NullableEncoder =
            (writer, value) => writer.WriteNullableClass(value, null);

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

        /// <summary>Unmarshals the current object by reading its data members from the <see cref="BufferReader"/>.
        /// </summary>
        /// <param name="reader">The buffer reader.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceRead(BufferReader reader, bool firstSlice);
        internal void Read(BufferReader reader) => IceRead(reader, true);

        /// <summary>Marshals the current object by writing its data to from the <see cref="BufferWriter"/>.</summary>
        /// <param name="writer">The stream to write to.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceWrite(BufferWriter writer, bool firstSlice);
        internal void Write(BufferWriter writer) => IceWrite(writer, true);
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
