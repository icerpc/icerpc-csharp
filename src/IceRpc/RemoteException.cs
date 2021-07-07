// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Diagnostics;
using System.Text;

namespace IceRpc
{
    /// <summary>The retry policy that can be specified when constructing a remote exception.</summary>
    public readonly struct RetryPolicy : IEquatable<RetryPolicy>
    {
        /// <summary>The retry policy ability for retrying.</summary>
        public readonly Retryable Retryable;
        /// <summary>The retry policy delay to apply for retries.</summary>
        public readonly TimeSpan Delay;

        /// <summary>The NoRetry policy specifies that the exception cannot be retried. This is the default policy
        /// when no policy is specified.</summary>
        public static readonly RetryPolicy NoRetry = new(Retryable.No);

        /// <summary>The OtherReplica policy specifies that the exception can be retried on a different replica.
        /// </summary>
        public static readonly RetryPolicy OtherReplica = new(Retryable.OtherReplica);

        /// <summary>The Immediately policy specifies that the exception can be retried without any delay.</summary>
        public static readonly RetryPolicy Immediately = new(Retryable.AfterDelay, TimeSpan.Zero);

        /// <summary>Creates a retry policy that specifies that the exception can be retried after the given delay.</summary>
        /// <param name="delay">The delay after which the exception can be retried.</param>
        /// <returns>The retry policy.</returns>
        public static RetryPolicy AfterDelay(TimeSpan delay) => new(Retryable.AfterDelay, delay);

        /// <inheritdoc/>
        public bool Equals(RetryPolicy other) => Retryable == other.Retryable && Delay == other.Delay;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is RetryPolicy other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Retryable, Delay);

        /// <inheritdoc/>
        public override string? ToString() => Retryable switch
        {
            Retryable.AfterDelay => $"after {Delay.ToPropertyValue()} delay",
            Retryable.OtherReplica => "other replica",
            Retryable.No => "no retry",
            _ => "unknown"
        };

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(RetryPolicy lhs, RetryPolicy rhs) => lhs.Equals(rhs);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(RetryPolicy lhs, RetryPolicy rhs) => !(lhs == rhs);

        internal RetryPolicy(IceDecoder iceDecoder)
        {
            Retryable = iceDecoder.ReadRetryable();
            Delay = Retryable == Retryable.AfterDelay ? TimeSpan.FromMilliseconds(iceDecoder.ReadVarULong()) : TimeSpan.Zero;
        }

        private RetryPolicy(Retryable retryable, TimeSpan delay = default)
        {
            Retryable = retryable;
            Delay = delay;
        }
    }

    /// <summary>Base class for exceptions that can be transmitted in responses to Ice requests. The derived exception
    /// classes are generated from exceptions defined in Slice.</summary>
    public class RemoteException : Exception
    {
        /// <inheritdoc/>
        public override string Message => _hasCustomMessage || DefaultMessage == null ? base.Message : DefaultMessage;

        /// <summary>When true, if this exception is thrown from the implementation of an operation, Ice will convert
        /// it into an Ice.UnhandledException. When false, Ice marshals this remote exception as-is. true is the
        /// default for exceptions unmarshaled by Ice, while false is the default for exceptions that did not originate
        /// in a remote server.</summary>
        public bool ConvertToUnhandled { get; set; }

        /// <summary>The features of this remote exception when it is thrown by a service dispatch  method. The
        /// features are set with the features from <c>Dispatch.ResponseFeatures</c>.</summary>
        public FeatureCollection Features { get; internal set; } = FeatureCollection.Empty;

        /// <summary>The remote exception origin.</summary>
        public RemoteExceptionOrigin Origin { get; internal set; } = RemoteExceptionOrigin.Unknown;

        /// <summary>The remote exception retry policy.</summary>
        public RetryPolicy RetryPolicy { get; }

        /// <summary>When DefaultMessage is not null and the application does not construct the exception with a
        /// constructor that takes a message parameter, Message returns DefaultMessage. This property should be
        /// overridden in derived partial exception classes that provide a custom default message.</summary>
        protected virtual string? DefaultMessage => null;

        /// <summary>Returns the sliced data if the exception has a preserved-slice base exception and has been sliced
        /// during unmarshaling, <c>null</c> is returned otherwise.</summary>
        protected SlicedData? IceSlicedData { get; set; }
        internal SlicedData? SlicedData => IceSlicedData;

        private readonly bool _hasCustomMessage;

        /// <summary>Constructs a remote exception with the default system message.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        protected RemoteException(RetryPolicy retryPolicy = default) => RetryPolicy = retryPolicy;

        /// <summary>Constructs a remote exception with the provided message and inner exception.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        protected internal RemoteException(
            string? message,
            Exception? innerException = null,
            RetryPolicy retryPolicy = default)
            : base(message, innerException)
        {
            RetryPolicy = retryPolicy;
            _hasCustomMessage = message != null;
        }

        /// <summary>Constructs a remote exception with the provided message and origin.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="origin">The remote exception origin.</param>
        protected internal RemoteException(string? message, RemoteExceptionOrigin origin)
            : base(message)
        {
            Origin = origin;
            _hasCustomMessage = message != null;
        }

        /// <summary>Reads a remote exception from the <see cref="IceDecoder"/>. This base implementation is only
        /// called on a plain RemoteException.</summary>
        /// <param name="iceDecoder">The Ice decoder.</param>
        /// <param name="firstSlice"><c>True</c> if the exception corresponds to the first Slice, <c>False</c>
        /// otherwise.</param>
        protected virtual void IceRead(IceDecoder iceDecoder, bool firstSlice)
        {
            Debug.Assert(firstSlice);
            IceSlicedData = iceDecoder.SlicedData;
            ConvertToUnhandled = true;
        }

        internal void Read(IceDecoder iceDecoder) => IceRead(iceDecoder, true);

        /// <summary>Writes a remote exception to the <see cref="IceEncoder"/>. This implementation can only be
        /// called on a plain RemoteException with IceSlicedData set.</summary>
        /// <param name="writer">The Ice encoder.</param>
        /// <param name="firstSlice"><c>True</c> if the exception corresponds to the first Slice, <c>False</c>
        /// otherwise.</param>
        protected virtual void IceWrite(IceEncoder writer, bool firstSlice) =>
            writer.WriteSlicedData(IceSlicedData!.Value, Array.Empty<string>(), Message, Origin);

        internal void Write(IceEncoder writer) => IceWrite(writer, true);
    }

    /// <summary>Provides public extensions methods for RemoteException instances.</summary>
    public static class RemoteExceptionExtensions
    {
        /// <summary>During unmarshaling, Ice slices off derived slices that it does not know how to read, and preserves
        /// these "unknown" slices.</summary>
        /// <returns>A SlicedData value that provides the list of sliced-off slices.</returns>
        public static SlicedData? GetSlicedData(this RemoteException ex) => ex.SlicedData;
    }

    public partial struct RemoteExceptionOrigin
    {
        /// <summary>With the Ice 1.1 encoding, <c>Unknown</c> is used as the remote exception origin for exceptions
        /// other than <see cref="ServiceNotFoundException"/> and <see cref="OperationNotFoundException"/>.</summary>
        public static readonly RemoteExceptionOrigin Unknown = new("", "");
    }

    public partial class ServiceNotFoundException
    {
        /// <summary>The service facet.</summary>
        protected internal string Facet { get; init; } = "";

        /// <inheritdoc/>
        protected override string? DefaultMessage
        {
            get
            {
                if (Origin != RemoteExceptionOrigin.Unknown)
                {
                    var sb = new StringBuilder("could not find service '");
                    sb.Append(Origin.Path);
                    sb.Append('\'');
                    if (Facet.Length > 0)
                    {
                        sb.Append(" with facet '");
                        sb.Append(Facet);
                        sb.Append('\'');
                    }
                    sb.Append(" while attempting to dispatch operation '");
                    sb.Append(Origin.Operation);
                    sb.Append('\'');
                    return sb.ToString();
                }
                else
                {
                    return null;
                }
            }
        }
    }

    public partial class OperationNotFoundException
    {
        /// <summary>The service facet.</summary>
        protected internal string Facet { get; init; } = "";

        /// <inheritdoc/>
        protected override string? DefaultMessage
        {
            get
            {
                if (Origin != RemoteExceptionOrigin.Unknown)
                {
                    var sb = new StringBuilder("could not find operation '");
                    sb.Append(Origin.Operation);
                    sb.Append("' for service '");
                    sb.Append(Origin.Path);
                    sb.Append('\'');
                    if (Facet.Length > 0)
                    {
                        sb.Append(" with facet '");
                        sb.Append(Facet);
                        sb.Append('\'');
                    }
                    return sb.ToString();
                }
                else
                {
                    return null;
                }
            }
        }
    }

    public partial class UnhandledException : RemoteException
    {
        /// <summary>Constructs a new exception where the cause is a remote exception. The remote exception features
        /// are inherited and set on this UnhandledException.</summary>
        /// <param name="innerException">The remote exception that is the cause of the current exception.</param>
        public UnhandledException(RemoteException innerException)
            : base(message: null, innerException) =>
            // Inherit the features of the unhandled remote exception.
            Features = innerException.Features;

        /// <summary>Constructs a new exception.</summary>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public UnhandledException(Exception innerException)
            : base(message: null, innerException)
        {
        }

        /// <inheritdoc/>
        protected override string? DefaultMessage
        {
            get
            {
                string message = "unhandled exception";
                if (Origin != RemoteExceptionOrigin.Unknown)
                {
                    message += $" while dispatching '{Origin.Operation}' on service '{Origin.Path}'";
                }
#if DEBUG
                message += $":\n{InnerException}\n---";
#else
                // The stack trace of the inner exception can include sensitive information we don't want to send
                // "over the wire" in non-debug builds.
                message += $":\n{InnerException!.Message}";
#endif
                return message;
            }
        }
    }
}
