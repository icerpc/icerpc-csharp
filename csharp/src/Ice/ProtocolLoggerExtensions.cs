// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ZeroC.Ice
{
    internal static class ProtocolLoggerExtensions
    {
        internal const int ReceivedIce1CloseConnectionFrame = 0;
        internal const int ReceivedIce1RequestBatchFrame = 1;
        internal const int ReceivedIce1RequestFrame = 2;
        internal const int ReceivedIce1ResponseFrame = 3;
        internal const int ReceivedIce1ValidateConnectionFrame = 4;

        internal const int ReceivedIce2GoAwayFrame = 5;
        internal const int ReceivedIce2InitializeFrame = 6;
        internal const int ReceivedIce2RequestFrame = 7;
        internal const int ReceivedIce2ResponseFrame = 8;

        internal const int RequestDispatchException = 9;

        internal const int RetryRequestInvocation = 10;
        internal const int RetryConnectionEstablishment = 11;

        internal const int SendIce1ValidateConnectionFrame = 12;
        internal const int SendingIce1CloseConnectionFrame = 13;
        internal const int SendingIce1RequestFrame = 14;
        internal const int SendingIce1ResponseFrame = 15;

        internal const int SendingIce2GoAwayFrame = 16;
        internal const int SendingIce2InitializeFrame = 17;
        internal const int SendingIce2RequestFrame = 18;
        internal const int SendingIce2ResponseFrame = 19;

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce1CloseConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(ReceivedIce1CloseConnectionFrame, nameof(ReceivedIce1CloseConnectionFrame)),
                "received ice1 close connection frame: encoding = {Encoding}");

        private static readonly Action<ILogger, int, Exception> _receivedIce1RequestBatchFrame =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                new EventId(ReceivedIce1RequestBatchFrame, nameof(ReceivedIce1RequestBatchFrame)),
                "received ice1 request batch frame: number of requests = `{NumberOfRequests}'");

        private static readonly Action<ILogger, RequestScope, Exception> _receivedIce1RequestFrame =
            LoggerMessage.Define<RequestScope>(
                LogLevel.Information,
                new EventId(ReceivedIce1RequestFrame, nameof(ReceivedIce1RequestFrame)),
                "received ice1 request frame: {Request}");

        private static readonly Action<ILogger, ResultType, int, Exception> _receivedIce1ResponseFrame =
            LoggerMessage.Define<ResultType, int>(
                LogLevel.Information,
                new EventId(ReceivedIce1ResponseFrame, nameof(ReceivedIce1ResponseFrame)),
                "received ice1 response frame: result = {Result}, request ID = {RequestID}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce1ValidateConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(ReceivedIce1ValidateConnectionFrame, nameof(ReceivedIce1ValidateConnectionFrame)),
                "received ice1 validate connection frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce2GoAwayFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(ReceivedIce2GoAwayFrame, nameof(ReceivedIce2GoAwayFrame)),
                "received ice2 go away frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce2InitializeFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(ReceivedIce2InitializeFrame, nameof(ReceivedIce2InitializeFrame)),
                "received ice2 initialize frame: encoding = {Encoding}");

        private static readonly Action<ILogger, RequestScope, Exception> _receivedIce2RequestFrame =
            LoggerMessage.Define<RequestScope>(
                LogLevel.Information,
                new EventId(ReceivedIce2RequestFrame, nameof(ReceivedIce2RequestFrame)),
                "received ice2 request frame: {Request}");

        private static readonly Action<ILogger, ResultType, long, Exception> _receivedIce2ResponseFrame =
            LoggerMessage.Define<ResultType, long>(
                LogLevel.Information,
                new EventId(ReceivedIce2ResponseFrame, nameof(ReceivedIce2ResponseFrame)),
                "received ice2 response frame: result = {Result}, stream ID = {StreamID}");

        private static readonly Action<ILogger, Exception> _requestDispatchException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(RequestDispatchException, nameof(RequestDispatchException)),
            "dispatch exception");

        private static readonly Action<ILogger, RetryPolicy, int, int, Exception> _retryRequestInvocation =
            LoggerMessage.Define<RetryPolicy, int, int>(
                LogLevel.Debug,
                new EventId(RetryRequestInvocation, nameof(RetryRequestInvocation)),
                "retrying request because of retryable exception: retry policy = {RetryPolicy}, " +
                "request attempt = {Attempt} / {MaxAttempts}");

        private static readonly Action<ILogger, RetryPolicy, int, int, Exception> _retryConnectionEstablishmentAfterTryingAllEndpoints =
            LoggerMessage.Define<RetryPolicy, int, int>(
                LogLevel.Debug,
                new EventId(RetryConnectionEstablishment, nameof(RetryConnectionEstablishment)),
                "retrying connection establishment because of retryable exception: retry policy = {RetryPolicy}, " +
                "request attempt = {Attempt} / {MaxAttempts}");

        private static readonly Action<ILogger, Exception> _retryConnectionEstablishment = LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(RetryConnectionEstablishment, nameof(RetryConnectionEstablishment)),
            "retrying connection establishment because of retryable exception");

        private static readonly Func<ILogger, IReadOnlyList<KeyValuePair<string, object>>, IDisposable> _requestScope =
            LoggerMessage.DefineScope<IReadOnlyList<KeyValuePair<string, object>>>("request {Request}");

        private static readonly Action<ILogger, Encoding, Exception> _sendIce1ValidateConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(SendIce1ValidateConnectionFrame, nameof(SendIce1ValidateConnectionFrame)),
                "sent ice1 validate connection frame: encoding = `{Encoding}'");

        private static readonly Action<ILogger, Encoding, Exception> _sendingIce1CloseConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(SendingIce1CloseConnectionFrame, nameof(SendingIce1CloseConnectionFrame)),
                "sending ice1 close connection frame: encoding = {Encoding}");

        private static readonly Action<ILogger, RequestScope, Exception> _sendingIce1RequestFrame =
            LoggerMessage.Define<RequestScope>(
                LogLevel.Debug,
                new EventId(SendingIce1RequestFrame, nameof(SendingIce1RequestFrame)),
                "sending ice1 request frame: {Request}");

        private static readonly Func<ILogger, Encoding, int, int, IDisposable> _ice1RequestsScope =
            LoggerMessage.DefineScope<Encoding, int, int>(
                "request: encoding {Encoding}, frame size = {FrameSize}, request ID = {RequestID}");

        private static readonly Action<ILogger, ResultType, int, Exception> _sendingIce1ResponseFrame =
            LoggerMessage.Define<ResultType, int>(
                LogLevel.Information,
                new EventId(SendingIce1ResponseFrame, nameof(SendingIce1ResponseFrame)),
                "sending ice1 response frame: result = {Result}, request ID = {RequestID}");

        private static readonly Action<ILogger, Encoding, Exception> _sendingIce2GoAwayFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(SendingIce2GoAwayFrame, nameof(SendingIce2GoAwayFrame)),
                "sending ice2 go away frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _sendingIce2InitializeFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                new EventId(SendingIce2InitializeFrame, nameof(SendingIce2InitializeFrame)),
                "sending ice2 initialize frame: encoding = {Encoding}");

        private static readonly Action<ILogger, RequestScope, Exception> _sendingIce2RequestFrame =
            LoggerMessage.Define<RequestScope>(
                LogLevel.Information,
                new EventId(SendingIce2RequestFrame, nameof(SendingIce2RequestFrame)),
                "sending ice2 request frame: {Request}");

        private static readonly Action<ILogger, ResultType, long, Exception> _sendingIce2ResponseFrame =
            LoggerMessage.Define<ResultType, long>(
                LogLevel.Information,
                new EventId(SendingIce2ResponseFrame, nameof(SendingIce2ResponseFrame)),
                "sending ice2 response frame: result = {Result}, stream ID = {StreamID}");

        internal static void LogReceivedIce1CloseConnectionFrame(this ILogger logger) =>
            _receivedIce1CloseConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogReceivedIce1RequestBatchFrame(this ILogger logger, int requests) =>
            _receivedIce1RequestBatchFrame(logger, requests, null!);

        internal static void LogReceivedIce1ValidateConnectionFrame(this ILogger logger) =>
            _receivedIce1ValidateConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogReceivedIce2GoAwayFrame(this ILogger logger) =>
            _receivedIce2GoAwayFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogReceivedIce2InitializeFrame(this ILogger logger) =>
            _receivedIce2InitializeFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogReceivedRequest(this ILogger logger, IncomingRequestFrame request, long streamID)
        {
            if (request.Protocol == Protocol.Ice1)
            {
                _receivedIce1RequestFrame(logger, new RequestScope(request, streamID), null!);
            }
            else
            {
                _receivedIce2RequestFrame(logger, new RequestScope(request, streamID), null!);
            }
        }

        internal static void LogReceivedResponse(this ILogger logger, long streamId, IncomingResponseFrame response)
        {
            if (response.Protocol == Protocol.Ice1)
            {
                _receivedIce1ResponseFrame(logger, response.ResultType, GetIce1RequestID(streamId), null!);
            }
            else
            {
                _receivedIce2ResponseFrame(logger, response.ResultType, streamId, null!);
            }
        }

        internal static void LogRetryRequestInvocation(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex) =>
            _retryRequestInvocation(logger, retryPolicy, attempt, maxAttempts, ex!);

        // TODO trace remote exception, currently we pass null because the remote exception is not unmarshaled at this point
        internal static void LogRetryConnectionEstablishment(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex) =>
            _retryConnectionEstablishmentAfterTryingAllEndpoints(logger, retryPolicy, attempt, maxAttempts, ex!);

        internal static void LogRetryConnectionEstablishment(this ILogger logger, Exception? ex) =>
            _retryConnectionEstablishment(logger, ex!);

        internal static void LogRequestDispatchException(this ILogger logger, Exception ex) =>
            _requestDispatchException(logger, ex);

        internal static void LogSendIce1ValidateConnectionFrame(this ILogger logger) =>
            _sendIce1ValidateConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogSendingIce1CloseConnectionFrame(this ILogger logger) =>
            _sendingIce1CloseConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogSendingIce2GoAwayFrame(this ILogger logger) =>
            _sendingIce2GoAwayFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogSendingIce2InitializeFrame(this ILogger logger) =>
            _sendingIce2InitializeFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogSendingRequest(this ILogger logger, OutgoingRequestFrame request, long streamID)
        {
            var requestScope = new RequestScope(request, streamID);
            using (_requestScope(logger, requestScope))
            {
                if (request.Protocol == Protocol.Ice1)
                {
                    _sendingIce1RequestFrame(logger, requestScope, null!);
                }
                else
                {
                    _sendingIce2RequestFrame(logger, requestScope, null!);
                }
            }
        }

        internal static void LogSendingResponse(this ILogger logger, long streamId, OutgoingResponseFrame response)
        {
            if (response.Protocol == Protocol.Ice1)
            {
                _sendingIce1ResponseFrame(logger, response.ResultType, GetIce1RequestID(streamId), null!);
            }
            else
            {
                _sendingIce2ResponseFrame(logger, response.ResultType, streamId, null!);
            }
        }

        internal static void LogSendingResponse(this ILogger logger, long streamId, IncomingResponseFrame response)
        {
            if (response.Protocol == Protocol.Ice1)
            {
                _receivedIce1ResponseFrame(logger, response.ResultType, GetIce1RequestID(streamId), null!);
            }
            else
            {
                _receivedIce2ResponseFrame(logger, response.ResultType, streamId, null!);
            }
        }

        private static int GetIce1RequestID(long streamId) => streamId % 4 < 2 ? (int)(streamId >> 2) + 1 : 0;

        internal class RequestScope : IReadOnlyList<KeyValuePair<string, object>>
        {
            private const string ContextKey = "Context";
            private const string IdempotentKey = "Idempotent";
            private const string IdentityKey = "Identity";
            private const string OperationKey = "Operation";
            private const string PayloadEncodingKey = "PayloadEncoding";
            private const string PayloadSizeKey = "PayloadSize";
            private const string RequestIDKey = "RequestID";
            private const string StreamIDKey = "StreamID";

            private IReadOnlyDictionary<string, string> _context;
            private bool _idempotent;
            private Identity _identity;
            private string _operation;
            private Encoding _payloadEncoding;
            private int _payloadSize;
            private Protocol _protocol;
            private long _streamID;

            private string? _cached;

            internal RequestScope(OutgoingRequestFrame request, long streamID)
            {
                _context = request.Context;
                _idempotent = request.IsIdempotent;
                _identity = request.Identity;
                _operation = request.Operation;
                _payloadSize = request.PayloadSize;
                _payloadEncoding = request.PayloadEncoding;
                _protocol = request.Protocol;
                _streamID = streamID;
            }

            internal RequestScope(IncomingRequestFrame request, long streamID)
            {
                _context = request.Context;
                _idempotent = request.IsIdempotent;
                _identity = request.Identity;
                _operation = request.Operation;
                _payloadSize = request.PayloadSize;
                _payloadEncoding = request.PayloadEncoding;
                _streamID = streamID;
            }

            public KeyValuePair<string, object> this[int index] =>
                index switch
                {
                    0 => new KeyValuePair<string, object>(ContextKey, _context),
                    1 => new KeyValuePair<string, object>(IdempotentKey, _idempotent),
                    2 => new KeyValuePair<string, object>(IdentityKey, _identity),
                    3 => new KeyValuePair<string, object>(OperationKey, _operation),
                    4 => new KeyValuePair<string, object>(PayloadSizeKey, _payloadSize),
                    5 => new KeyValuePair<string, object>(PayloadEncodingKey, _payloadEncoding),
                    6 => _protocol == Protocol.Ice1 ?
                        new KeyValuePair<string, object>(RequestIDKey, GetIce1RequestID(_streamID)) :
                        new KeyValuePair<string, object>(StreamIDKey, _streamID),
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };

            public int Count => 7;

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                for (var i = 0; i < Count; ++i)
                {
                    yield return this[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString()
            {
                if (_cached == null)
                {
                    var sb = new StringBuilder();
                    if (_protocol == Protocol.Ice1)
                    {
                        sb.Append("Request ID = ").Append(GetIce1RequestID(_streamID)).Append(", ");
                    }
                    else
                    {
                        sb.Append("Stream ID = ").Append(_streamID).Append(", ");
                    }
                    sb.Append("Operation = ").Append(_operation).Append(", ");
                    sb.Append("Identity = ").Append(_identity).Append(", ");
                    sb.Append("Payload ize = ").Append(_payloadSize).Append(", ");
                    sb.Append("Payload Encoding = ").Append(_payloadEncoding);
                    _cached = sb.ToString();
                }

                return _cached;
            }
        }
    }
}
