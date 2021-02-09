#
# Copyright (c) ZeroC, Inc. All rights reserved.
#

from Util import *

#
# Start 12 servers
#
servers=range(1, 13)
traceProps = {
    "Ice.Trace.Transport" : 3,
    "Ice.Trace.Protocol" : 2
}

serverProps = lambda i : {"Ice.ProgramName": "server{}".format(i)}

TestSuite(__name__, [
    ClientServerTestCase(
        client=Client(args=[i for i in servers]),
        servers=[Server(args=[i], props=serverProps(i),waitForShutdown=False, quiet=True) for i in servers],
        traceProps=traceProps)
])
