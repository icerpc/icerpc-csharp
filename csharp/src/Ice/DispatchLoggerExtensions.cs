// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace ZeroC.Ice
{
    internal static class DispatchLoggerExtensions
    {
        private static readonly Action<ILogger, Exception> _dispatchException = LoggerMessage.Define(
            LogLevel.Error,
            GetEventId(DispatchEvent.DispatchException),
            "dispatch exception");

        private static readonly Func<ILogger, string, Identity, string, IDisposable> _startDispatchScope =
            LoggerMessage.DefineScope<string, Identity, string>(
                "dispatching operation `{Operation}' on object with identity `{Identity}#{Facet}' invoked from remote endpoint `TODO'");

        /// <summary>This scope is activated when the connection is used to read or write data.</summary>
        private static readonly Func<ILogger, long, string, bool, Transport, Protocol, IDisposable> _collocatedConnectionScope =
            LoggerMessage.DefineScope<long, string, bool, Transport, Protocol>(
                "collocated connection: ID = {Id}, object adapter = {Adapter}, incoming = {Incoming}, " +
                "transport = {Transport}, protocol = {Protocol}");

        private static readonly Func<ILogger, string, string, Transport, Protocol, IDisposable> _connectionScope =
            LoggerMessage.DefineScope<string, string, Transport, Protocol>(
                "connection: local address = {LocalAddress}, remote address = {RemoteAddress}, " +
                "transport = {Transport}, protocol = {Protocol}");

        // TODO pass identityAndFacet as a string
        // TODO pass remote endpoint
        internal static IDisposable StartDispatchScope(
            this ILogger logger,
            string operation,
            Identity identity,
            string facet) =>
            _startDispatchScope(logger, operation, identity, facet);

        internal static IDisposable StartCollocatedConnectionScope(
            this ILogger logger,
            long id,
            string adapter,
            bool incoming,
            Transport transport,
            Protocol protocol) =>
            _collocatedConnectionScope(logger, id, adapter, incoming, transport, protocol);

        internal static IDisposable StartConnectionScope(
            this ILogger logger,
            string localAddress,
            string remoteAddress,
            Transport transport,
            Protocol protocol) =>
            _connectionScope(logger, localAddress, remoteAddress, transport, protocol);

        internal static void LogDispatchException(this ILogger logger, Exception ex) => _dispatchException(logger, ex);

        private static EventId GetEventId(DispatchEvent e) => new EventId((int)e, e.ToString());

        private enum DispatchEvent
        {
            DispatchException
        }
    }
}