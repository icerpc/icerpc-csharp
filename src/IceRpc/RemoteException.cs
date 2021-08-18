// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;
using System.Text;

namespace IceRpc
{
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

        internal SlicedData? SlicedData { get; set; }

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

        /// <summary>Decodes a remote exception from an <see cref="Ice11Decoder"/>.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        // This implementation is only called on a plain RemoteException.
        protected virtual void IceDecode(Ice11Decoder decoder) => ConvertToUnhandled = true;

        /// <summary>Decodes a remote exception from an <see cref="Ice20Decoder"/>.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        protected virtual void IceDecode(Ice20Decoder decoder) => ConvertToUnhandled = true;

        /// <summary>Encodes a remote exception to an <see cref="Ice11Encoder"/>.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        // This implementation is only called on a plain RemoteException.
        protected virtual void IceEncode(Ice11Encoder encoder) =>
            encoder.EncodeSlicedData(SlicedData!.Value, fullySliced: true);

        /// <summary>Encodes a remote exception to an <see cref="Ice20Encoder"/>.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        protected virtual void IceEncode(Ice20Encoder encoder) => Debug.Assert(false);

        internal void Decode(Ice11Decoder decoder) => IceDecode(decoder);
        internal void Decode(Ice20Decoder decoder) => IceDecode(decoder);
        internal void Encode(Ice11Encoder encoder) => IceEncode(encoder);
        internal void Encode(Ice20Encoder encoder) => IceEncode(encoder);
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
        /// <inheritdoc/>
        protected override string? DefaultMessage
        {
            get
            {
                if (Origin != RemoteExceptionOrigin.Unknown)
                {
                    var sb = new StringBuilder("could not find service '");
                    sb.Append(Origin.Path);
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
