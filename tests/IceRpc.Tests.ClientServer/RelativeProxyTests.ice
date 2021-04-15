// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
	interface RelativeProxy
    {
        int op();
    }

	interface RelativeCallback
	{
		int op(RelativeProxy relative);
	}

	interface RelativeProxyOperations
	{
		RelativeProxy opRelative(RelativeCallback callback);
	}
}
