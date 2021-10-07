// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>Protocol identifies the protocol used by IceRpc connections.</summary>
    public class Protocol : IEquatable<Protocol>
    {
        /// <summary>The ice1 protocol supported by all Ice versions since Ice 1.0.</summary>
        public static readonly Protocol Ice1 = Ice1Protocol.Instance;

        /// <summary>The ice2 protocol introduced in IceRpc.</summary>
        public static readonly Protocol Ice2 = Ice2Protocol.Instance;

        /// <summary>The protocol code of this protocol.</summary>
        public ProtocolCode Code { get; }

        /// <summary>Returns <c>true</c> if this protocol is supported by this IceRpc runtime, <c>false</c>
        /// otherwise.</summary>
        public virtual bool IsSupported => false;

        /// <summary>The name of this protocol, for example "ice2" for the Ice2 protocol.</summary>
        public string Name { get; }

        private protected const string Ice1Name = "ice1";
        private protected const string Ice2Name = "ice2";

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Protocol? lhs, Protocol? rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null || rhs is null)
            {
                return false;
            }
            return lhs.Equals(rhs);
        }

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Protocol? lhs, Protocol? rhs) => !(lhs == rhs);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Protocol value && Equals(value);

        /// <summary>Checks if this encoding is equal to another encoding.</summary>
        /// <param name="other">The other encoding.</param>
        /// <returns><c>true</c>when the two encodings have the same name; otherwise, <c>false</c>.</returns>
        public bool Equals(Protocol? other) => Name == other?.Name;

        /// <summary>Computes the hash code for this encoding.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        /// <summary>Converts this encoding into a string.</summary>
        /// <returns>The name of the encoding.</returns>
        public override string ToString() => Name;

        /// <summary>Returns a Protocol with the given major and minor versions. This method always succeeds.
        /// </summary>
        public static Protocol FromProtocolCode(ProtocolCode code) =>
            code switch
            {
                ProtocolCode.Ice1 => Ice1,
                ProtocolCode.Ice2 => Ice2,
                _ => new Protocol(code, $"{code}")
            };

        /// <summary>Returns a Protocol with the given name. This method always succeeds.</summary>
        /// <param name="name">The name of the protocol.</param>
        /// <returns>One of the well-known Protocol instance (Ice1, Ice2, etc.) when the name matches;
        /// otherwise, a new Protocol instance.</returns>
        public static Protocol FromString(string name) =>
            name switch
            {
                Ice1Name => Ice1,
                Ice2Name => Ice2,
                _ => TryParseProtocolCode(name)
            };

        internal virtual ValueTask<IProtocolConnection> CreateConnectionAsync(
            INetworkConnection networkConnection,
            int incomingFrameMaxSize,
            ILoggerFactory loggerFactory,
            CancellationToken cancel) =>
                throw new NotSupportedException(
                    @$"Ice protocol '{Name
                    }' is not supported by this IceRPC runtime ({typeof(Protocol).Assembly.GetName().Version})");

        /// <summary>Creates an outgoing response with the exception. With the ice1 protocol, this method sets
        /// the <see cref="ReplyStatus"/> feature. This method also sets the <see
        /// cref="FieldKey.RetryPolicy"/> if an exception retry policy is set.</summary>
        internal virtual OutgoingResponse CreateResponseFromException(Exception exception,  IncomingRequest request)
        {
            RemoteException? remoteException = exception as RemoteException;
            if (remoteException == null || remoteException.ConvertToUnhandled)
            {
                remoteException = new UnhandledException(exception);
            }

            if (remoteException.Origin == RemoteExceptionOrigin.Unknown)
            {
                remoteException.Origin = new RemoteExceptionOrigin(request.Path, request.Operation);
            }

            return CreateResponseFromRemoteException(remoteException, request.GetIceEncoding());
        }

        internal virtual OutgoingResponse CreateResponseFromRemoteException(
            RemoteException remoteException,
            IceEncoding payloadEncoding) =>
            throw new NotSupportedException($"can't create response for unknown protocol {this}");

        /// <summary>Returns the Ice encoding that this protocol uses for its headers. It's also used as the
        /// default encoding for endpoints that don't explicitly specify an encoding.</summary>
        /// <returns>The Ice encoding, or null if the protocol does not use a known Ice encoding.</returns>
        internal virtual IceEncoding? GetIceEncoding() => null;

        /// <summary>Specifies whether or not the protocol supports fields in protocol frame
        /// headers.</summary>
        /// <returns><c>true</c> if the protocol supports fields.</returns>
        internal virtual bool HasFieldSupport() => false;

        private protected Protocol(ProtocolCode code, string name)
        {
            Code = code;
            Name = name;
        }

        private static Protocol TryParseProtocolCode(string str)
        {
            if (str.EndsWith(".0", StringComparison.Ordinal))
            {
                str = str[0..^2];
            }
            if (byte.TryParse(str, out byte value))
            {
                return FromProtocolCode((ProtocolCode)value);
            }
            else
            {
                throw new FormatException($"invalid protocol '{str}'");
            }
        }
    }
}
