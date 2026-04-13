// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;

namespace IceRpc.Slice.Generator;

/// <summary>Shared helpers used by both <see cref="ProxyGenerator"/> and <see cref="DispatchGenerator"/>.</summary>
internal static class EncodeHelper
{
    /// <summary>Wraps an encode body in the standard pipe creation/size-placeholder boilerplate.</summary>
    internal static CodeBlock BuildEncodeBody(CodeBlock encodeBody)
    {
        return new CodeBlock($$"""
            var pipe_ = new global::System.IO.Pipelines.Pipe(
                encodeOptions?.PipeOptions ?? SliceEncodeOptions.Default.PipeOptions);
            var encoder_ = new SliceEncoder(pipe_.Writer);

            Span<byte> sizePlaceholder_ = encoder_.GetPlaceholderSpan(4);
            int startPos_ = encoder_.EncodedByteCount;

            {{encodeBody}}

            SliceEncoder.EncodeVarUInt62((ulong)(encoder_.EncodedByteCount - startPos_), sizePlaceholder_);

            pipe_.Writer.Complete();
            return pipe_.Reader;
            """);
    }
}
