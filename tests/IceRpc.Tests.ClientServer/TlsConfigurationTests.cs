// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class TlsConfigurationTests
    {
        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        [TestCase("cacert2.p12", "cacert2.p12", "cacert2.der")] // Using self-signed certs
        public async Task TlsConfiguration_With_TlsOptions(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    RemoteCertificateValidationCallback = CertificateValidaton.GetClientCertificateValidationCallback(
                        clientCertificateRequired: true,
                        certificateAuthorities: new X509Certificate2Collection()
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        }),
                    ClientCertificateRequired = true,
                })
                .AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        })
                })
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(prx.Proxy.Connection!.IsSecure, Is.True);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_ValidationCallback(string clientCertFile, string serverCertFile)
        {
            bool clientValidationCallbackCalled = false;
            bool serverValidationCallbackCalled = false;

            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        serverValidationCallbackCalled = true;
                        return true;
                    },
                    ClientCertificateRequired = true,
                })
                .AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        clientValidationCallbackCalled = true;
                        return true;
                    }
                })
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(prx.Proxy.Connection!.IsSecure, Is.True);
            Assert.That(clientValidationCallbackCalled, Is.True);
            Assert.That(serverValidationCallbackCalled, Is.True);
        }

        [Test]
        public async Task TlsConfiguration_With_CertificateSelectionCallback()
        {
            using var cert0 = new X509Certificate2(GetCertificatePath("c_rsa_ca1.p12"), "password");
            using var cert1 = new X509Certificate2(GetCertificatePath("c_rsa_ca2.p12"), "password");

            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        Assert.That(cert0.GetCertHash(), Is.EqualTo(certificate!.GetCertHash()));
                        return certificate!.GetCertHash().SequenceEqual(cert0.GetCertHash());
                    },
                    ClientCertificateRequired = true,
                })
                .AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        cert0,
                        cert1
                    },
                    LocalCertificateSelectionCallback =
                        (sender, targetHost, certs, remoteCertificate, acceptableIssuers) =>
                        {
                            Assert.That(certs.Count, Is.EqualTo(2));
                            Assert.That(certs[0], Is.EqualTo(cert0));
                            Assert.That(certs[1], Is.EqualTo(cert1));
                            return certs[0];
                        },
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath("cacert1.der"))
                        }),
                })
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(prx.Proxy.Connection!.IsSecure, Is.True);
        }

        // The client doesn't have a CA certificate to verify the server
        [TestCase("s_rsa_ca1.p12", "")]
        // Server certificate not trusted by the client configured CA
        [TestCase("s_rsa_ca2.p12", "cacert1.der")]
        // Server certificate expired
        [TestCase("s_rsa_ca1_exp.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedServer(string serverCertFile, string caFile)
        {
            IServiceCollection serviceCollection = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password")
                });
            if (caFile.Length > 0)
            {
                serviceCollection.AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        })
                });
            }
            await using ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            Assert.ThrowsAsync<AuthenticationException>(async () => await prx.IcePingAsync());
        }

        // The server doesn't have a CA certificate to verify the client
        [TestCase("c_rsa_ca1.p12", "")]
        // Client certificate not trusted by the server CA
        [TestCase("c_rsa_ca2.p12", "cacert1.der")]
        // Client certificate expired
        [TestCase("c_rsa_ca1_exp.p12", "cacert1.der")]
        // The server requests a certificate but the client doesn't provide one
        [TestCase("", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedClient(string clientCertFile, string caFile)
        {
            var tlsClientOptions = new SslClientAuthenticationOptions()
            {
                RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                    certificateAuthorities: new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    })
            };

            if (clientCertFile.Length > 0)
            {
                tlsClientOptions.ClientCertificates = new X509Certificate2Collection()
                {
                    new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                };
            }

            var tlsServerOptions = new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                ClientCertificateRequired = true,
            };

            if (caFile.Length != 0)
            {
                tlsServerOptions.RemoteCertificateValidationCallback =
                    CertificateValidaton.GetClientCertificateValidationCallback(
                        clientCertificateRequired: true,
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        });
            }

            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => tlsServerOptions)
                .AddTransient(_ => tlsClientOptions)
                .BuildServiceProvider();
            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();

            Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
        }

        // This must succeed, the target host matches the certificate DNS altName.
        [TestCase("s_rsa_ca1_cn1.p12", "localhost", OperatingSystem.All)]
        // This must fail, the target host does not match the certificate DNS altName.
        [TestCase("s_rsa_ca1_cn2.p12", "localhost", OperatingSystem.None)]
        // This must succeed, the target host matches the certificate Common Name and the certificate
        // does not include a DNS altName.
        [TestCase("s_rsa_ca1_cn3.p12", "localhost", OperatingSystem.All & ~OperatingSystem.MacOS)]
        // This must fail, the target host does not match the certificate Common Name and the
        // certificate does not include a DNS altName.
        [TestCase("s_rsa_ca1_cn4.p12", "localhost", OperatingSystem.None)]
        // This must fail, the target host matches the certificate Common Name and the certificate has
        // a DNS altName that does not matches the target host
        [TestCase("s_rsa_ca1_cn5.p12", "localhost", OperatingSystem.None)]
        // Target host matches the certificate IP altName
        [TestCase("s_rsa_ca1_cn6.p12", "::1", OperatingSystem.All)]
        // Target host does not match the certificate IP altName
        [TestCase("s_rsa_ca1_cn7.p12", "::1", OperatingSystem.None)]
        // Target host is an IP address that matches the CN and the certificate doesn't include an IP
        // altName
        [TestCase("s_rsa_ca1_cn8.p12", "::1", OperatingSystem.All & ~OperatingSystem.MacOS)]
        public async Task TlsConfiguration_HostnameVerification(
            string serverCertFile,
            string targetHost,
            OperatingSystem mustSucceed)
        {
            string serverHost = targetHost switch
            {
                "localhost" => System.OperatingSystem.IsWindows() ? "[::1]" : "127.0.0.1",
                _ => $"[{targetHost}]"
            };

            string clientHost = targetHost switch
            {
                "localhost" => targetHost,
                _ => $"[{targetHost}]"
            };

            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection(serverHost)
                .AddTransient(typeof(Endpoint), _ => Endpoint.FromString($"icerpc://{serverHost}:0"))
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password")
                })
                .AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback =
                        CertificateValidaton.GetServerCertificateValidationCallback(
                            certificateAuthorities: new X509Certificate2Collection
                            {
                                new X509Certificate2(GetCertificatePath("cacert1.der"))
                            })
                })
                .AddTransient<Connection>(serviceProvider =>
                {
                    Server server = serviceProvider.GetRequiredService<Server>();
                    return new Connection(new ConnectionOptions
                    {
                        AuthenticationOptions = serviceProvider.GetService<SslClientAuthenticationOptions>(),
                        MultiplexedClientTransport =
                            serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                        RemoteEndpoint = $"icerpc://{clientHost}:{server.Endpoint.Port}"
                    });
                })
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            if ((GetOperatingSystem() & mustSucceed) != 0)
            {
                Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                Assert.That(prx.Proxy.Connection!.IsSecure, Is.True);
            }
            else
            {
                Assert.ThrowsAsync<AuthenticationException>(async () => await prx.IcePingAsync());
            }
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols values",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_CommonProtocol()
        {
            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                })
                .AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath("cacert1.der"))
                        }),
                    EnabledSslProtocols = SslProtocols.Tls12
                })
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(prx.Proxy.Connection!.IsSecure, Is.True);
            Assert.That(prx.Proxy.Connection!.NetworkConnectionInformation?.RemoteCertificate, Is.Not.Null);
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5397:Do not use deprecated SslProtocols values",
            Justification = "Test code")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols values",
            Justification = "Test code")]
        public async Task TlsConfiguration_Fail_NoCommonProtocol()
        {
            // This should throw the client and the server doesn't enable a common protocol.
            await using ServiceProvider serviceProvider = new TlsIntegrationTestServiceCollection()
                .AddTransient(_ => new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    EnabledSslProtocols = SslProtocols.Tls11
                })
                .AddTransient(_ => new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath("cacert1.der"))
                        }),
                    EnabledSslProtocols = SslProtocols.Tls12
                })
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();
            Assert.ThrowsAsync<AuthenticationException>(async () => await prx.IcePingAsync());
        }

        private static string GetCertificatePath(string file) =>
            Path.Combine(Environment.CurrentDirectory, "certs", file);

        private class TlsIntegrationTestServiceCollection : IntegrationTestServiceCollection
        {
            internal TlsIntegrationTestServiceCollection(string? hostname = null)
            {
                string serverHost = hostname switch
                {
                    null => "[::1]",
                    "localhost" => System.OperatingSystem.IsWindows() ? "[::1]" : "127.0.0.1",
                    _ => hostname
                };
                this.UseTransport("tcp");
                this.UseVoidDispatcher();
                this.AddTransient(typeof(Endpoint), _ => Endpoint.FromString($"icerpc://{serverHost}:0"));
            }
        }

        [Flags]
        public enum OperatingSystem
        {
            None = 0,
            Linux = 1,
            Windows = 2,
            MacOS = 4,
            Other = 8,
            All = Linux | Windows | MacOS | Other
        }

        private static OperatingSystem GetOperatingSystem()
        {
            if (System.OperatingSystem.IsMacOS())
            {
                return OperatingSystem.MacOS;
            }
            else if (System.OperatingSystem.IsLinux())
            {
                return OperatingSystem.Linux;
            }
            else if (System.OperatingSystem.IsWindows())
            {
                return OperatingSystem.Windows;
            }
            else
            {
                return OperatingSystem.Other;
            }
        }
    }
}
