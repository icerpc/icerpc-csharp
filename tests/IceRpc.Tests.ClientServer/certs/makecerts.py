#!/usr/bin/env python3
# Copyright (c) ZeroC, Inc. All rights reserved.

import os, sys, socket, getopt, IceCertUtils

if not IceCertUtils.CertificateUtils.opensslSupport:
    print("openssl is required to generate the test certificates")
    sys.exit(1)

def usage():
    print("Usage: " + sys.argv[0] + " [options]")
    print("")
    print("Options:")
    print("-h               Show this message.")
    print("-d | --debug     Debugging output.")
    print("--clean          Clean the CA database first.")
    print("--force          Re-save all the files even if they already exists.")
    sys.exit(1)

#
# Check arguments
#
debug = False
clean = False
force = False
try:
    opts, args = getopt.getopt(sys.argv[1:], "hd", ["help", "debug", "clean", "force"])
except getopt.GetoptError as e:
    print("Error %s " % e)
    usage()
    sys.exit(1)

for (o, a) in opts:
    if o == "-h" or o == "--help":
        usage()
        sys.exit(0)
    elif o == "-d" or o == "--debug":
        debug = True
    elif o == "--clean":
        clean = True
    elif o == "--force":
        force = True

home = os.path.join(os.path.dirname(os.path.abspath(__file__)), "db")
homeca1 = os.path.join(home, "ca1")
homeca2 = os.path.join(home, "ca2")
if not os.path.exists("db"):
    os.mkdir(home)
    os.mkdir(homeca1)
    os.mkdir(homeca2)

if clean:
    for h in [homeca1, homeca2]:
        IceCertUtils.CertificateFactory(home=h).destroy(True)

# Create 2 CAs, ca2 is also used as a server certificate. The serverAuth extension is required on some OSs (like macOS)
ca1 = IceCertUtils.CertificateFactory(home=homeca1, cn="ZeroC Test CA 1", ip=["127.0.0.1", "::1"], email="issuer@zeroc.com")
ca2 = IceCertUtils.CertificateFactory(home=homeca2, cn="ZeroC Test CA 2", ip=["127.0.0.1", "::1"], email="issuer@zeroc.com",
                                      extendedKeyUsage="serverAuth")

# Export CA certificates
if force or not os.path.exists("cacert1.der"): ca1.getCA().save("cacert1.der")
if force or not os.path.exists("cacert2.der"): ca2.getCA().save("cacert2.der")


# Also export the ca2 self-signed certificate, it's used by the tests to test self-signed certificates
if force or not os.path.exists("cacert2.p12"): ca2.getCA().save("cacert2.p12", addkey=True)


# Create certificates (CA, alias, { creation parameters passed to ca.create(...) })
certs = [
    (ca1, "s_rsa_ca1",       { "cn": "Server", "ip": ["127.0.0.1", "::1"], "dns": "server", "serial": 1 }),
    (ca1, "c_rsa_ca1",       { "cn": "Client", "ip": ["127.0.0.1", "::1"], "dns": "client", "serial": 2 }),
    (ca1, "s_rsa_ca1_exp",   { "cn": "Server", "validity": -1 }), # Expired certificate
    (ca1, "c_rsa_ca1_exp",   { "cn": "Client", "validity": -1 }), # Expired certificate

    (ca1, "s_rsa_ca1_cn1",   { "cn": "Server", "dns": "localhost" }),       # DNS subjectAltName localhost
    (ca1, "s_rsa_ca1_cn2",   { "cn": "Server", "dns": "localhostXX" }),     # DNS subjectAltName localhostXX
    (ca1, "s_rsa_ca1_cn3",   { "cn": "localhost" }),                        # No subjectAltName, CN=localhost
    (ca1, "s_rsa_ca1_cn4",   { "cn": "localhostXX" }),                      # No subjectAltName, CN=localhostXX
    (ca1, "s_rsa_ca1_cn5",   { "cn": "localhost", "dns": "localhostXX" }),      # DNS subjectAltName localhostXX, CN=localhost
    (ca1, "s_rsa_ca1_cn6",   { "cn": "Server", "ip": ["127.0.0.1", "::1"] }),   # IP subjectAltName 127.0.0.1
    (ca1, "s_rsa_ca1_cn7",   { "cn": "Server", "ip": "127.0.0.2" }),        # IP subjectAltName 127.0.0.2
    (ca1, "s_rsa_ca1_cn8",   { "cn": "127.0.0.1" }),                        # No subjectAltName, CN=127.0.0.1

    (ca2, "s_rsa_ca2",       { "cn": "Server", "ip": ["127.0.0.1", "::1"], "dns": "server" }),
    (ca2, "c_rsa_ca2",       { "cn": "Client", "ip": ["127.0.0.1", "::1"], "dns": "client" }),
]

#
# Create the certificates
#
for (ca, alias, args) in certs:
    if not ca.get(alias):
        ca.create(alias, extendedKeyUsage="clientAuth" if alias.startswith("c_") else "serverAuth", **args)

savecerts = [
    (ca1, "s_rsa_ca1",     None,              {}),
    (ca1, "c_rsa_ca1",     None,              {}),
    (ca1, "s_rsa_ca1_exp", None,              {}),
    (ca1, "c_rsa_ca1_exp", None,              {}),
    (ca1, "s_rsa_ca1_cn1", None,              {}),
    (ca1, "s_rsa_ca1_cn2", None,              {}),
    (ca1, "s_rsa_ca1_cn3", None,              {}),
    (ca1, "s_rsa_ca1_cn4", None,              {}),
    (ca1, "s_rsa_ca1_cn5", None,              {}),
    (ca1, "s_rsa_ca1_cn6", None,              {}),
    (ca1, "s_rsa_ca1_cn7", None,              {}),
    (ca1, "s_rsa_ca1_cn8", None,              {}),
    (ca2, "s_rsa_ca2",     None,              {}),
    (ca2, "c_rsa_ca2",     None,              {}),
]

# Save the certificates in PKCS12 format.
for (ca, alias, path, args) in savecerts:
    if not path: path = alias
    password = args.get("password", None)
    cert = ca.get(alias)
    if force or not os.path.exists(path + ".p12"):
        cert.save(path + ".p12", **args)
