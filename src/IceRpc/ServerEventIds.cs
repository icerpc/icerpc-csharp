// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains constants used for server logging event Ids.</summary>
    public static class ServerEventIds
    {
        public static readonly EventId ServerListening = new(BaseEventId + 0, nameof(ServerListening));
        public static readonly EventId ServerShuttingDown = new(BaseEventId + 1, nameof(ServerShuttingDown));
        public static readonly EventId ServerShutdownComplete = new(BaseEventId + 2, nameof(ServerShutdownComplete));

        private const int BaseEventId = Internal.LoggerExtensions.ServerBaseEventId;
    }
}
