// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace HelloCoreExample;

/// <summary>Implements a simplistic encoder/decoder for strings. It encodes strings into a <see cref="PipeReader" />
/// and decodes strings from a <see cref="PipeReader" />.</summary>
internal static class StringCodec
{
    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static PipeReader EncodeString(string s) =>
        PipeReader.Create(new ReadOnlySequence<byte>(_utf8.GetBytes(s)));

    internal static async Task<string> DecodePayloadStringAsync(PipeReader payload)
    {
        // We assume 100 UTF-8 bytes is the longest string we'll ever get.
        ReadResult readResult = await payload.ReadAtLeastAsync(100);

        // See SliceDecoder.DecodeString for a complete implementation with error handling.
        string result = _utf8.GetString(readResult.Buffer);

        payload.AdvanceTo(readResult.Buffer.End);
        await payload.CompleteAsync();
        return result;
    }
}
