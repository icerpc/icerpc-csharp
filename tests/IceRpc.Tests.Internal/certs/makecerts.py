#!/usr/bin/env python3
# Copyright (c) ZeroC, Inc. All rights reserved.

import os
import sys
import getopt
import IceCertUtils


def usage():
    print("Usage: " + sys.argv[0] + " [options]")
    print("")
    print("Options:")
    print("-h               Show this message.")
    print("--clean          Clean the CA database first.")
    sys.exit(1)


#
# Check arguments
#
debug = False
clean = False
force = False
try:
    opts, args = getopt.getopt(sys.argv[1:], "hd", ["help", "clean"])
except getopt.GetoptError as e:
    print("Error %s " % e)
    usage()
    sys.exit(1)

for (o, a) in opts:
    if o == "-h" or o == "--help":
        usage()
        sys.exit(0)
    elif o == "--clean":
        clean = True

home = os.path.join(os.path.dirname(os.path.abspath(__file__)), "db")
if not os.path.exists("home"):
    os.mkdir(home)

if clean:
    IceCertUtils.CertificateFactory(home=home).destroy(True)

ca = IceCertUtils.CertificateFactory(home=home, cn="ZeroC Test CA", ip="127.0.0.1", email="issuer@zeroc.com")
ca.getCA().save("cacert.der")

cert = ca.create("server", extendedKeyUsage="serverAuth", cn="Server", dns="server", ip="127.0.0.1")
cert.save("server.p12")
