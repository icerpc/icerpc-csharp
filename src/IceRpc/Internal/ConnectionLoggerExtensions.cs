// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging connection messages.</summary>
    internal static partial class ConnectionLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)ConnectionEventIds.DispatchException,
            EventName = nameof(ConnectionEventIds.DispatchException),
            Level = LogLevel.Debug,
            Message = "dispatch exception (Connection={Connection}, Path={Path}, Operation={Operation})")]
        internal static partial void LogDispatchException(
            this ILogger logger,
            Connection connection,
            string path,
            string operation,
            Exception ex);
    }
}
