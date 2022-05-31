// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Extensions.DependencyInjection.Builder;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Extensions.DependencyInjection.Tests;

public sealed class DispatcherBuilderTests
{
    [Test]
    /// <summary>Verifies that DispatcherBuilder.Map works with singleton, scoped and transient services.</summary>
    public async Task Map_dispatches_to_service_with_any_lifetime([Values] ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection().AddSingleton<ICallTracker, CallTracker>();
        var descriptor = new ServiceDescriptor(typeof(ITestService), typeof(TestService), lifetime);
        services.Add(descriptor);
        await using var provider = services.BuildServiceProvider(true);
        var dispatcherBuilder = new DispatcherBuilder(provider);
        dispatcherBuilder.Map<ITestService>("/foo");
        IDispatcher dispatcher = dispatcherBuilder.Build();

        _ = await dispatcher.DispatchAsync(new IncomingRequest(InvalidConnection.IceRpc) { Path = "/foo" });

        Assert.That(provider.GetRequiredService<ICallTracker>().Called, Is.True);
    }

    [Test]
    /// <summary>Verifies that DispatcherBuilder.Mount works with singleton, scoped and transient services.</summary>
    public async Task Mount_dispatches_to_service_with_any_lifetime([Values] ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection().AddSingleton<ICallTracker, CallTracker>();
        var descriptor = new ServiceDescriptor(typeof(ITestService), typeof(TestService), lifetime);
        services.Add(descriptor);
        await using var provider = services.BuildServiceProvider(true);
        var dispatcherBuilder = new DispatcherBuilder(provider);
        dispatcherBuilder.Mount<ITestService>("/");
        IDispatcher dispatcher = dispatcherBuilder.Build();

        _ = await dispatcher.DispatchAsync(new IncomingRequest(InvalidConnection.IceRpc) { Path = "/foo" });

        Assert.That(provider.GetRequiredService<ICallTracker>().Called, Is.True);
    }

    [Test]
    /// <summary>Verifies that UseMiddleware with a single service dependency works with a scoped service dependency.
    /// </summary>
    public async Task UseMiddleware_with_single_service_dependency()
    {
        IServiceCollection services = new ServiceCollection()
            .AddSingleton<ICallTracker, CallTracker>()
            .AddSingleton<IPathTracker, PathTracker>()
            .AddScoped<IUser, User>()
            .AddScoped<ITestService, TestService>();

        await using var provider = services.BuildServiceProvider(true);
        var dispatcherBuilder = new DispatcherBuilder(provider);
        dispatcherBuilder.UseMiddleware<UserMiddleware, IUser>();
        dispatcherBuilder.Map<ITestService>("/foo");
        IDispatcher dispatcher = dispatcherBuilder.Build();

        _ = await dispatcher.DispatchAsync(new IncomingRequest(InvalidConnection.IceRpc) { Path = "/foo" });

        Assert.That(provider.GetRequiredService<ICallTracker>().Called, Is.True);
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

        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            _callTracker.Called = true;
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

    public class UserMiddleware : IMiddleware<IUser>
    {
        private readonly IDispatcher _next;

        public UserMiddleware(IDispatcher next) => _next = next;

        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, IUser dep, CancellationToken cancel)
        {
            dep.Path = request.Path;
            return _next.DispatchAsync(request, cancel);
        }
    }

    public interface ICallTracker
    {
        bool Called { get; set; }
    }

    public class CallTracker : ICallTracker
    {
        public bool Called { get; set; }
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
