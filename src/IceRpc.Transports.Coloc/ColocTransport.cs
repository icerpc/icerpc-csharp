// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Coloc.Internal;
using System.Collections.Concurrent;

namespace IceRpc.Transports.Coloc;

/// <summary>The Coloc transport class provides a client and server duplex transport that can be used for in-process
/// communications.</summary>
public sealed class ColocTransport
{
    /// <summary>The transport name.</summary>
    public const string Name = "coloc";

    /// <summary>Gets the colocated client transport.</summary>
    /// <value>The client transport.</value>
    public IDuplexClientTransport ClientTransport { get; }

    /// <summary>Gets the colocated server transport.</summary>
    /// <value>The server transport.</value>
    public IDuplexServerTransport ServerTransport { get; }

    /// <summary>Constructs a <see cref="ColocTransport" />.</summary>
    public ColocTransport()
        : this(new ColocTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="ColocTransport" />.</summary>
    /// <param name="options">The options to configure the Coloc transport.</param>
    /// <exception cref="ArgumentException">Thrown when the <see cref="ColocTransportOptions.ResumeWriterThreshold" />
    /// of <paramref name="options" /> is greater than its <see cref="ColocTransportOptions.PauseWriterThreshold"
    /// />.</exception>
    public ColocTransport(ColocTransportOptions options)
    {
        if (options.ResumeWriterThreshold > options.PauseWriterThreshold)
        {
            throw new ArgumentException(
                $"The value of {nameof(ColocTransportOptions.ResumeWriterThreshold)} cannot be greater than the value of {nameof(ColocTransportOptions.PauseWriterThreshold)}.",
                nameof(options));
        }

        var listeners = new ConcurrentDictionary<(string Host, ushort Port), ColocListener>();
        ClientTransport = new ColocClientTransport(listeners, options);
        ServerTransport = new ColocServerTransport(listeners, options);
    }
}
