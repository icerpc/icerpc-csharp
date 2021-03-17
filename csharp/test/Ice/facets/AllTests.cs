// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using IceRpc.Test;
using System;
using System.IO;
using System.Threading.Tasks;

namespace IceRpc.Test.Facets
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;

            TextWriter output = helper.Output;
            output.Write("testing Ice.Admin.Facets property... ");
            TestHelper.Assert(communicator.GetPropertyAsList("Ice.Admin.Facets") == null);
            communicator.SetProperty("Ice.Admin.Facets", "foobar");
            string[]? facetFilter = communicator.GetPropertyAsList("Ice.Admin.Facets");
            TestHelper.Assert(facetFilter != null && facetFilter.Length == 1 && facetFilter[0].Equals("foobar"));
            communicator.SetProperty("Ice.Admin.Facets", "foo\\'bar");
            facetFilter = communicator.GetPropertyAsList("Ice.Admin.Facets");
            TestHelper.Assert(facetFilter != null && facetFilter.Length == 1 && facetFilter[0].Equals("foo'bar"));
            communicator.SetProperty("Ice.Admin.Facets", "'foo bar' toto 'titi'");
            facetFilter = communicator.GetPropertyAsList("Ice.Admin.Facets");
            TestHelper.Assert(facetFilter != null && facetFilter.Length == 3 && facetFilter[0].Equals("foo bar") &&
                    facetFilter[1].Equals("toto") && facetFilter[2].Equals("titi"));
            communicator.SetProperty("Ice.Admin.Facets", "'foo bar\\' toto' 'titi'");
            facetFilter = communicator.GetPropertyAsList("Ice.Admin.Facets");
            TestHelper.Assert(facetFilter != null && facetFilter.Length == 2 && facetFilter[0].Equals("foo bar' toto") &&
                    facetFilter[1].Equals("titi"));
            // communicator.SetProperty("Ice.Admin.Facets", "'foo bar' 'toto titi");
            // facetFilter = communicator.Properties.getPropertyAsList("Ice.Admin.Facets");
            // TestHelper.Assert(facetFilter.Length == 0);
            communicator.SetProperty("Ice.Admin.Facets", "");
            output.WriteLine("ok");

            output.Write("testing facet registration exceptions... ");
            var server = new Server(communicator);

            var obj = new Empty();

            server.Add("d", obj);
            server.Add("d#facetABCD", obj);
            try
            {
                server.Add("d#facetABCD", obj);
                TestHelper.Assert(false);
            }
            catch (ArgumentException)
            {
            }
            server.Remove("d#facetABCD");
            server.Remove("d#facetABCD"); // multiple Remove are fine as of Ice 4.0
            output.WriteLine("ok");

            await server.DisposeAsync();

            var prx = IServicePrx.Parse(helper.GetTestProxy("d", 0), communicator);
            IDPrx? d;
            IDPrx? df2;
            IDPrx? df3;

            output.Write("testing unchecked cast... ");
            output.Flush();
            d = IDPrx.Factory.Copy(prx);
            TestHelper.Assert(d != null);
            TestHelper.Assert(d.GetFacet().Length == 0);
            IDPrx df = prx.WithFacet("facetABCD", IDPrx.Factory);
            TestHelper.Assert(df.GetFacet() == "facetABCD");
            df2 = IDPrx.Factory.Copy(df);
            TestHelper.Assert(df2 != null);
            TestHelper.Assert(df2.GetFacet() == "facetABCD");
            df3 = df.WithFacet("", IDPrx.Factory);
            TestHelper.Assert(df3 != null);
            TestHelper.Assert(df3.GetFacet().Length == 0);
            output.WriteLine("ok");

            output.Write("testing checked cast... ");
            output.Flush();
            d = await IDPrx.Factory.CheckedCastAsync(prx);
            TestHelper.Assert(d != null);
            TestHelper.Assert(d.GetFacet().Length == 0);
            df = prx.WithFacet("facetABCD", IDPrx.Factory);
            TestHelper.Assert(df.GetFacet() == "facetABCD");
            df2 = IDPrx.Factory.Copy(df);
            TestHelper.Assert(df2 != null);
            TestHelper.Assert(df2.GetFacet() == "facetABCD");
            df3 = df.WithFacet("", IDPrx.Factory);
            TestHelper.Assert(df3.GetFacet().Length == 0);
            output.WriteLine("ok");

            output.Write("testing non-facets A, B, C, and D... ");
            output.Flush();
            d = IDPrx.Factory.Copy(prx);
            TestHelper.Assert(d != null);
            TestHelper.Assert(d.Equals(prx));
            TestHelper.Assert(d.CallA().Equals("A"));
            TestHelper.Assert(d.CallB().Equals("B"));
            TestHelper.Assert(d.CallC().Equals("C"));
            TestHelper.Assert(d.CallD().Equals("D"));
            output.WriteLine("ok");

            output.Write("testing facets A, B, C, and D... ");
            output.Flush();
            df = d.WithFacet("facetABCD", IDPrx.Factory);
            TestHelper.Assert(df != null);
            TestHelper.Assert(df.CallA().Equals("A"));
            TestHelper.Assert(df.CallB().Equals("B"));
            TestHelper.Assert(df.CallC().Equals("C"));
            TestHelper.Assert(df.CallD().Equals("D"));
            output.WriteLine("ok");

            output.Write("testing facets E and F... ");
            output.Flush();
            IFPrx ff = d.WithFacet("facetEF", IFPrx.Factory);
            TestHelper.Assert(ff.CallE().Equals("E"));
            TestHelper.Assert(ff.CallF().Equals("F"));
            output.WriteLine("ok");

            output.Write("testing facet G... ");
            output.Flush();
            IGPrx gf = ff.WithFacet("facetGH", IGPrx.Factory);
            TestHelper.Assert(gf.CallG().Equals("G"));
            output.WriteLine("ok");

            output.Write("testing whether casting preserves the facet... ");
            output.Flush();
            var hf = IHPrx.Factory.Copy(gf);
            TestHelper.Assert(hf != null);
            TestHelper.Assert(hf.CallG().Equals("G"));
            TestHelper.Assert(hf.CallH().Equals("H"));
            output.WriteLine("ok");

            await gf.ShutdownAsync();
        }
    }
}
