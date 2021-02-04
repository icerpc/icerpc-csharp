// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests
{
    public class CollocatedTest
    {
        private protected Communicator Communicator { get; }
        private protected ObjectAdapter ObjectAdapter { get; }

        public CollocatedTest()
        {
            Communicator = new Communicator();
            ObjectAdapter = new ObjectAdapter(Communicator);
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            await ObjectAdapter.DisposeAsync();
            await Communicator.DisposeAsync();
        }
    }
}