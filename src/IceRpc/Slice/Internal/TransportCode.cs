// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Internal;

/// <summary>TransportCode is used by Slice1 to encode a transport name (such as "tcp") as an int16 value.</summary>
internal enum TransportCode
{
    /// <summary>This server address encapsulation contains a server address URI string.</summary>
    Uri = 0,

    /// <summary>TCP transport.</summary>
    Tcp = 1,

    /// <summary>SSL transport.</summary>
    Ssl = 2,
}
