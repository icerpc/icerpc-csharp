// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports;

/// <summary>Provides an extension method for <see cref="SocketException"/> to convert it into an <see
/// cref="IceRpcException"/>.</summary>
public static class SocketExceptionExtensions
{
    /// <summary>Converts a <see cref="SocketException"/> into an <see cref="IceRpcException" />.</summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="innerException">The inner exception for the <see cref="IceRpcException"/>, when
    /// <see langword="null"/> <paramref name="exception"/> is used as the inner exception.</param>
    /// <returns>The <see cref="IceRpcException"/> created from the <see cref="SocketException"/>.</returns>
    public static IceRpcException ToIceRpcException(this SocketException exception, Exception? innerException = null)
    {
        innerException ??= exception;
        IceRpcError errorCode = exception.SocketErrorCode switch
        {
            // Address is already in use when attempting to bind a listening socket.
            SocketError.AddressAlreadyInUse => IceRpcError.AddressInUse,

            // ConnectionAborted is reported by the OS and is often triggered by the peer or by a network failure.
            SocketError.ConnectionAborted => IceRpcError.ConnectionAborted,

            // Shutdown matches EPIPE and ConnectionReset matches ECONNRESET. Both are the result of the peer
            // closing the connection non-gracefully.
            //
            // EPIPE is returned when writing to a socket that has been closed and the send buffer is empty.
            // ECONNRESET is returned when the connection is reset while data is still pending in the send buffer.
            //
            // In both cases the connection can no longer be used.
            SocketError.ConnectionReset => IceRpcError.ConnectionAborted,
            SocketError.Shutdown => IceRpcError.ConnectionAborted,

            // NetworkReset indicates that an established connection became invalid due to a network event
            // (for example interface reset, Wi-Fi reconnect, VPN reconnect).
            SocketError.NetworkReset => IceRpcError.ConnectionAborted,

            // The server is reachable but no process is listening on the target port.
            SocketError.ConnectionRefused => IceRpcError.ConnectionRefused,

            // These errors indicate the remote server cannot be reached. This includes routing failures,
            // local network failures, OS-level TCP timeouts, and host reachability failures.
            //
            // The TimedOut error refers to the OS TCP stack exhausting SYN retransmissions when
            // attempting to connect — not to IceRPC-level timeouts, which result in
            // OperationCanceledException via CancellationToken.
            //
            // With the async socket API used by IceRPC, these errors occur during connection
            // establishment. On an established TCP connection, the kernel absorbs ICMP errors
            // and retransmits internally; the application sees ConnectionReset (mapped to
            // ConnectionAborted above) rather than these reachability errors.
            //
            // They are grouped as ServerUnreachable because from the RPC perspective the connection
            // cannot be established.
            SocketError.HostUnreachable => IceRpcError.ServerUnreachable,
            SocketError.NetworkUnreachable => IceRpcError.ServerUnreachable,
            SocketError.NetworkDown => IceRpcError.ServerUnreachable,
            SocketError.HostDown => IceRpcError.ServerUnreachable,
            SocketError.TimedOut => IceRpcError.ServerUnreachable,

            // Name resolution failures are also mapped to ServerUnreachable so that the connection cache
            // can treat DNS failures and transport reachability failures uniformly and try alternate
            // server addresses.
            //
            // These errors always occur before connection establishment (during name resolution).
            //
            // Some of these errors (for example HostNotFound) are often permanent configuration issues,
            // but IceRPC keeps the public error model small and leaves retry decisions to higher layers.
            SocketError.HostNotFound => IceRpcError.ServerUnreachable,
            SocketError.TryAgain => IceRpcError.ServerUnreachable,
            SocketError.NoRecovery => IceRpcError.ServerUnreachable,
            SocketError.NoData => IceRpcError.ServerUnreachable,

            // Operation was aborted locally, typically due to cancellation or socket disposal.
            SocketError.OperationAborted => IceRpcError.OperationAborted,

            _ => IceRpcError.IceRpcError
        };

        return new IceRpcException(errorCode, innerException);
    }
}
