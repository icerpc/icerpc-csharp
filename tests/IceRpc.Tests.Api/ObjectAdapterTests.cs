// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class ObjectAdapterTests
    {
        /// <summary>Test that the communicator default dispatch interceptors are used when the
        /// object adapter doesn't specify its own interceptors.</summary>
        [Test]
        public void ObjectAdapterInvocationInterceptors()
        {
            var communicator = new Communicator
            {
                DefaultDispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                    (request, current, next, cancel) => throw new NotImplementedException(),
                    (request, current, next, cancel) => throw new NotImplementedException())
            };
            var objectAdapter = communicator.CreateObjectAdapter("TestAdapter-1");

            CollectionAssert.AreEqual(communicator.DefaultDispatchInterceptors, objectAdapter.DispatchInterceptors);
        }
    }
}