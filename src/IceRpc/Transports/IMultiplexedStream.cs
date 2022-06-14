// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
    public interface IMultiplexedStream : IDuplexPipe
    {
        /// <summary>Gets the stream ID.</summary>
        /// <exception cref="InvalidOperationException">Raised if the stream is not started. Local streams are not
        /// started until data is written. A remote stream is always started.</exception>
        long Id { get; }

        /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
        bool IsBidirectional { get; }

        /// <summary>Gets a value indicating whether the stream is remote. A remote stream is a
        /// stream initiated by the peer and it's returned by <see
        /// cref="IMultiplexedNetworkConnection.AcceptStreamAsync(CancellationToken)"/>.</summary>
        bool IsRemote { get; }

        /// <summary>Gets a value indicating whether the stream is started.</summary>
        bool IsStarted { get; }

        /// <summary>Aborts the stream. This will cause the stream <see cref="IDuplexPipe.Input"/> and <see
        /// cref="IDuplexPipe.Output"/> pipe read and write methods to throw the given exception.</summary>
        /// <param name="exception">The abortion exception.</param>
        void Abort(Exception exception);

        /// <summary>Sets the action which is called when the stream is shutdown.</summary>
        /// <param name="action">The callback to register.</param>
        /// <remarks>If the stream is already shutdown, the callback is called synchronously by this method.</remarks>
        void OnShutdown(Action action);

        /// <summary>Sets the action which is called when the peer <see cref="IDuplexPipe.Input"/> completes.</summary>
        /// <param name="action">The callback to register.</param>
        /// <remarks>If the he peer <see cref="IDuplexPipe.Input"/> is already completed, the callback is called
        /// synchronously by this method.</remarks>
        void OnPeerInputCompleted(Action action);

        /// <summary>Converts the exception given to <see cref="Abort"/>, <see cref="PipeReader.Complete(Exception?)"/>,
        /// <see cref="PipeWriter.Complete(Exception?)"/> etc. into a transport-independent error code.</summary>
        /// <param name="exception">The exception to convert, or null.</param>
        /// <returns>The error code.</returns>
        public static MultiplexedStreamErrorCode ToErrorCode(Exception? exception)
        {
            if (exception == null)
            {
                return MultiplexedStreamErrorCode.NoError;
            }
            else
            {
                return exception switch
                {
                    ConnectionClosedException => MultiplexedStreamErrorCode.ConnectionShutdown,
                    MultiplexedStreamException multiplexedStreamException => multiplexedStreamException.ErrorCode,
                    OperationCanceledException => MultiplexedStreamErrorCode.RequestCanceled,
                    _ => MultiplexedStreamErrorCode.Unspecified
                };
            }
        }

        /// <summary>Converts an error code received from the peer into an exception. This exception can be thrown by
        /// <see cref="PipeReader.ReadAsync(CancellationToken)"/>,
        /// <see cref="PipeWriter.FlushAsync(CancellationToken)"/> and other APIs implemented by this stream.</summary>
        /// <param name="errorCode">The error code to convert into an exception.</param>
        /// <returns>A new exception, or null when the error code received shows no error.</returns>
        public static Exception? FromErrorCode(MultiplexedStreamErrorCode errorCode) =>
            errorCode switch
            {
                MultiplexedStreamErrorCode.NoError => null,
                MultiplexedStreamErrorCode.RequestCanceled =>
                    new OperationCanceledException("request canceled by peer"),
                MultiplexedStreamErrorCode.ConnectionShutdown =>
                    new ConnectionClosedException("connection shutdown by peer"),
                _ => new MultiplexedStreamException(errorCode)
            };
    }
}
