// Copyright (c) ZeroC, Inc. All rights reserved.

using HelloSecureExample;
using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options with a custom certificate validation callback
// that uses our Root CA certificate.

using var rootCA = new X509Certificate2("../../certs/cacert.der");
var clientAuthenticationOptions = new SslClientAuthenticationOptions()
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

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"), clientAuthenticationOptions);

var hello = new HelloProxy(connection);

string greeting = await hello.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);
