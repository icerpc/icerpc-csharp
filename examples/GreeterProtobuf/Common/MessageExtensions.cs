// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace Google.Protobuf;

/// <summary>Provides extension methods for <see cref="IMessage" />.</summary>
public static class MessageExtensions
{
    /// <summary>Merges data from a <see cref="PipeReader" /> into an existing message.</summary>
    /// <param name="message">The <see cref="IMessage" /> to merge the data into.</param>
    /// <param name="reader">The <see cref="PipeReader" /> containing the protobuf-encoded binary data to merge.</param>
    public static async Task MergeFromAsync(this IMessage message, PipeReader reader)
    {
        using var stream = new MemoryStream();
        await reader.CopyToAsync(stream).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);
        message.MergeFrom(stream);
    }

    public static PipeReader ToPipeReader(this IMessage message) =>
        PipeReader.Create(new ReadOnlySequence<byte>(message.ToByteArray()));
}
