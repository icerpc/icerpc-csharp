// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable]
    public class TlsConfigurationTests : ClientServerBaseTest
    {
        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_TlsOptions(string clientCertFile, string serverCertFile, string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 1);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_ValidationCallback(string clientCertFile, string serverCertFile)
        {
            bool clientValiadationCallbackCalled = false;
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        clientValiadationCallbackCalled = true;
                        return true;
                    }
                });

            bool serverValiadationCallbackCalled = false;
            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        serverValiadationCallbackCalled = true;
                        return true;
                    }
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 2);
            Assert.IsTrue(clientValiadationCallbackCalled);
            Assert.IsTrue(serverValiadationCallbackCalled);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_CertificateProperties(string clientCertFile, string serverCertFile, string caFile)
        {
            await using var clientCommunicator = CreateCommunicator(clientCertFile);
            await using var serverCommunicator = CreateCommunicator(serverCertFile);

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 3);

            Communicator CreateCommunicator(string certFile) =>
                new Communicator(new Dictionary<string, string>()
                {
                    { "IceSSL.DefaultDir", GetCertificatesDir() },
                    { "IceSSL.CertFile", certFile },
                    { "IceSSL.CAs", caFile },
                    { "IceSSL.Password", "password" }
                });
        }
        
        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "Test code")]
        public void TlsConfiguration_With_InvalidOptions()
        {
            // Certificate authorities and validation callback settings are mutually exclusive
            var clientOptions = new TlsClientOptions
            {
                ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                }
            };
            _ = Assert.Throws<ArgumentException>(() =>
                clientOptions.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true);

            clientOptions = new TlsClientOptions
            {
                ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            _ = Assert.Throws<ArgumentException>(() =>
                clientOptions.ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                });

            var serverOptions = new TlsServerOptions
            {
                ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                }
            };
            _ = Assert.Throws<ArgumentException>(() =>
                serverOptions.ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true);

            serverOptions = new TlsServerOptions
            {
                ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            _ = Assert.Throws<ArgumentException>(() =>
                serverOptions.ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                });
        }

        private static string GetCertificatesDir() =>
           Path.Combine(Environment.CurrentDirectory, "certs");

        private static string GetCertificatePath(string file) =>
            Path.Combine(GetCertificatesDir(), file);

        private async Task WithSecureServerAsnyc(
            Communicator clientCommunicator,
            Communicator serverCommunicator,
            int portNumber)
        {
            await using var server = new Server(serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: portNumber),
                    AcceptNonSecure = NonSecure.Never,
                });

            server.Add("hello", new GreeterTestService());
            await server.ActivateAsync();

            var prx = IServicePrx.Parse(GetTestProxy("hello", port: portNumber), clientCommunicator).Clone(preferNonSecure: NonSecure.Never);
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
        }
        internal class GreeterTestService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}