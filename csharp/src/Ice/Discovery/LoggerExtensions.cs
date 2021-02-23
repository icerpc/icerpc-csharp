// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace ZeroC.Ice.Discovery
{
    internal static class LoggerExtensions
    {
        private static readonly Action<ILogger, IFindAdapterByIdReplyPrx, Exception> _foundAdapterByIdRequestFailed =
            LoggerMessage.Define<IFindAdapterByIdReplyPrx>(
                LogLevel.Error,
                GetEventId(DiscoveryEvent.FoundAdapterByIdRequestFailed),
                "Ice discovery failed to send foundAdapterById to `{ReplyProxy}'");

        private static readonly Action<ILogger, IFindObjectByIdReplyPrx, Exception> _foundObjectByIdRequestFailed =
            LoggerMessage.Define<IFindObjectByIdReplyPrx>(
                LogLevel.Error,
                GetEventId(DiscoveryEvent.FoundObjectByIdRequestFailed),
                "Ice discovery failed to send foundObjectById to `{ReplyProxy}'");

        private static readonly Action<ILogger, ILookupPrx, Exception> _lookupRequestFailed =
            LoggerMessage.Define<ILookupPrx>(
                LogLevel.Error,
                GetEventId(DiscoveryEvent.LookupRequestFailed),
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

        private static EventId GetEventId(DiscoveryEvent e) => new((int)e, e.ToString());

        private enum DiscoveryEvent
        {
            FoundAdapterByIdRequestFailed,
            FoundObjectByIdRequestFailed,
            LookupRequestFailed
        }
    }
}
