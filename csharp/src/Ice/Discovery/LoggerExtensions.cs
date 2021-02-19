// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace ZeroC.Ice.Discovery
{
    internal static class LoggerExtensions
    {
        private static readonly Action<ILogger, IResolveAdapterIdReplyPrx, Exception> _foundAdapterIdRequestFailed = LoggerMessage.Define<IResolveAdapterIdReplyPrx>(
            LogLevel.Error,
            GetEventId(DiscoveryEvent.FoundAdapterIdRequestFailed),
            "Ice discovery failed to send foundAdapterId to `{ReplyProxy}'");

        private static readonly Action<ILogger, IFindAdapterByIdReplyPrx, Exception> _foundAdapterByIdRequestFailed = LoggerMessage.Define<IFindAdapterByIdReplyPrx>(
            LogLevel.Error,
            GetEventId(DiscoveryEvent.FoundAdapterByIdRequestFailed),
            "Ice discovery failed to send foundAdapterById to `{ReplyProxy}'");

        private static readonly Action<ILogger, IFindObjectByIdReplyPrx, Exception> _foundObjectByIdRequestFailed = LoggerMessage.Define<IFindObjectByIdReplyPrx>(
            LogLevel.Error,
            GetEventId(DiscoveryEvent.FoundObjectByIdRequestFailed),
            "Ice discovery failed to send foundObjectById to `{ReplyProxy}'");

        private static readonly Action<ILogger, IResolveWellKnownProxyReplyPrx, Exception> _foundWellKnownProxyReuestFailed = LoggerMessage.Define<IResolveWellKnownProxyReplyPrx>(
            LogLevel.Error,
            GetEventId(DiscoveryEvent.FoundObjectByIdRequestFailed),
            "Ice discovery failed to send foundWellKnownProxy to `{ReplyProxy}'");

        private static readonly Action<ILogger, ILookupPrx, Exception> _lookupRequestFailed = LoggerMessage.Define<ILookupPrx>(
            LogLevel.Error,
            GetEventId(DiscoveryEvent.LookupRequestFailed),
            "Ice discovery failed to send lookup request using lookup `{LookupProxy}'");

        internal static void LogFoundAdapterIdRequestFailed(
            this ILogger logger,
            IResolveAdapterIdReplyPrx replyProxy,
            Exception ex) =>
            _foundAdapterIdRequestFailed(logger, replyProxy, ex);

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

        internal static void LogFoundWellKnownProxyReuestFailed(
            this ILogger logger,
            IResolveWellKnownProxyReplyPrx replyProxy,
            Exception ex) =>
            _foundWellKnownProxyReuestFailed(logger, replyProxy, ex);

        internal static void LogLookupRequestFailed(
            this ILogger logger,
            ILookupPrx lookup,
            Exception ex) =>
            _lookupRequestFailed(logger, lookup, ex);

        private static EventId GetEventId(DiscoveryEvent e) => new EventId((int)e, e.ToString());
        private enum DiscoveryEvent
        {
            FoundAdapterIdRequestFailed,
            FoundAdapterByIdRequestFailed,
            FoundObjectByIdRequestFailed,
            FoundWellKnownProxyReuestFailed,
            LookupRequestFailed
        }
    }
}
