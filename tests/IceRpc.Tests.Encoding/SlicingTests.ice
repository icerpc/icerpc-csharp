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

	class MyCompactBaseClass(1)
	{
		string m1;
	}

	class MyCompactDerivedClass(2) : MyCompactBaseClass
	{
		string m2;
	}

	class MyCompactMostDerivedClass(3) : MyCompactDerivedClass
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

	[preserve-slice]
	class MyPreservedClass : MyBaseClass
	{
		string m2;
	}

	class MyPreservedDerivedClass1 : MyPreservedClass
	{
		MyBaseClass m3;
	}

	class MyPreservedDerivedClass2(56) : MyPreservedClass
	{
		MyBaseClass m3;
	}
}
