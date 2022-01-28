// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Provides extension methods for <see cref="PipeWriter"/>.</summary>
    public static class PipeWriterExtensions
    {
        /// <summary>Creates a stream over the supplied pipe writer. The disposal of this stream calls
        /// <see cref="PipeWriter.CompleteAsync"/> on the pipe writer. Unlike the stream returned by
        /// <see cref="PipeWriter.AsStream"/>, this stream's DisposeAsync method never calls
        /// <see cref="PipeWriter.Complete"/> for a successful completion.</summary>
        /// <param name="writer">The pipe writer.</param>
        /// <returns>The stream that wraps <paramref name="writer"/>.</returns>
        /// <remarks>Always use this method and not <see cref="PipeWriter.AsStream"/> when wrapping an
        /// <see cref="OutgoingFrame.PayloadSink"/> in an interceptor or middleware. Otherwise, when IceRPC calls
        /// <see cref="PipeWriter.CompleteAsync"/> on the payload sink through your stream wrapper, this call gets
        /// converted into a call to <see cref="PipeWriter.Complete"/>.</remarks>
        public static Stream ToPayloadSinkStream(this PipeWriter writer) => new PipeWriterStream(writer);
    }
}
