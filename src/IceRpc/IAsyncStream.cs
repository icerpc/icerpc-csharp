// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents an incoming stream of elements decoded from a request or response payload.</summary>
/// <typeparam name="T">The type of the streamed elements.</typeparam>
/// <remarks>An <see cref="IAsyncStream{T}" /> owns the underlying transport
/// <see cref="System.IO.Pipelines.PipeReader" /> from which elements are decoded. It is the caller's responsibility
/// to complete this reader by disposing the stream, or iterating over its elements, or doing both.
/// <para>Calling <see cref="IDisposable.Dispose" /> on a stream while an async iteration is in progress is safe:
/// the in-flight read is unblocked and the consumer's <c>MoveNextAsync</c> throws
/// <see cref="ObjectDisposedException" />. Calling <see cref="IDisposable.Dispose" /> a second time is a no-op.
/// </para></remarks>
public interface IAsyncStream<out T> : IAsyncEnumerable<T>, IDisposable
{
}
