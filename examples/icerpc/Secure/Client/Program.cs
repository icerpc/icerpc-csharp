// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Create the authentication options with a custom certificate validation callback that uses the test certificates'
// Root CA.

using var rootCA = new X509Certificate2("../../../../certs/cacert.der");
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

await using var connection = new ClientConnection(new Uri("icerpc://localhost"), clientAuthenticationOptions);

var greeter = new GreeterProxy(connection);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
