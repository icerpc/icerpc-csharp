// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options with a custom certificate validation callback
// that uses our Root CA certificate.

using var rootCA = new X509Certificate2("../../certs/cacert.der");
var authenticationOptions = new SslClientAuthenticationOptions()
{
    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
    {
        chain = new X509Chain();
        try
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Clear();
            chain.ChainPolicy.CustomTrustStore.Add(rootCA);
            return chain.Build((X509Certificate2)certificate!);
        }
        finally
        {
            chain.Dispose();
        }
    }
};

await using var connection = new Connection("icerpc://127.0.0.1", authenticationOptions);

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
