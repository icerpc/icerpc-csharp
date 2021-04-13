// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Encoding
{
	class MyBaseClass
	{
		string m1;
	}

	class MyDerivedClass : MyBaseClass
	{
		string m2;
	}

	class MyMostDerivedClass : MyDerivedClass
	{
		string m3;
	}

	exception MyBaseException
	{
		string m1;
	}

	exception MyDerivedException : MyBaseException
	{
		string m2;
	}

	exception MyMostDerivedException : MyDerivedException
	{
		string m3;
	}
}
