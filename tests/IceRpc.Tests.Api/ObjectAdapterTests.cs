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
        public void ObjectAdapter_DefaultDispatchInterceptors()
        {
            var communicator = new Communicator
            {
                DefaultDispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                    (request, current, next, cancel) => throw new NotImplementedException(),
                    (request, current, next, cancel) => throw new NotImplementedException())
            };
            var adapter = new ObjectAdapter(communicator);
            CollectionAssert.AreEqual(communicator.DefaultDispatchInterceptors, adapter.DispatchInterceptors);
        }
    }
}