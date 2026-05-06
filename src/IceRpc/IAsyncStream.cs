// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents an incoming stream of elements decoded from a request or response payload.</summary>
/// <typeparam name="T">The type of the streamed elements.</typeparam>
/// <remarks>An <see cref="IAsyncStream{T}" /> owns the underlying transport
/// <see cref="System.IO.Pipelines.PipeReader" /> from which elements are decoded. It is the caller's responsibility to
/// dispose the stream — typically with a <c>using</c> statement — to complete this reader.</remarks>
public interface IAsyncStream<out T> : IAsyncEnumerable<T>, IDisposable
    where T : allows ref struct
{
}
