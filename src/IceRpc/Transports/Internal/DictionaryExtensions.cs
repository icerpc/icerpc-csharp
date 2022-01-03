// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Transports.Internal
{
    internal static class DictionaryExtensions
    {
        internal static IEnumerable<(ParameterKey Key, ulong Value)> DecodedParameters(
            this IDictionary<int, IList<byte>> parameters) =>
            parameters.Select(pair =>
                ((ParameterKey)pair.Key,
                 Encoding.Slice20.DecodeBuffer(
                     pair.Value.ToArray(),
                     (ref IceDecoder decoder) => decoder.DecodeVarULong())));
    }
}
