// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using Microsoft.Extensions.Logging;

namespace ZeroC.Ice
{
    internal static class SlicingLoggerExtensions
    {
        private static readonly Action<ILogger, string, string, Exception> _slicingUnknowType = LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            GetEventId(SlicingEvent.SlicingUnknownType),
            "slicing unknown {Kind} type `{PrintableId}'");

        internal static void LogSlicingUnknownType(this ILogger logger, string kind, string printableId) =>
            _slicingUnknowType(logger, kind, printableId, null!);
        private static EventId GetEventId(SlicingEvent e) => new EventId((int)e, e.ToString());

        private enum SlicingEvent
        {
            SlicingUnknownType
        }
    }
}
