// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for connection logging.</summary>
    public enum ConnectionEvent
    {
        /// <summary>A dispatch operation thrown an unexpected exception.</summary>
        DispatchException = Internal.LoggerExtensions.ConnectionBaseEventId
    }
}
