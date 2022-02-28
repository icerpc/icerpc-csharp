// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class ExceptionTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ExceptionOperationsPrx _prx;

        public ExceptionTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, ExceptionOperations>()
                .BuildServiceProvider();
            _prx = ExceptionOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public void Exception_Constructors()
        {
            var a = new MyExceptionA(RetryPolicy.NoRetry);
            Assert.That(a.RetryPolicy, Is.EqualTo(RetryPolicy.NoRetry));

            a = new MyExceptionA(RetryPolicy.OtherReplica);
            Assert.That(a.RetryPolicy, Is.EqualTo(RetryPolicy.OtherReplica));

            a = new MyExceptionA(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            Assert.That(a.RetryPolicy, Is.EqualTo(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1))));

            a = new MyExceptionA();
            Assert.That(a.RetryPolicy, Is.EqualTo(RetryPolicy.NoRetry));

            Assert.That(new MyExceptionA(10).M1, Is.EqualTo(10));

            var b = new MyExceptionB("my exception B", 20, retryPolicy: RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            a = new MyExceptionA("my exception A", 10, b, RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            Assert.That(a.Message, Is.EqualTo("my exception A"));
            Assert.That(a.M1, Is.EqualTo(10));
            Assert.That(a, Is.Not.Null);
            Assert.That(a.InnerException, Is.EqualTo(b));
            Assert.That(a.RetryPolicy, Is.EqualTo(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1))));
        }

        [Test]
        public void Exception_Operations()
        {
            MyExceptionA? a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAAsync(10));
            Assert.That(a, Is.Not.Null);
            Assert.That(a!.M1, Is.EqualTo(10));

            a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAorBAsync(10));
            Assert.That(a, Is.Not.Null);
            Assert.That(a!.M1, Is.EqualTo(10));

            MyExceptionB? b = Assert.ThrowsAsync<MyExceptionB>(async () => await _prx.ThrowAorBAsync(0));
            Assert.That(b, Is.Not.Null);
            Assert.That(b!.M1, Is.EqualTo(0));
        }

        public class ExceptionOperations : Service, IExceptionOperations
        {
            public ValueTask ThrowAAsync(int a, Dispatch dispatch, CancellationToken cancel) => throw new MyExceptionA(a);
            public ValueTask ThrowAorBAsync(int a, Dispatch dispatch, CancellationToken cancel)
            {
                if (a > 0)
                {
                    throw new MyExceptionA(a);
                }
                else
                {
                    throw new MyExceptionB(a);
                }
            }
        }
    }
}
