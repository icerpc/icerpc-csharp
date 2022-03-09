// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Tests;

[Timeout(30000)]
public class FieldTests
{
    private const ConnectionFieldKey ConnectionFieldKey = (ConnectionFieldKey)100;
    private static readonly byte[] _connectionFieldValue = new byte[] { 0xAA, 0xBB, 0xCC };

    [Test]
    public async Task Verify_client_connection_field_is_received_by_server_connection()
    {
        ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>>? peerFields = null;
        var colocTransport = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher((request, cancel) =>
            {
                peerFields = request.Connection.PeerFields;
                return new(new OutgoingResponse(request));
            }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>
            {
                [ConnectionFieldKey] = new OutgoingFieldValue(new ReadOnlySequence<byte>(_connectionFieldValue))
            },
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
            RemoteEndpoint = server.Endpoint
        });
        var prx = ServicePrx.FromConnection(connection);

        await prx.IcePingAsync();

        Assert.That(peerFields, Is.Not.Null);
        Assert.That(peerFields[ConnectionFieldKey].ToArray(), Is.EqualTo(_connectionFieldValue));
    }

    [Test]
    public async Task Verify_server_connection_field_is_received_by_client_connection()
    {
        var colocTransport = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>
            {
                [ConnectionFieldKey] = new OutgoingFieldValue(new ReadOnlySequence<byte>(_connectionFieldValue))
            },
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
            RemoteEndpoint = server.Endpoint
        });

        await connection.ConnectAsync();

        Assert.That(connection.PeerFields[ConnectionFieldKey].ToArray(), Is.EqualTo(_connectionFieldValue));
    }
}
