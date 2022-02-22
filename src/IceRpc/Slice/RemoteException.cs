// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Base class for exceptions defined in Slice.</summary>
    public abstract class RemoteException : Exception
    {
        /// <inheritdoc/>
        public override string Message => _hasCustomMessage || DefaultMessage == null ? base.Message : DefaultMessage;

        /// <summary>When true, if this exception is thrown from the implementation of an operation, Ice will convert
        /// it into an Ice.UnhandledException. When false, Ice marshals this remote exception as-is. true is the
        /// default for exceptions unmarshaled by Ice, while false is the default for exceptions that did not originate
        /// in a remote server.</summary>
        public bool ConvertToUnhandled { get; set; }

        /// <summary>The remote exception origin.</summary>
        public IncomingResponse? Origin { get; internal set; }

        /// <summary>The remote exception retry policy.</summary>
        public RetryPolicy RetryPolicy { get; } = RetryPolicy.NoRetry;

        /// <summary>When DefaultMessage is not null and the application does not construct the exception with a
        /// constructor that takes a message parameter, Message returns DefaultMessage. This property should be
        /// overridden in derived partial exception classes that provide a custom default message.</summary>
        protected virtual string? DefaultMessage => null;

        private readonly bool _hasCustomMessage;

        /// <summary>Constructs a remote exception with the default system message.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        protected RemoteException(RetryPolicy? retryPolicy = null) => RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;

        /// <summary>Constructs a remote exception with the provided message and inner exception.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        protected RemoteException(
            string? message,
            Exception? innerException = null,
            RetryPolicy? retryPolicy = null)
            : base(message, innerException)
        {
            RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
            _hasCustomMessage = message != null;
        }

        /// <summary>Constructs a remote exception using a decoder.</summary>
        /// <param name="decoder">The decoder.</param>
        protected RemoteException(ref SliceDecoder decoder)
            : base(decoder.Encoding == Encoding.Slice11 ? null : decoder.DecodeString())
        {
            _hasCustomMessage = decoder.Encoding != Encoding.Slice11;
            ConvertToUnhandled = true;
        }

        /// <summary>Decodes a remote exception.</summary>
        /// <param name="decoder">The Slice decoder.</param>
        protected abstract void DecodeCore(ref SliceDecoder decoder);

        /// <summary>Encodes a remote exception.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        protected abstract void EncodeCore(ref SliceEncoder encoder);

        internal void Decode(ref SliceDecoder decoder) => DecodeCore(ref decoder);
        internal void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);
    }
}
