// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Logger;

/// <summary>This class contains event ID constants used by the logger middleware.</summary>
public enum LoggerMiddlewareEventIds
{
    /// <summary>The dispatch was successful as far as IceRPC is concerned. Its result type can nevertheless contain
    /// a failure.</summary>
    Dispatch,

    /// <summary>The dispatch failed with an exception.</summary>
    DispatchException,
}
