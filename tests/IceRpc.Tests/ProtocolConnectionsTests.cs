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

    private static IEnumerable<TestCaseData> ClientServerConnections
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

    /// <summary>Provides test case data for the <see cref="Payload_completed_on_request_and_response"/> test. The test
    /// case data provides the outgoing request to send, the dispatcher to provide the response and the payload reader
    /// decorator used to ensure <see cref="PipeReader.Complete"/> is called. The test case test the completion of the
    /// request/response payload and payload stream on a successful request or on various failure conditions.</summary>
    private static IEnumerable<TestCaseData> RequestsAndResponses
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                foreach (TestCaseData testCase in GetTestCaseData(protocol, isOneway: false))
                {
                    testCase.TestName =
                        $"Payload_completed_on_request_and_response({testCase.TestName},{protocol},oneway))";
                    yield return testCase;
                }
                foreach (TestCaseData testCase in GetTestCaseData(protocol, isOneway: true))
                {
                    testCase.TestName =
                        $"Payload_completed_on_request_and_response({testCase.TestName},{protocol},twoway)";
                    yield return testCase;
                }
            }

            static IEnumerable<TestCaseData> GetTestCaseData(Protocol protocol, bool isOneway)
            {
                // Valid requests with payload and payload stream
                yield return CreateRequestTestCase("valid_request_payload", payload: EmptyPipeReader.Instance);

                if (HasPayloadStreamSupport(protocol))
                {
                    yield return CreateRequestTestCase(
                        "valid_request_payload_stream",
                        payloadStream: EmptyPipeReader.Instance);

                    yield return CreateRequestTestCase(
                        "valid_request_payload_And_payload_stream",
                        payload: EmptyPipeReader.Instance,
                        payloadStream: EmptyPipeReader.Instance);
                }

                // Valid responses with payload and payload stream
                yield return CreateResponseTestCase("valid_response_payload", payload: EmptyPipeReader.Instance);

                if (HasPayloadStreamSupport(protocol))
                {
                    yield return CreateResponseTestCase(
                        "valid_response_payload_stream",
                        payloadStream: EmptyPipeReader.Instance);

                    yield return CreateResponseTestCase(
                        "valid_response_payload_And_payload_stream",
                        payload: EmptyPipeReader.Instance,
                        payloadStream: EmptyPipeReader.Instance);
                }

                // Invalid requests
                yield return CreateRequestTestCase("invalid_request_payload", payload: InvalidPipeReader.Instance);
                yield return CreateRequestTestCase("invalid_request_writer", writer: InvalidPayloadWriter);

                yield return CreateRequestTestCase(
                    "invalid_request_payload_stream",
                    payloadStream: InvalidPipeReader.Instance);

                if (protocol.HasFields)
                {
                    yield return CreateRequestTestCase("invalid_request_Fields", invalidFields: true);
                }

                if (isOneway)
                {
                    // No response, so there's no need to test response payload or payload stream completion.
                    yield break;
                }

                // Invalid responses
                yield return CreateResponseTestCase("invalid_response_payload", payload: InvalidPipeReader.Instance);
                yield return CreateResponseTestCase("invalid_response_writer", writer: InvalidPayloadWriter);

                yield return CreateResponseTestCase(
                    "invalid_response_payload_stream",
                    payloadStream: InvalidPipeReader.Instance);

                if (protocol.HasFields)
                {
                    yield return CreateResponseTestCase("invalid_response_Fields", invalidFields: true);
                }

                TestCaseData CreateRequestTestCase(
                    string name,
                    PipeReader? payload = null,
                    PipeReader? payloadStream = null,
                    Func<PipeWriter, PipeWriter>? writer = null,
                    bool invalidFields = false)
                {
                    PayloadPipeReaderDecorator? decorator = payload == null ? null : new(payload);
                    PayloadPipeReaderDecorator? streamDecorator = payloadStream == null ? null : new(payloadStream);
                    bool invalid = payload == InvalidPipeReader.Instance || payloadStream == InvalidPipeReader.Instance;
                    var testCaseData = new TestCaseData(
                        protocol,
                        CreateRequest(
                            protocol,
                            isOneway,
                            payload: decorator,
                            payloadStream: streamDecorator,
                            writer: writer,
                            invalidFields: invalidFields),
                        null, // dispatcher
                        decorator,
                        streamDecorator,
                        invalid ? typeof(NotSupportedException) : null);
                    testCaseData.SetName(name);
                    return testCaseData;
                }

                TestCaseData CreateResponseTestCase(
                    string name,
                    PipeReader? payload = null,
                    PipeReader? payloadStream = null,
                    Func<PipeWriter, PipeWriter>? writer = null,
                    bool invalidFields = false)
                {
                    PayloadPipeReaderDecorator? decorator = payload == null ? null : new(payload);
                    PayloadPipeReaderDecorator? streamDecorator = payloadStream == null ? null : new(payloadStream);
                    bool invalid = payload == InvalidPipeReader.Instance || payloadStream == InvalidPipeReader.Instance;
                    var testCaseData = new TestCaseData(
                        protocol,
                        CreateRequest(protocol, isOneway),
                        CreateResponseDispatcher(
                            payload: decorator,
                            payloadStream: streamDecorator,
                            writer: writer,
                            invalidFields: invalidFields),
                        decorator,
                        streamDecorator,
                        invalid ? typeof(NotSupportedException) : null);
                    testCaseData.SetName(name);
                    return testCaseData;
                }
            }
        }
    }

    /// <summary>Provides test case data for the <see cref="payload_writer_completed_on_request_and_response"> test. The
    /// test case data provides the outgoing request to send, the dispatcher to provide the response and the payload
    /// writer interceptor use to ensure <see cref="PipeWriter.Complete"/> is called.</summary>
    private static IEnumerable<TestCaseData> RequestsAndResponsesWithPayloadWriter
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                foreach ((string name, TestCaseData testCase) in GetTestCaseData(protocol, isOneway: false))
                {
                    testCase.SetName(
                        $"PayloadWriter_completed_on_request_and_response({name},{protocol},oneway)");
                    yield return testCase;
                }
                foreach ((string name, TestCaseData testCase) in GetTestCaseData(protocol, isOneway: true))
                {
                    testCase.SetName(
                        $"PayloadWriter_completed_on_request_and_response{name}({protocol},twoway)");
                    yield return testCase;
                }
            }

            static IEnumerable<(string, TestCaseData)> GetTestCaseData(Protocol protocol, bool isOneway)
            {
                // Valid request
                {
                    var writerSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = CreateRequest(protocol, isOneway, writer: PayloadWriter(writerSource));
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return ("request_payload_writer",
                                  new(protocol, request, dispatcher, writerSource.Task, null));
                }

                // Valid response
                if (!isOneway)
                {
                    var writerSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = CreateRequest(protocol, isOneway);
                    var dispatcher = CreateResponseDispatcher(writer: PayloadWriter(writerSource));
                    yield return ("response_payload_writer",
                                  new(protocol, request, dispatcher, writerSource.Task, null));
                }

                if (protocol == Protocol.Ice)
                {
                    // The Ice protocol reads payload before instantiating the payload writer so the test bellow
                    // would hang since the payload writer will never be set.
                    yield break;
                }

                // Invalid request payload
                {
                    var writerSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = CreateRequest(
                        protocol, isOneway, InvalidPipeReader.Instance, writer: PayloadWriter(writerSource));
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return ("invalid_request_payload",
                                  new(protocol, request, dispatcher, writerSource.Task, typeof(NotSupportedException)));
                }

                // Invalid response payload
                if (!isOneway)
                {
                    var writerSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = CreateRequest(protocol, isOneway);
                    var dispatcher = CreateResponseDispatcher(InvalidPipeReader.Instance, PayloadWriter(writerSource));
                    yield return ("invalid_response_payload",
                                  new(protocol, request, dispatcher, writerSource.Task, typeof(NotSupportedException)));
                }
            }

            static Func<PipeWriter, PipeWriter> PayloadWriter(
                TaskCompletionSource<PayloadPipeWriterDecorator> source) =>
                writer =>
                {
                    var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                    source.SetResult(payloadWriterDecorator);
                    return payloadWriterDecorator;
                };
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
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt32(56))
            })
            .UseClientConnectionOptions(new()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt32(34))
                        .With((ConnectionFieldKey)10, (ref SliceEncoder encoder) => encoder.EncodeInt32(38))
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
            fields.DecodeValue(key, (ref SliceDecoder decoder) => decoder.DecodeInt32());
    }

    /// <summary>Ensures that the PeerShutdownInitiated callback is called when the peer initiates the
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(ClientServerConnections))]
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
    public async Task Sendrequest_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();

        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Client.ShutdownAsync("");

        // Act
        Task<IncomingResponse> sendRequestTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)));

        // Assert
        Assert.ThrowsAsync<ConnectionClosedException>(async () => await sendRequestTask);
    }

    /// <summary>Ensures that the request or response payload is always completed.</summary>
    [Test, TestCaseSource(nameof(RequestsAndResponses))]
    public async Task Payload_completed_on_request_and_response(
        Protocol protocol,
        OutgoingRequest request,
        IDispatcher dispatcher,
        PayloadPipeReaderDecorator? payload,
        PayloadPipeReaderDecorator? payloadStream,
        Type? exceptionType)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.Multiple(async () =>
        {
            if (payload != null)
            {
                Assert.That(await payload.CompleteCalled, Is.True);
            }
            if (payloadStream != null)
            {
                Assert.That(await payloadStream.CompleteCalled, Is.True);
            }
            Assert.That((payload ?? payloadStream)?.CompleteException?.GetType(), Is.EqualTo(exceptionType));
        });
    }

    /// <summary>Ensures that the request or response payload writer is always completed.</summary>
    [Test, TestCaseSource(nameof(RequestsAndResponsesWithPayloadWriter))]
    public async Task PayloadWriter_completed_on_request_and_response(
        Protocol protocol,
        OutgoingRequest request,
        IDispatcher dispatcher,
        Task<PayloadPipeWriterDecorator> payloadWriterTask,
        Type? exceptionType)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(request);
        PayloadPipeWriterDecorator payloadWriter = await payloadWriterTask;
        bool completedCalled = await payloadWriter.CompleteCalled;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(completedCalled, Is.True);
            Assert.That(payloadWriter.CompleteException?.GetType(), Is.EqualTo(exceptionType));
        });
    }

    /// <summary>Ensures that the request payload and payload stream pipe readers are completed if the connection is
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Request_payload_completed_when_connection_is_shutdown(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Client.ShutdownAsync("", default);

        var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        PayloadPipeReaderDecorator? payloadStream = null;
        if (HasPayloadStreamSupport(protocol))
        {
            payloadStream = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        }
        var request = CreateRequest(protocol, isOneway: false, payload, payloadStream);

        // Act
        Task<IncomingResponse> sendRequestTask = sut.Client.InvokeAsync(request);
        bool completeCalled = await payload.CompleteCalled;

        // Assert
        Assert.Multiple(async () =>
        {
            Assert.ThrowsAsync<ConnectionClosedException>(() => sendRequestTask);
            Assert.That(completeCalled, Is.True);
            Assert.That(payload.CompleteException, Is.InstanceOf<ConnectionClosedException>());
            if (payloadStream != null)
            {
                Assert.That(await payloadStream.CompleteCalled, Is.True);
                Assert.That(payloadStream.CompleteException, Is.InstanceOf<ConnectionClosedException>());
            }
        });
    }

    private static OutgoingRequest CreateRequest(
        Protocol protocol,
        bool isOneway = false,
        PipeReader? payload = null,
        PipeReader? payloadStream = null,
        Func<PipeWriter, PipeWriter>? writer = null,
        bool invalidFields = false)
    {
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            Payload = payload ?? EmptyPipeReader.Instance,
            PayloadStream = payloadStream
        };
        if (invalidFields)
        {
            request.Fields = request.Fields.With(
                RequestFieldKey.Idempotent,
                (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));
        }
        if (writer != null)
        {
            request.Use(writer);
        }
        return request;
    }

    private static IDispatcher CreateResponseDispatcher(
        PipeReader? payload = null,
        Func<PipeWriter, PipeWriter>? writer = null,
        PipeReader? payloadStream = null,
        bool invalidFields = false) =>
        new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = payload ?? EmptyPipeReader.Instance,
                    PayloadStream = payloadStream
                };
                if (invalidFields)
                {
                    response.Fields = response.Fields.With(
                        ResponseFieldKey.CompressionFormat,
                        (ref SliceEncoder encoder) => throw new NotSupportedException("invalid response fields"));
                }
                if (writer != null)
                {
                    response.Use(writer);
                }
                return new(response);
            });

    private static bool HasPayloadStreamSupport(Protocol protocol) => protocol == Protocol.IceRpc;

    private static PipeWriter InvalidPayloadWriter(PipeWriter _) => InvalidPipeWriter.Instance;
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
