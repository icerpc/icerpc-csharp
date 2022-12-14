// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Transports;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Demo;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class Program
{
    public static async Task Main()
    {
        using var rootCA = new X509Certificate2("../../certs/cacert.der");
        var clientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            {
                using var customChain = new X509Chain();
                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                customChain.ChainPolicy.DisableCertificateDownloads = true;
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
                return customChain.Build((X509Certificate2)certificate!);
            }
        };

        await using var connection = new ClientConnection(
            new Uri("icerpc://127.0.0.1"),
            clientAuthenticationOptions,
            multiplexedClientTransport: new QuicClientTransport());

        var helloProxy = new HelloProxy(connection);
        string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

        Console.WriteLine(greeting);
    }
}
