// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The base class for classes defined in Slice.</summary>
    public abstract class AnyClass
    {
        /// <summary>A decode function for non-nullable class instances.</summary>
        public static readonly DecodeFunc<AnyClass> DecodeFunc =
            decoder => decoder.DecodeClass<AnyClass>(formalTypeId: null);

        /// <summary>An encode action for non-nullable class instances.</summary>
        public static readonly EncodeAction<AnyClass> EncodeAction = (encoder, value) => encoder.EncodeClass(value);

        /// <summary>A decode function for nullable class instances.</summary>
        public static readonly DecodeFunc<AnyClass?> NullableDecodeFunc =
            decoder => decoder.DecodeNullableClass<AnyClass>(formalTypeId: null);

        /// <summary>An encode action for nullable class instances.</summary>
        public static readonly EncodeAction<AnyClass?> NullableEncodeAction =
            (encoder, value) => encoder.EncodeNullableClass(value);

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

        /// <summary>Decodes this instance by decoding its data members from the <see cref="IceDecoder"/>.
        /// </summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceDecode(IceDecoder decoder, bool firstSlice);
        internal void Decode(IceDecoder decoder) => IceDecode(decoder, true);

        /// <summary>Encodes this instance by encoding its data to the <see cref="IceEncoder"/>.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceEncode(IceEncoder encoder, bool firstSlice);
        internal void Encode(IceEncoder encoder) => IceEncode(encoder, true);
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
