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
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 1,
                prx =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                    return Task.CompletedTask;
                });
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
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 2,
                prx =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                    return Task.CompletedTask;
                });
            Assert.IsTrue(clientValiadationCallbackCalled);
            Assert.IsTrue(serverValiadationCallbackCalled);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_CertificateProperties(string clientCertFile, string serverCertFile, string caFile)
        {
            await using var clientCommunicator = CreateCommunicator(clientCertFile);
            await using var serverCommunicator = CreateCommunicator(serverCertFile);

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 3,
                prx =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                    return Task.CompletedTask;
                });

            Communicator CreateCommunicator(string certFile) =>
                new Communicator(new Dictionary<string, string>()
                {
                    { "IceSSL.DefaultDir", GetCertificatesDir() },
                    { "IceSSL.CertFile", certFile },
                    { "IceSSL.CAs", caFile },
                    { "IceSSL.Password", "password" }
                });
        }

        [TestCase("s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_CertificateSelectionCallback(string serverCertFile, string caFile)
        {
            var cert0 = new X509Certificate2(GetCertificatePath("c_rsa_ca1.p12"), "password");
            var cert1 = new X509Certificate2(GetCertificatePath("c_rsa_ca2.p12"), "password");
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        cert0,
                        cert1
                    },
                    ClientCertificateSelectionCallback =
                        (sender, targetHost, certs, remoteCertificate, acceptableIssuers) =>
                        {
                            Assert.AreEqual(2, certs.Count);
                            Assert.AreEqual(cert0, certs[0]);
                            Assert.AreEqual(cert1, certs[1]);
                            return certs[0];
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
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 4,
                prx =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                    return Task.CompletedTask;
                });
        }

        [TestCase("s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithOutClientCert(string serverCertFile, string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
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
                    RequireClientCertificate = true,
                });

            // This should throw the server request a certificate and the client doesn't provide one
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 5,
                prx =>
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
                    return Task.CompletedTask;
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedServer(string clientCertFile, string serverCertFile, string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    }
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                });

            // This should throw the client doesn't trust the server certificate.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator, 6,
                prx =>
                {
                    Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
                    return Task.CompletedTask;
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
            int portNumber,
            Func<IServicePrx, Task> closure)
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

            await closure(prx);
        }
        internal class GreeterTestService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}