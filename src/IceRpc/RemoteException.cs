// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Text;

namespace IceRpc
{
    /// <summary>Base class for exceptions defined in Slice.</summary>
    [TypeId("::IceRpc::RemoteException")]
    public class RemoteException : Exception
    {
        /// <inheritdoc/>
        public override string Message => _hasCustomMessage || DefaultMessage == null ? base.Message : DefaultMessage;

        private static readonly string _iceTypeId = TypeExtensions.GetIceTypeId(typeof(RemoteException))!;

        /// <summary>When true, if this exception is thrown from the implementation of an operation, Ice will convert
        /// it into an Ice.UnhandledException. When false, Ice marshals this remote exception as-is. true is the
        /// default for exceptions unmarshaled by Ice, while false is the default for exceptions that did not originate
        /// in a remote server.</summary>
        public bool ConvertToUnhandled { get; set; }

        /// <summary>The features of this remote exception when it is thrown by a service dispatch method. The
        /// features are set with the features from <c>Dispatch.ResponseFeatures</c>.</summary>
        public FeatureCollection Features { get; internal set; } = FeatureCollection.Empty;

        /// <summary>The remote exception origin.</summary>
        public RemoteExceptionOrigin Origin { get; internal set; } = RemoteExceptionOrigin.Unknown;

        /// <summary>The remote exception retry policy.</summary>
        public RetryPolicy RetryPolicy { get; } = RetryPolicy.NoRetry;

        /// <summary>When DefaultMessage is not null and the application does not construct the exception with a
        /// constructor that takes a message parameter, Message returns DefaultMessage. This property should be
        /// overridden in derived partial exception classes that provide a custom default message.</summary>
        protected virtual string? DefaultMessage => null;

        private readonly bool _hasCustomMessage;

        /// <summary>Constructs a remote exception with the default system message.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public RemoteException(RetryPolicy? retryPolicy = null) => RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;

        /// <summary>Constructs a remote exception with the provided message and inner exception.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        public RemoteException(
            string? message,
            Exception? innerException = null,
            RetryPolicy? retryPolicy = null)
            : base(message, innerException)
        {
            RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
            _hasCustomMessage = message != null;
        }

        /// <summary>Constructs a remote exception with the provided message and origin.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="origin">The remote exception origin.</param>
        public RemoteException(string? message, RemoteExceptionOrigin origin)
            : base(message)
        {
            Origin = origin;
            _hasCustomMessage = message != null;
        }

        /// <summary>Constructs a remote exception using a decoder.</summary>
        /// <param name="decoder">The decoder.</param>
        public RemoteException(ref IceDecoder decoder)
            : base(decoder.Encoding == Encoding.Ice11 ? null : decoder.DecodeString())
        {
            if (decoder.Encoding != Encoding.Ice11)
            {
                Origin = new RemoteExceptionOrigin(ref decoder);
                _hasCustomMessage = true;
            }
            ConvertToUnhandled = true;
        }

        /// <summary>Decodes a remote exception from an <see cref="IceDecoder"/>.</summary>
        /// <param name="decoder">The decoder.</param>
        // This implementation is only called on a plain RemoteException.
        protected virtual void IceDecode(ref IceDecoder decoder)
        {
        }

        /// <summary>Encodes a remote exception to an <see cref="IceEncoder"/>.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        protected virtual void IceEncode(ref IceEncoder encoder)
        {
            if (encoder.Encoding == Encoding.Ice11)
            {
                encoder.IceStartSlice(_iceTypeId);
                encoder.IceEndSlice(lastSlice: true);
            }
            else
            {
                encoder.EncodeString(_iceTypeId);
                encoder.EncodeString(Message);
                Origin.Encode(ref encoder);
            }
        }

        internal void Decode(ref IceDecoder decoder) => IceDecode(ref decoder);
        internal void Encode(ref IceEncoder encoder) => IceEncode(ref encoder);
    }

    public readonly partial record struct RemoteExceptionOrigin
    {
        /// <summary>With the Ice 1.1 encoding, <c>Unknown</c> is used as the remote exception origin for exceptions
        /// other than <see cref="ServiceNotFoundException"/> and <see cref="OperationNotFoundException"/>.</summary>
        public static readonly RemoteExceptionOrigin Unknown = new("", "", "");
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
                    sb.Append("' while attempting to dispatch operation '");
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
