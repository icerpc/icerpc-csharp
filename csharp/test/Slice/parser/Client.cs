// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

// TODO: disabled BOM testing as mcpp on Ubuntu does not have the required patch
// var s = new Bom.Bar(); // just to make sure we actually compile the Slice files.
// s.X = 5;

Test.IAPrx? aPrx = null;
Test.IBPrx? bPrx = null;

_ = aPrx?.ShutdownAsync();
_ = bPrx?.ShutdownAsync();

Console.WriteLine("testing circular Slice file includes... ok");

Test.Foo.Bar.ICPrx? cPrx = null;
Test.X.Y.Z.IDPrx? dPrx = null;
Test.X.IEPrx? ePrx = null;

_ = cPrx?.ShutdownAsync();
_ = dPrx?.ShutdownAsync();
_ = ePrx?.ShutdownAsync();

Console.WriteLine("testing nested scope resolution... ok");
return 0;
