// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable]
    public class TlsConfigurationTests : ClientServerBaseTest
    {
        public async Task TlsConfiguration_Properties(string clientCertFile, string serverCertFile, string caFile)
        {
            await using var clientCommunicator = CreateCommunicator(clientCertFile, caFile, "password");
            await using var serverCommunicator = CreateCommunicator(serverCertFile, caFile, "password");

            await using var server = new Server(serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(1)
                });
        }

        private static Communicator CreateCommunicator(
            string certFile,
            string caFile,
            string password = "")
        {
            var properties = new Dictionary<string, string>()
            {
                { "IceSSL.CertFile", certFile },
                { "IceSSL.CAs", caFile }
            };

            if (password.Length > 0)
            {
                properties["IceSSL.Password"] = password;
            }

            return new Communicator(properties);
        }
    }
}