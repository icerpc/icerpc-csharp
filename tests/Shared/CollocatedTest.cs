// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests
{
    /// <summary>Test fixture for tests that need to create a collocated server. The constructor initialize
    /// a communicator and an ObjectAdapter.</summary>
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