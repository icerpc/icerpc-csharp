This directory contains certificates that are required by the tests in
`TlsConfigurationTests.cs`. The `makecerts.py` script generates
certificates in the current directory using the CA databases stored in
the `certs/db` directory.

Running this script, we'll just re-save the certificates in the
current directory without creating new certificates.
