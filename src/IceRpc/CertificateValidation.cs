// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc
{
    /// <summary>This class allows creating certificate validation callbacks to validate client and server certificates.
    /// </summary>
    public static class CertificateValidaton
    {
        /// <summary>Returns a certificate validation callback that can be used to validate server certificates.
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

        /// <summary>Returns a certificate validation callback that can be used to validate client certificates.
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
                    // For a client connection the peer must always provide a certificate, for a server connection the
                    // certificate is only required if the RequireClientCertificate option was set.
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
                // If using custom certificate authorities or the machine context and the peer provides a
                // certificate, we rebuild the certificate chain with our custom chain policy.
                if (buildCustomChain)
                {
                    chain = new X509Chain(useMachineContext);
                    try
                    {
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.DisableCertificateDownloads = true;
                        if (certificateAuthorities != null)
                        {
                            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            chain.ChainPolicy.CustomTrustStore.Clear();
                            chain.ChainPolicy.CustomTrustStore.AddRange(certificateAuthorities);
                        }
                        return chain.Build((X509Certificate2)certificate!);
                    }
                    finally
                    {
                        chain.Dispose();
                    }
                }
                else
                {
                    return errors == 0;
                }
            };
        }
    }
}
