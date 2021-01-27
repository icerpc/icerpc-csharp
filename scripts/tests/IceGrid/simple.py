# -*- coding: utf-8 -*-
#
# Copyright (c) ZeroC, Inc. All rights reserved.
#

def serverProps(process, current):
    return {
        "TestAdapter.Endpoints" : f"{current.config.transport} -h 127.0.0.1",
        "TestAdapter.AdapterId" : "TestAdapter"
    }

registryProps = {
    "IceGrid.Registry.DynamicRegistration" : 1
}

# Filter-out the warning about invalid lookup proxy
outfilters = [ lambda x: re.sub("-! .* warning: .*failed to lookup locator.*\n", "", x),
               lambda x: re.sub("^   .*\n", "", x) ]

if isinstance(platform, Windows) or os.getuid() != 0:
    TestSuite(__name__, [
        IceGridTestCase("without deployment", application=None,
                        icegridregistry=[IceGridRegistryMaster(props=registryProps),
                                         IceGridRegistrySlave(1, props=registryProps),
                                         IceGridRegistrySlave(2, props=registryProps)],
                        client=ClientServerTestCase(client=IceGridClient(outfilters=outfilters),
                                                    server=IceGridServer(props=serverProps))),
        IceGridTestCase("with deployment", client=IceGridClient(args=["--with-deploy"]))
    ], multihost=False)
