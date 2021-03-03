// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable]
    public class InvocationRetriesTests : ClientServerBaseTest
    {
        [Test]
        public void InvocationRetries_Cancelation()
        {
        }

        public sealed class NonReplicated : IAsyncRetryNonReplicatedService
        {
            public ValueTask OtherReplicaAsync(Current current, CancellationToken cancel) =>
                throw new RetrySystemFailure(RetryPolicy.OtherReplica);
        }


        internal class RetryService : IAsyncRetryService
        {
            private int _counter;

            public ValueTask<int> OpAfterDelayAsync(int retries, int delay, Current current, CancellationToken cancel)
            {
                if (retries > _counter)
                {
                    _counter++;
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                }
                int counter = _counter;
                _counter = 0;
                return new(counter);
            }

            public ValueTask OpAsync(bool kill, Current current, CancellationToken cancel)
            {
                if (kill)
                {
                    current.Connection.AbortAsync();
                }
                return default;
            }

            public async ValueTask OpBidirRetryAsync(IRetryBidirServicePrx bidir, Current current, CancellationToken cancel)
            {
                bidir = bidir.Clone(fixedConnection: current.Connection);
                Assert.ThrowsAsync< ServiceNotFoundException>(
                    async () => await bidir.OtherReplicaAsync(cancel: CancellationToken.None));

                // With Ice1 the exception is not retryable, with Ice2 we can retry using the existing connection
                // because the exception uses the AfterDelay retry policy.
                try
                {
                    await bidir.AfterDelayAsync(2, cancel: CancellationToken.None);
                    Assert.IsTrue(current.Protocol == Protocol.Ice2);
                }
                catch (ServiceNotFoundException)
                {
                    Assert.IsTrue(current.Protocol == Protocol.Ice1);
                }
            }

            public ValueTask<int> OpIdempotentAsync(int nRetry, Current current, CancellationToken cancel)
            {
                int[] delays = new int[] { 0, 1, 10000 };
                if (nRetry > _counter)
                {
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delays[_counter++ % 3])));
                }
                int counter = _counter;
                _counter = 0;
                return new(counter);
            }

            public ValueTask OpNotIdempotentAsync(Current current, CancellationToken cancel) =>
                throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));

            public ValueTask OpSystemExceptionAsync(Current current, CancellationToken cancel) =>
                throw new RetrySystemFailure();

            public ValueTask OpWithDataAsync(
                int retries,
                int delay,
                byte[] data,
                Current current,
                CancellationToken cancel)
            {
                if (retries > _counter++)
                {
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                }
                _counter = 0;
                return default;
            }

            public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
            {
                current.Server.ShutdownAsync();
                return default;
            }

            public async ValueTask SleepAsync(int delay, Current current, CancellationToken cancel) =>
                await Task.Delay(delay, cancel);
        }
    }
}
