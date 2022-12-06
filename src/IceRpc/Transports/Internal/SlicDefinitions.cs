// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.Transports.Internal;

internal static class SlicDefinitions
{
    internal const ulong V1 = 1;
}

internal static class ParameterFieldValueSliceDecoderExtensions
{
    internal static ReadOnlySequence<byte> DecodeParameterFieldValue(this ref SliceDecoder decoder) =>
        new ReadOnlySequence<byte>(decoder.DecodeSequence<byte>());
}

internal static class ParameterFieldValueSliceEncoderExtensions
{
    internal static void EncodeParameterFieldValue(this ref SliceEncoder encoder, ReadOnlySequence<byte> value)
    {
        encoder.EncodeSize((int)value.Length);
        encoder.WriteByteSequence(value);
    }
}
