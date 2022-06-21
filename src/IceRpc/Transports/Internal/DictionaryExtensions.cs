// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Transports.Internal;

internal static class DictionaryExtensions
{
    internal static IEnumerable<(ParameterKey Key, ulong Value)> DecodedParameters(
        this IDictionary<int, IList<byte>> parameters) =>
        parameters.Select(pair =>
            ((ParameterKey)pair.Key,
             SliceEncoding.Slice2.DecodeBuffer(
                 new ReadOnlySequence<byte>(pair.Value.ToArray()), // TODO: fix to avoid copy
                 (ref SliceDecoder decoder) => decoder.DecodeVarUInt62())));
}
