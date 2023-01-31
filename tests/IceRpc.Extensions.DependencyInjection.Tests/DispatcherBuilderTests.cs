// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Builder;
using IceRpc.Builder.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Extensions.DependencyInjection.Tests;

public sealed class DispatcherBuilderTests
{
    /// <summary>Verifies that DispatcherBuilder.Map works with scoped services.</summary>
    [Test]
    public async Task Map_dispatches_to_service()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICallTracker, CallTracker>()
            .AddScoped<ITestService, TestService>()
            .BuildServiceProvider(true);

        var builder = new DispatcherBuilder(provider);
        builder.Map<ITestService>("/foo");
        IDispatcher dispatcher = builder.Build();

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance) { Path = "/foo" };
        _ = await dispatcher.DispatchAsync(request);

        Assert.That(provider.GetRequiredService<ICallTracker>().Count, Is.EqualTo(1));
    }

    /// <summary>Verifies that DispatcherBuilder.Mount works with scoped services.</summary>
    [Test]
    public async Task Mount_dispatches_to_service()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICallTracker, CallTracker>()
            .AddScoped<ITestService, TestService>()
            .BuildServiceProvider(true);

        var builder = new DispatcherBuilder(provider);
        builder.Mount<ITestService>("/");
        IDispatcher dispatcher = builder.Build();
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance) { Path = "/foo" };

        _ = await dispatcher.DispatchAsync(request);

        Assert.That(provider.GetRequiredService<ICallTracker>().Count, Is.EqualTo(1));
    }

    /// <summary>Verifies that UseMiddleware with a single service dependency works with a scoped service dependency.
    /// </summary>
    [Test]
    public async Task UseMiddleware_with_single_service_dependency()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICallTracker, CallTracker>()
            .AddSingleton<IPathTracker, PathTracker>()
            .AddScoped<IUser, User>()
            .AddScoped<ITestService, TestService>()
            .BuildServiceProvider(true);

        var builder = new DispatcherBuilder(provider);
        builder.UseMiddleware<UserMiddleware, IUser>();
        builder.Map<ITestService>("/foo");
        IDispatcher dispatcher = builder.Build();
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance) { Path = "/foo" };

        _ = await dispatcher.DispatchAsync(request);

        Assert.That(provider.GetRequiredService<ICallTracker>().Count, Is.EqualTo(1));
        Assert.That(provider.GetRequiredService<IPathTracker>().Path, Is.EqualTo("/foo"));
    }

    /// <summary>Verifies that UseMiddleware with a 3 service dependencies works with scoped service dependencies.
    /// </summary>
    [Test]
    public async Task UseMiddleware_with_3_service_dependencies()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ICallTracker, CallTracker>()
            .AddSingleton<IPathTracker, PathTracker>()
            .AddScoped<IUser, User>()
            .AddScoped<IDep2, Dep2>()
            .AddScoped<IDep3, Dep3>()
            .AddScoped<ITestService, TestService>()
            .BuildServiceProvider(true);

        var builder = new DispatcherBuilder(provider);
        builder.UseMiddleware<TripleMiddleware, IUser, IDep2, IDep3>();
        builder.Map<ITestService>("/foo");
        IDispatcher dispatcher = builder.Build();
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance) { Path = "/foo" };

        _ = await dispatcher.DispatchAsync(request);

        Assert.That(provider.GetRequiredService<ICallTracker>().Count, Is.EqualTo(3));
        Assert.That(provider.GetRequiredService<IPathTracker>().Path, Is.EqualTo("/foo"));
    }

    public interface ITestService
    {
        void DoIt(); // just to have a non-empty interface
    }

    public class TestService : ITestService, IDispatcher
    {
        private readonly ICallTracker _callTracker;

        public TestService(ICallTracker callTracker) => _callTracker = callTracker;

        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
        {
            _callTracker.Called();
            DoIt();
            return new(new OutgoingResponse(request));
        }

        public void DoIt()
        {
        }
    }

    public interface IUser
    {
        string Path { get; set; }
    }

    public class User : IUser
    {
        private readonly IPathTracker _pathTracker;

        public string Path
        {
            get => _pathTracker.Path;
            set => _pathTracker.Path = value;
        }

        public User(IPathTracker pathTracker) => _pathTracker = pathTracker;
    }

    public interface IDep2
    {
        void DoIt();
    }

    public class Dep2 : IDep2
    {
        public void DoIt()
        {
        }

        public Dep2(ICallTracker callTracker) => callTracker.Called();
    }

    public interface IDep3
    {
        void DoIt();
    }

    public class Dep3 : IDep3
    {
        public void DoIt()
        {
        }

        public Dep3(ICallTracker callTracker) => callTracker.Called();
    }

    public class UserMiddleware : IMiddleware<IUser>
    {
        private readonly IDispatcher _next;

        public UserMiddleware(IDispatcher next) => _next = next;

        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, IUser dep, CancellationToken cancellationToken)
        {
            var user = dep;
            user.Path = request.Path;
            return _next.DispatchAsync(request, cancellationToken);
        }
    }

    public class TripleMiddleware : IMiddleware<IUser, IDep2, IDep3>
    {
        private readonly IDispatcher _next;

        public TripleMiddleware(IDispatcher next) => _next = next;

        public ValueTask<OutgoingResponse> DispatchAsync(
            IncomingRequest request,
            IUser dep1,
            IDep2 dep2,
            IDep3 dep3,
            CancellationToken cancellationToken)
        {
            var user = dep1;
            user.Path = request.Path;
            return _next.DispatchAsync(request, cancellationToken);
        }
    }

    public interface ICallTracker
    {
        int Count { get; }

        void Called();
    }

    public class CallTracker : ICallTracker
    {
        public int Count { get; private set; }

        public void Called() => Count++;
    }

    public interface IPathTracker
    {
        string Path { get; set; }
    }

    public class PathTracker : IPathTracker
    {
        public string Path { get; set; } = "/invalid";
    }
}
