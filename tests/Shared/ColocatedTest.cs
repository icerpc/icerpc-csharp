// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading.Tasks;

namespace IceRpc.Tests
{
    /// <summary>Test fixture for tests that need to create a colocated server. The constructor initialize
    /// a communicator and an Server.</summary>
    public class ColocatedTest
    {
        private protected Communicator Communicator { get; }
        private protected Server Server { get; }

        public ColocatedTest()
        {
            Communicator = new Communicator();
            Server = new Server(Communicator);
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            await Server.DisposeAsync();
            await Communicator.DisposeAsync();
        }
    }
}
