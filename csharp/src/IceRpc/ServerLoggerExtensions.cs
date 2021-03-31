// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging WebSocket transport messages.</summary>
    internal static class ServerLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.ServerBaseEventId;
        private const int ServerPublishedEndpoints = BaseEventId + 0;

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _serverPublishedEndpoints =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Information,
                new EventId(ServerPublishedEndpoints, nameof(ServerPublishedEndpoints)),
                "published endpoints for server {Name}: {Endpoints}");

        internal static void LogServerPublishedEndpoints(
            this ILogger logger,
            string name,
            IReadOnlyList<Endpoint> endpoints) =>
            _serverPublishedEndpoints(logger, name, endpoints, null!);
    }
}
