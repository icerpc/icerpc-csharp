// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Pipe extensions methods to complete pipe readers and writers for application errors. Interceptors and
    /// middlewares can use these extension methods to complete the pipe readers or writers with specific errors. The
    /// writer or reader is completed with a <see cref="MultiplexedStreamAbortedException"/> created from the
    /// application error.</summary>
    public static class ApplicationErrorPipeExtensions
    {
        /// <summary>Signals to the producer that the consumer is done reading.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="error">The application error.</param>
        public static void Complete(this PipeReader reader, int error) =>
            reader.Complete(new MultiplexedStreamAbortedException(MultiplexedStreamErrorKind.Application, error));

        /// <summary>Signals to the producer that the consumer is done reading.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="error">The application error.</param>
        public static ValueTask CompleteAsync(this PipeReader reader, int error) =>
            reader.CompleteAsync(new MultiplexedStreamAbortedException(MultiplexedStreamErrorKind.Application, error));

        /// <summary>Signals to the consumer that the producer is done writing.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="error">The application error.</param>
        public static void Complete(this PipeWriter writer, int error) =>
            writer.Complete(new MultiplexedStreamAbortedException(MultiplexedStreamErrorKind.Application, error));

        /// <summary>Signals to the consumer that the producer is done writing.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="error">The application error.</param>
        public static ValueTask CompleteAsync(this PipeWriter writer, int error) =>
            writer.CompleteAsync(new MultiplexedStreamAbortedException(MultiplexedStreamErrorKind.Application, error));
    }
}
