// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>The IceRpc core and built-in invokers, dispatchers (including built-in middleware and interceptors) report
/// errors by throwing this exception. Slice invocations throw <see cref="DispatchException" /> in addition to
/// <see cref="IceRpcException" />.</summary>
public class IceRpcException : IOException
{
    /// <summary>Gets the IceRpc error.</summary>
    /// <value>The <see cref="IceRpcError.IceRpcError"/> of this exception.</value>
    public IceRpcError IceRpcError { get; }

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class.</summary>
    /// <param name="error">The error.</param>
    /// <param name="message">A message that describes the exception.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public IceRpcException(IceRpcError error, string? message, Exception? innerException = null)
        : base(message ?? $"An IceRpc call failed with error '{error}'.", innerException) =>
        IceRpcError = error;

    /// <summary>Constructs a new instance of the <see cref="IceRpcException" /> class.</summary>
    /// <param name="error">The error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public IceRpcException(IceRpcError error, Exception? innerException = null)
        : this(error, message: null, innerException)
    {
    }
}
