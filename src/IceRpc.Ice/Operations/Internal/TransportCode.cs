// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Operations.Internal;

/// <summary>TransportCode is used by the Ice encoding to encode a transport name (such as "tcp") as a short value.
/// </summary>
internal enum TransportCode
{
    /// <summary>This server address encapsulation contains a server address URI string.</summary>
    Uri = 0,

    /// <summary>TCP transport.</summary>
    Tcp = 1,

    /// <summary>SSL transport.</summary>
    Ssl = 2,
}
