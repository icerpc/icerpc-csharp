// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading.Tasks;

namespace IceRpc.Tests
{
    /// <summary>Test fixture for tests that need to create a colocated server. The constructor initializes
    /// a communicator and a Server.</summary>
    public class ColocTest
    {
        private protected Communicator Communicator { get; }
        private protected Server Server { get; }

        public ColocTest()
        {
            Communicator = new Communicator();
            Server = new Server
            {
                Communicator = Communicator
            };
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            await Server.DisposeAsync();
            await Communicator.DisposeAsync();
        }
    }
}
