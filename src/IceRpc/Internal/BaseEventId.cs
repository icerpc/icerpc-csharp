// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal;

/// <summary>This enumeration contain base event ID constants used by category specific event ID enumerations.</summary>
internal enum BaseEventId
{
    /// <summary>The base event ID for connection logging.</summary>
    Connection = 100,

    /// <summary>The base event ID for protocol logging.</summary>
    Protocol = 200,

    /// <summary>The base event ID for transport logging.</summary>
    Transport = 300,

    /// <summary>The base event ID for Tcp logging.</summary>
    Tcp = 400,
}
