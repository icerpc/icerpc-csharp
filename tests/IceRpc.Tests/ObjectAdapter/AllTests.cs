// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ObjectAdapterTest
{
    [Parallelizable]
    public class AllTests : FunctionalTest
    {
        /// <summary>Test that the communicator default dispatch interceptors are used when the
        /// object adapter doesn't specify its own interceptors.</summary>
        [Test]
        public void ObjectAdapterInvocationInterceptors()
        {
            var communicator = new Communicator();
            communicator.DefaultDispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                (request, current, next, cancel) =>
                    {
                        throw new NotImplementedException();
                    },
                (request, current, next, cancel) =>
                    {
                        throw new NotImplementedException();
                    });
            var objectAdapter = communicator.CreateObjectAdapter(
                "TestAdapter-1",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint(port: 1)
                });

            CollectionAssert.AreEqual(communicator.DefaultDispatchInterceptors, objectAdapter.DispatchInterceptors);
        }
    }
}