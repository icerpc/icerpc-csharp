// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace ZeroC.Ice.Discovery
{
    /// <summary>This class contains ILogger extensions methods for logging messages in "IceRpc.Discovery" category.
    /// </summary>
    internal static class LoggerExtensions
    {
        private const int FoundAdapterByIdRequestFailed = 0;
        private const int FoundObjectByIdRequestFailed = 1;
        private const int LookupRequestFailed = 2;

        private static readonly Action<ILogger, IFindAdapterByIdReplyPrx, Exception> _foundAdapterByIdRequestFailed =
            LoggerMessage.Define<IFindAdapterByIdReplyPrx>(
                LogLevel.Error,
                new EventId(FoundAdapterByIdRequestFailed, nameof(FoundAdapterByIdRequestFailed)),
                "Ice discovery failed to send foundAdapterById to `{ReplyProxy}'");

        private static readonly Action<ILogger, IFindObjectByIdReplyPrx, Exception> _foundObjectByIdRequestFailed =
            LoggerMessage.Define<IFindObjectByIdReplyPrx>(
                LogLevel.Error,
                new EventId(FoundObjectByIdRequestFailed, nameof(FoundObjectByIdRequestFailed)),
                "Ice discovery failed to send foundObjectById to `{ReplyProxy}'");

        private static readonly Action<ILogger, ILookupPrx, Exception> _lookupRequestFailed =
            LoggerMessage.Define<ILookupPrx>(
                LogLevel.Error,
                new EventId(LookupRequestFailed, nameof(LookupRequestFailed)),
                "Ice discovery failed to send lookup request using lookup `{LookupProxy}'");

        internal static void LogFoundAdapterByIdRequestFailed(
            this ILogger logger,
            IFindAdapterByIdReplyPrx replyProxy,
            Exception ex) =>
            _foundAdapterByIdRequestFailed(logger, replyProxy, ex);

        internal static void LogFoundObjectByIdRequestFailed(
            this ILogger logger,
            IFindObjectByIdReplyPrx replyProxy,
            Exception ex) =>
            _foundObjectByIdRequestFailed(logger, replyProxy, ex);

        internal static void LogLookupRequestFailed(
            this ILogger logger,
            ILookupPrx lookup,
            Exception ex) =>
            _lookupRequestFailed(logger, lookup, ex);
    }
}
