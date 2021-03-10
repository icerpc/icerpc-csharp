// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc
{
    public static class CertificateValidaton
    {
        /// <summary>Returns a certificatve validation callback that can be used to validate server certificates.
        /// </summary>
        /// <param name="useMachineContext">When <c>true</c> the certificate chain used to validate the server
        /// certificate is built using the machine context, otherwise the certificate chain uses the current user
        /// context.</param>
        /// <param name="certificateAuthorities">The certificates collection that will be used as trusted
        /// certificate authorities to verify the server certificate.</param>
        /// <returns>The certificate validation callback</returns>
        public static RemoteCertificateValidationCallback GetServerCertificateValidationCallback(
            bool useMachineContext = false,
            X509Certificate2Collection? certificateAuthorities = null) =>
            GetRemoteCertificateValidationCallback(true, useMachineContext, certificateAuthorities);

        /// <summary>Returns a certificatve validation callback that can be used to validate client certificates.
        /// </summary>
        /// <param name="clientCertificateRequired">When <c>true</c>the validation callback will only trust clients
        /// that provide a certificate context.</param>
        /// <param name="useMachineContext">When <c>true</c> the certificate chain used to validate the client
        /// certificate is built using the machine context, otherwise the certificate chain uses the current user
        /// context.</param>
        /// <param name="certificateAuthorities">The certificates collection that will be used as trusted
        /// certificate authorities to verify the client certificate.</param>
        /// <returns>The certificate validation callback</returns>
        public static RemoteCertificateValidationCallback GetClientCertificateValidationCallback(
            bool clientCertificateRequired = false,
            bool useMachineContext = false,
            X509Certificate2Collection? certificateAuthorities = null) =>
            GetRemoteCertificateValidationCallback(clientCertificateRequired,
                                                   useMachineContext,
                                                   certificateAuthorities);

        private static RemoteCertificateValidationCallback GetRemoteCertificateValidationCallback(
            bool peerCertificateRequired,
            bool useMachineContext,
            X509Certificate2Collection? certificateAuthorities)
        {
            return (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
            {
                if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) > 0)
                {
                    // For an outgoing connection the peer must always provide a certificate, for an incoming
                    // connection the certificate is only required if the RequireClientCertificate option was set.
                    if (peerCertificateRequired)
                    {
                        return false;
                    }
                    else
                    {
                        errors ^= SslPolicyErrors.RemoteCertificateNotAvailable;
                    }
                }

                if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) > 0)
                {
                    return false;
                }

                bool buildCustomChain = (certificateAuthorities != null || useMachineContext) && certificate != null;
                try
                {
                    // If using custom certificate authorities or the machine context and the peer provides a
                    // certificate, we rebuild the certificate chain with our custom chain policy.
                    if (buildCustomChain)
                    {
                        chain = new X509Chain(useMachineContext);
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                        if (certificateAuthorities != null)
                        {
                            // We need to set this flag to be able to use a certificate authority from the extra store.
                            chain.ChainPolicy.VerificationFlags =
                                X509VerificationFlags.AllowUnknownCertificateAuthority;
                            foreach (X509Certificate2 cert in certificateAuthorities)
                            {
                                chain.ChainPolicy.ExtraStore.Add(cert);
                            }
                        }
                        chain.Build((X509Certificate2)certificate!);
                    }

                    if (chain != null && chain.ChainStatus != null)
                    {
                        var chainStatus = new List<X509ChainStatus>(chain.ChainStatus);

                        if (certificateAuthorities != null)
                        {
                            // Untrusted root is OK when using our custom chain engine if the CA certificate is
                            // present in the chain policy extra store.
                            X509ChainElement root = chain.ChainElements[^1];
                            if (chain.ChainPolicy.ExtraStore.Contains(root.Certificate) &&
                                chainStatus.Exists(status => status.Status == X509ChainStatusFlags.UntrustedRoot))
                            {
                                chainStatus.Remove(
                                    chainStatus.Find(status => status.Status == X509ChainStatusFlags.UntrustedRoot));
                                errors ^= SslPolicyErrors.RemoteCertificateChainErrors;
                            }
                            else if (!chain.ChainPolicy.ExtraStore.Contains(root.Certificate) &&
                                     !chainStatus.Exists(status => status.Status == X509ChainStatusFlags.UntrustedRoot))
                            {
                                chainStatus.Add(new X509ChainStatus() { Status = X509ChainStatusFlags.UntrustedRoot });
                                errors |= SslPolicyErrors.RemoteCertificateChainErrors;
                            }
                        }

                        foreach (X509ChainStatus status in chainStatus)
                        {
                            if (status.Status != X509ChainStatusFlags.NoError)
                            {
                                errors |= SslPolicyErrors.RemoteCertificateChainErrors;
                            }
                        }
                    }
                }
                finally
                {
                    if (buildCustomChain)
                    {
                        chain!.Dispose();
                    }
                }
                return errors == 0;
            };
        }
    }
}
