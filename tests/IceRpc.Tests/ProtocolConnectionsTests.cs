// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class ProtocolConnectionTests
{
    private static readonly List<Protocol> _protocols = new() { Protocol.Ice, Protocol.IceRpc };
    private static readonly List<Protocol> _protocolsWithDelayedPayloadReading = new() { Protocol.IceRpc };

    private static IEnumerable<TestCaseData> Protocol_on_server_and_client_connection
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol, ConnectionType.Client);
                yield return new TestCaseData(protocol, ConnectionType.Server);
            }
        }
    }

    private static IEnumerable<TestCaseData> Payload_completed_on_request
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol);
            }
        }
    }

    private static IEnumerable<TestCaseData> Payload_completed_on_twoway_and_oneway_request
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> Payload_completed_on_request_with_fields
    {
        get
        {
            foreach (Protocol protocol in _protocols.Where(protocol => protocol.HasFields))
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> Payload_completed_on_response
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol);
            }
        }
    }

    private static IEnumerable<TestCaseData> PayloadStream_completed_on_request
    {
        get
        {
            foreach (Protocol protocol in _protocols.Where(protocol => HasPayloadStreamSupport(protocol)))
            {
                yield return new TestCaseData(protocol);
            }
        }
    }

    private static IEnumerable<TestCaseData> PayloadStream_completed_on_oneway_and_twoway_request
    {
        get
        {
            foreach (Protocol protocol in _protocols.Where(protocol => HasPayloadStreamSupport(protocol)))
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> PayloadStream_completed_on_response
    {
        get
        {
            foreach (Protocol protocol in _protocols.Where(protocol => HasPayloadStreamSupport(protocol)))
            {
                yield return new TestCaseData(protocol);
            }
        }
    }

    public enum ConnectionType
    {
        Client,
        Server
    }

    /// <summary>Ensures that the connection HasInvocationInProgress works.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Connection_has_invocation_in_progress(Protocol protocol)
    {
        // Arrange
        var result = new TaskCompletionSource<bool>();
        ProtocolConnectionPair? sut = null;
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions()
                {
                    Dispatcher = new InlineDispatcher((request, cancel) =>
                        {
                            result.SetResult(sut!.Value.Client.HasInvocationsInProgress);
                            return new(new OutgoingResponse(request));
                        })
                })
            .BuildServiceProvider();

        sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Value.Server.AcceptRequestsAsync();

        // Act
        await sut.Value.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));

        // Assert
        Assert.That(await result.Task, Is.True);
        await sut!.Value.DisposeAsync();
    }

    /// <summary>Ensures that the connection HasDispatchInProgress works.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Connection_has_dispatch_in_progress(Protocol protocol)
    {
        // Arrange
        var result = new TaskCompletionSource<bool>();
        ProtocolConnectionPair? sut = null;
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions()
            {
                Dispatcher = new InlineDispatcher((request, cancel) =>
                    {
                        result.SetResult(sut!.Value.Server.HasDispatchesInProgress);
                        return new(new OutgoingResponse(request));
                    })
            })
            .BuildServiceProvider();

        sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Value.Server.AcceptRequestsAsync();

        // Act
        await sut.Value.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));

        // Assert
        Assert.That(await result.Task, Is.True);
        await sut!.Value.DisposeAsync();
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Dispose_the_protocol_connections(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        // Act
        sut.Client.Dispose();
        sut.Server.Dispose();
    }

    /// <summary>Ensures that the connection fields are correctly exchanged on the protocol connection
    /// initialization.</summary>
    [Test]
    public async Task Initialize_peer_fields()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt(56))
            })
            .UseClientConnectionOptions(new()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt(34))
                        .With((ConnectionFieldKey)10, (ref SliceEncoder encoder) => encoder.EncodeInt(38))
            })
            .BuildServiceProvider();

        // Act
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync(); // Initializes the connections

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sut.Server.PeerFields, Has.Count.EqualTo(2));
            Assert.That(sut.Client.PeerFields, Has.Count.EqualTo(1));
            Assert.That(DecodeField(sut.Server.PeerFields, ConnectionFieldKey.MaxHeaderSize), Is.EqualTo(34));
            Assert.That(DecodeField(sut.Server.PeerFields, (ConnectionFieldKey)10), Is.EqualTo(38));
            Assert.That(DecodeField(sut.Client.PeerFields, ConnectionFieldKey.MaxHeaderSize), Is.EqualTo(56));
        });

        static int DecodeField(
            ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> fields,
            ConnectionFieldKey key) =>
            fields.DecodeValue(key, (ref SliceDecoder decoder) => decoder.DecodeInt());
    }

    /// <summary>Ensures that the PeerShutdownInitiated callback is called when the peer initiates the
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(Protocol_on_server_and_client_connection))]
    public async Task PeerShutdownInitiated_callback_is_called(Protocol protocol, ConnectionType connectionType)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        IProtocolConnection connection1 = connectionType == ConnectionType.Client ? sut.Server : sut.Client;
        IProtocolConnection connection2 = connectionType == ConnectionType.Client ? sut.Client : sut.Server;

        var shutdownInitiatedCalled = new TaskCompletionSource<string>();
        connection2.PeerShutdownInitiated = message =>
            {
                shutdownInitiatedCalled.SetResult(message);
                _ = connection2.ShutdownAsync("");
            };

        // Act
        await connection1.ShutdownAsync("hello world");

        // Assert
        string message = protocol == Protocol.Ice ? "connection shutdown by peer" : "hello world";
        Assert.That(await shutdownInitiatedCalled.Task, Is.EqualTo(message));
    }

    /// <summary>Ensures that the sending a request after shutdown fails.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Invoke_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();

        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Client.ShutdownAsync("");

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));

        // Assert
        Assert.ThrowsAsync<ConnectionClosedException>(async () => await invokeTask);
    }

    /// <summary>Ensures that the request payload is completed on valid request.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_twoway_and_oneway_request))]
    public async Task Payload_completed_on_valid_request(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
            {
                IsOneway = isOneway,
                Payload = payloadDecorator
            };

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(payloadCompleted, Is.True);
    }

    /// <summary>Ensures that the request payload is completed if the payload is invalid.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_twoway_and_oneway_request))]
    public async Task Payload_completed_on_invalid_request_payload(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(payloadCompleted, Is.True);
        Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload is completed if the payload is invalid.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_twoway_and_oneway_request))]
    public async Task Payload_completed_on_invalid_request_payload_writer(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };
        request.Use(writer => InvalidPipeWriter.Instance);

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(payloadCompleted, Is.True);
        Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload is completed if the request fields are invalid.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_request_with_fields))]
    public async Task Payload_completed_on_invalid_request_fields(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };
        request.Fields = request.Fields.With(
            RequestFieldKey.Idempotent,
            (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(payloadCompleted, Is.True);
        Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload is completed if the connection is shutdown.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_request))]
    public async Task Payload_completed_on_request_when_connection_is_shutdown(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Client.ShutdownAsync("", default);

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            Payload = payloadDecorator,
        };

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);
        bool completeCalled = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ConnectionClosedException>(() => invokeTask);
            Assert.That(completeCalled, Is.True);
            Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<ConnectionClosedException>());
        });
    }

    /// <summary>Ensures that the response payload is completed on valid response.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        bool completedCalled = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_invalid_response_payload(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        bool completedCalled = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
        Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_invalid_response_fields(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                    {
                        Payload = payloadDecorator
                    };
                response.Fields = response.Fields.With(
                    ResponseFieldKey.CompressionFormat,
                    (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));
                return new(response);
            });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        bool completedCalled = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
        Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_invalid_response_payload_writer(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                };
                response.Use(writer => InvalidPipeWriter.Instance);
                return new(response);
            });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        bool completedCalled = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
        Assert.That(payloadDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload stream is completed on valid request.</summary>
    [Test, TestCaseSource(nameof(PayloadStream_completed_on_oneway_and_twoway_request))]
    public async Task PayloadStream_completed_on_valid_request(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadStreamDecorator.CompleteCalled;

        // Assert
        Assert.That(payloadCompleted, Is.True);
    }

    /// <summary>Ensures that the request payload is completed if the payload is invalid.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_twoway_and_oneway_request))]
    public async Task PayloadStream_completed_on_invalid_request_payload(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadStreamDecorator.CompleteCalled;

        // Assert
        Assert.That(payloadCompleted, Is.True);
        Assert.That(payloadStreamDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload stream is completed if the connection is shutdown.</summary>
    [Test, TestCaseSource(nameof(PayloadStream_completed_on_request))]
    public async Task PayloadStream_completed_on_request_when_connection_is_shutdown(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Client.ShutdownAsync("", default);

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            PayloadStream = payloadStreamDecorator
        };

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);
        bool completeCalled = await payloadStreamDecorator.CompleteCalled;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ConnectionClosedException>(() => invokeTask);
            Assert.That(completeCalled, Is.True);
            Assert.That(payloadStreamDecorator.CompleteException, Is.InstanceOf<ConnectionClosedException>());
        });
    }

    /// <summary>Ensures that the response payload stream is completed on valid response.</summary>
    [Test, TestCaseSource(nameof(PayloadStream_completed_on_response))]
    public async Task PayloadStream_completed_on_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        bool completedCalled = await payloadDecorator.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test, TestCaseSource(nameof(PayloadStream_completed_on_response))]
    public async Task PayloadStream_completed_on_invalid_response_payload(Protocol protocol)
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        bool completedCalled = await payloadStreamDecorator.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
        Assert.That(payloadStreamDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on valid request.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task PayloadWriter_completed_with_valid_request(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        var request = new OutgoingRequest(new Proxy(protocol));
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request);
        PayloadPipeWriterDecorator payloadWriter = await payloadWriterSource.Task;
        bool completedCalled = await payloadWriter.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
    }

    /// <summary>Ensures that the request payload writer is completed on valid response.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task PayloadWriter_completed_with_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request);
                response.Use(writer =>
                    {
                        var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                        payloadWriterSource.SetResult(payloadWriterDecorator);
                        return payloadWriterDecorator;
                    });
                return new(response);
            });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        PayloadPipeWriterDecorator payloadWriter = await payloadWriterSource.Task;
        bool completedCalled = await payloadWriter.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
    }

    /// <summary>Ensures that the request payload writer is completed on invalid request.</summary>
    [Test, TestCaseSource(nameof(_protocolsWithDelayedPayloadReading))]
    public async Task PayloadWriter_completed_with_invalid_request(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        var request = new OutgoingRequest(new Proxy(protocol))
            {
                Payload = InvalidPipeReader.Instance
            };
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request);
        PayloadPipeWriterDecorator payloadWriter = await payloadWriterSource.Task;
        bool completedCalled = await payloadWriter.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
        Assert.That(payloadWriter.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on invalid response.</summary>
    [Test, TestCaseSource(nameof(_protocolsWithDelayedPayloadReading))]
    public async Task PayloadWriter_completed_with_invalid_response(Protocol protocol)
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                    {
                        Payload = InvalidPipeReader.Instance
                    };
                response.Use(writer =>
                    {
                        var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                        payloadWriterSource.SetResult(payloadWriterDecorator);
                        return payloadWriterDecorator;
                    });
                return new(response);
            });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));
        PayloadPipeWriterDecorator payloadWriter = await payloadWriterSource.Task;
        bool completedCalled = await payloadWriter.CompleteCalled;

        // Assert
        Assert.That(completedCalled, Is.True);
        Assert.That(payloadWriter.CompleteException, Is.InstanceOf<NotSupportedException>());
    }

    private static bool HasPayloadStreamSupport(Protocol protocol) => protocol == Protocol.IceRpc;
}

/// <summary>A payload pipe reader decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeReaderDecorator : PipeReader
{
    internal Task<bool> CompleteCalled => _completeCalled.Task;

    internal Exception? CompleteException { get; private set; }

    private readonly PipeReader _decoratee;
    private readonly TaskCompletionSource<bool> _completeCalled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        CompleteException = exception;
        _completeCalled.SetResult(true);
        _decoratee.Complete(exception);
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        _decoratee.ReadAsync(cancellationToken);

    public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

    internal PayloadPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
}

/// <summary>A payload pipe writer decorator to check if the complete method is called.</summary>
public sealed class PayloadPipeWriterDecorator : PipeWriter
{
    internal Task<bool> CompleteCalled => _completeCalled.Task;
    internal Exception? CompleteException { get; private set; }

    private readonly PipeWriter _decoratee;
    private readonly TaskCompletionSource<bool> _completeCalled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

    public override void Complete(Exception? exception = null)
    {
        CompleteException = exception;
        _completeCalled.SetResult(true);
        _decoratee.Complete(exception);
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
        _decoratee.FlushAsync(cancellationToken);

    public override void Advance(int bytes) =>
        _decoratee.Advance(bytes);

    public override Memory<byte> GetMemory(int sizeHint = 0) =>
        _decoratee.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) =>
        _decoratee.GetSpan(sizeHint);

    internal PayloadPipeWriterDecorator(PipeWriter decoratee) => _decoratee = decoratee;
}

/// <summary>A helper struct to ensure the network and protocol connections are correctly disposed.</summary>
internal struct ProtocolConnectionPair : IAsyncDisposable
{
    public IProtocolConnection Client { get; }
    public INetworkConnection ClientNetworkConnection { get; }
    public IProtocolConnection Server { get; }
    public INetworkConnection ServerNetworkConnection { get; }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Server.Dispose();
        await ClientNetworkConnection.DisposeAsync();
        await ServerNetworkConnection.DisposeAsync();
    }

    internal ProtocolConnectionPair(
        INetworkConnection clientNetworkConnection,
        INetworkConnection serverNetworkConnection,
        IProtocolConnection clientConnection,
        IProtocolConnection serverConnection)
    {
        ClientNetworkConnection = clientNetworkConnection;
        ServerNetworkConnection = serverNetworkConnection;
        Client = clientConnection;
        Server = serverConnection;
    }
}

internal class ProtocolServiceCollection : TransportServiceCollection
{
    public ProtocolServiceCollection()
    {
        this.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
        this.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
    }
}

internal static class ProtocolServiceCollectionExtensions
{
    internal static IServiceCollection UseProtocol(this IServiceCollection collection, Protocol protocol) =>
        collection.AddSingleton(protocol);

    internal static IServiceCollection UseServerConnectionOptions(
        this IServiceCollection collection,
        ConnectionOptions options) =>
        collection.AddSingleton(new ServerConnectionOptions(options));

    internal static IServiceCollection UseClientConnectionOptions(
        this IServiceCollection collection,
        ConnectionOptions options) =>
        collection.AddSingleton(new ClientConnectionOptions(options));

    internal static async Task<ProtocolConnectionPair> GetProtocolConnectionPairAsync(
        this IServiceProvider serviceProvider)
    {
        Task<(INetworkConnection, IProtocolConnection)> serverTask = serviceProvider.GetServerProtocolConnectionAsync();
        (INetworkConnection clientNetworkConnection, IProtocolConnection clientProtocolConnection) =
            await serviceProvider.GetClientProtocolConnectionAsync();
        (INetworkConnection serverNetworkConnection, IProtocolConnection serverProtocolConnection) = await serverTask;
        return new ProtocolConnectionPair(
            clientNetworkConnection,
            serverNetworkConnection,
            clientProtocolConnection,
            serverProtocolConnection);
    }

    private static Task<(INetworkConnection, IProtocolConnection)> GetClientProtocolConnectionAsync(
        this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
         GetProtocolConnectionAsync(serviceProvider, false, serviceProvider.GetSimpleClientConnectionAsync) :
         GetProtocolConnectionAsync(serviceProvider, false, serviceProvider.GetMultiplexedClientConnectionAsync);

    private static async Task<(INetworkConnection, IProtocolConnection)> GetProtocolConnectionAsync<T>(
        IServiceProvider serviceProvider,
        bool isServer,
        Func<Task<T>> networkConnectionFactory) where T : INetworkConnection
    {
        T networkConnection = await networkConnectionFactory();
        IProtocolConnection protocolConnection =
            await serviceProvider.GetRequiredService<IProtocolConnectionFactory<T>>().CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation: new(),
                connection: null!,
                connectionOptions: isServer ?
                    serviceProvider.GetService<ServerConnectionOptions>()?.Value ?? new() :
                    serviceProvider.GetService<ClientConnectionOptions>()?.Value ?? new(),
                isServer,
                CancellationToken.None);
        return (networkConnection, protocolConnection);
    }

    private static Task<(INetworkConnection, IProtocolConnection)> GetServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
         GetProtocolConnectionAsync(serviceProvider, true, serviceProvider.GetSimpleServerConnectionAsync) :
         GetProtocolConnectionAsync(serviceProvider, true, serviceProvider.GetMultiplexedServerConnectionAsync);

    private sealed class ClientConnectionOptions
    {
        internal ConnectionOptions Value { get; }

        internal ClientConnectionOptions(ConnectionOptions options) => Value = options;
    }

    private sealed class ServerConnectionOptions
    {
        internal ConnectionOptions Value { get; }

        internal ServerConnectionOptions(ConnectionOptions options) => Value = options;
    }
}
