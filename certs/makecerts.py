#!/usr/bin/env python3
#
# Copyright (c) ZeroC, Inc. All rights reserved.
#

import os, sys, socket, getopt

try:
    import IceCertUtils
except Exception as ex:
    print("couldn't load IceCertUtils, did you install the 'zeroc-icecertutils'\n"
          "package from the Python package repository?\nerror: " + str(ex))
    sys.exit(1)

def usage():
    print("Usage: " + sys.argv[0] + " [options]")
    print("")
    print("Options:")
    print("-h               Show this message.")
    print("-d | --debug     Debugging output.")
    print("--ip <ip>        The IP address for the server certificate.")
    print("--dns <dns>      The DNS name for the server certificate.")
    print("--use-dns        Use the DNS name for the server certificate common")
    print("                 name (default is to use the IP address)." )
    sys.exit(1)

# Check arguments
debug = False
ip = None
dns = None
usedns = False
impl = ""
try:
    opts, args = getopt.getopt(sys.argv[1:], "hd", ["help", "debug", "ip=", "dns=","use-dns","impl="])
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
    elif o == "--ip":
        ip = a
    elif o == "--dns":
        dns = a
    elif o == "--use-dns":
        usedns = True
    elif o == "--impl":
        impl = a

def request(question, newvalue, value):
    while True:
        sys.stdout.write(question)
        sys.stdout.flush()
        input = sys.stdin.readline().strip()
        if input == 'n':
            sys.stdout.write(newvalue)
            sys.stdout.flush()
            return sys.stdin.readline().strip()
        else:
            return value

# Change to the directory where the certs files are stored
os.chdir(os.path.dirname(os.path.abspath(__file__)))

if not ip:
    ip = ["127.0.0.1", "::1"]
    ip = request("The IP addresses used for the server certificate will be: {0}\n".format(ip) +
                 "Do you want to keep this IP address? (y/n) [y]", "IP :", ip)

if not dns:
    dns = "localhost"
    dns = request("The DNS name used for the server certificate will be: " + dns + "\n"
                  "Do you want to keep this DNS name? (y/n) [y]", "DNS : ", dns)

CertificateFactory = vars(IceCertUtils)[impl + "CertificateFactory"]
factory = CertificateFactory(debug=debug, cn="Ice Tests CA")

# CA certificate
factory.getCA().save("cacert.pem").save("cacert.der")

# Client certificate
client = factory.create("client", extendedKeyUsage="clientAuth")
client.save("client.p12")

# Server certificate
server = factory.create("server", cn = (dns if usedns else "server"), ip=ip, dns=dns, extendedKeyUsage="serverAuth,clientAuth")
server.save("server.p12")

factory.destroy()
