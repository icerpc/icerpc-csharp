// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Extensions.DependencyInjection.Tests;

/// <summary>Demonstrates the issue reported in #4424: the per-request DI scope is disposed by
/// <see cref="DispatcherBuilder" /> as soon as the dispatcher returns its <see cref="OutgoingResponse" />, but the
/// protocol connection only reads <c>response.Payload</c> afterwards. A payload that lazily pulls from a scoped
/// service therefore hits an <see cref="ObjectDisposedException" /> mid-transmission.</summary>
public sealed class ScopedPayloadDisposalTests
{
    /// <summary>This test documents the limitation: a response payload that lazily pulls from a scoped service will
    /// fail because the per-request DI scope is already disposed by the time the protocol connection reads the
    /// payload. Application code must materialize such data inside the dispatcher (under the scope) instead of
    /// relying on lazy reads.</summary>
    [Test]
    public async Task Scoped_service_is_disposed_before_response_payload_is_read()
    {
        // Arrange: a scoped service standing in for "scoped HttpClient" / "EF Core DbContext" — i.e. a disposable
        // dependency resolved from the per-request scope.
        await using ServiceProvider provider = new ServiceCollection()
            .AddScoped<IScopedHttpClient, ScopedHttpClient>()
            .AddScoped<StreamingService>()
            .BuildServiceProvider(validateScopes: true);

        var builder = new DispatcherBuilder(provider);
        builder.Map<StreamingService>("/test");
        IDispatcher dispatcher = builder.Build();

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Path = "/test"
        };

        // Act: dispatch the request. The dispatcher's await using over the AsyncServiceScope disposes the scope as
        // soon as DispatchAsync returns the OutgoingResponse — *before* anyone reads the payload.
        OutgoingResponse response = await dispatcher.DispatchAsync(request);

        // Sanity: the scoped service captured by the lazy payload was indeed disposed by the dispatcher's scope
        // teardown.
        var lazyPayload = (LazyScopedPipeReader)response.Payload;
        Assert.That(lazyPayload.CapturedClient.IsDisposed, Is.True);

        // Assert: this is what the protocol connection does at IceRpcProtocolConnection.cs:~1101 —
        // payloadWriter.CopyFromAsync(response.Payload, ...). Because the scope is already gone, the first ReadAsync
        // surfaces ObjectDisposedException from the scoped dependency.
        Assert.That(
            async () => await response.Payload.ReadAsync(),
            Throws.InstanceOf<ObjectDisposedException>());

        response.Payload.Complete();
    }

    public interface IScopedHttpClient
    {
        bool IsDisposed { get; }

        // Returns the next "chunk" of the streamed response.
        ValueTask<ReadOnlyMemory<byte>> ReadChunkAsync();
    }

    public sealed class ScopedHttpClient : IScopedHttpClient, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;

        public ValueTask<ReadOnlyMemory<byte>> ReadChunkAsync()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return new ValueTask<ReadOnlyMemory<byte>>(new byte[] { 1, 2, 3, 4 });
        }
    }

    /// <summary>An <see cref="IDispatcher" /> mapped at <c>/test</c>. It captures the request's scoped
    /// <see cref="IScopedHttpClient" /> and returns a response whose payload reads from it lazily — exactly the
    /// pattern the audit calls out.</summary>
    public sealed class StreamingService : IDispatcher
    {
        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
        {
            IServiceProvider scopedProvider = request.Features.Get<IServiceProviderFeature>()!.ServiceProvider;
            var scopedClient = scopedProvider.GetRequiredService<IScopedHttpClient>();

            return new ValueTask<OutgoingResponse>(
                new OutgoingResponse(request)
                {
                    Payload = new LazyScopedPipeReader(scopedClient)
                });
        }
    }

    /// <summary>A minimal PipeReader whose data is produced lazily by a scoped dependency. This mirrors a streamed
    /// reply backed by a scoped HttpClient or DbContext.</summary>
    private sealed class LazyScopedPipeReader : PipeReader
    {
        public IScopedHttpClient CapturedClient { get; }

        public LazyScopedPipeReader(IScopedHttpClient client) => CapturedClient = client;

        public override void AdvanceTo(SequencePosition consumed)
        {
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
        }

        public override void CancelPendingRead()
        {
        }

        public override void Complete(Exception? exception = null)
        {
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadOnlyMemory<byte> chunk = await CapturedClient.ReadChunkAsync().ConfigureAwait(false);
            return new ReadResult(new ReadOnlySequence<byte>(chunk), isCanceled: false, isCompleted: true);
        }

        public override bool TryRead(out ReadResult result)
        {
            result = default;
            return false;
        }
    }
}
