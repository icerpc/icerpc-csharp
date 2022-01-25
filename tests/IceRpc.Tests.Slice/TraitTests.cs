// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [TestFixture("icerpc")]
    public sealed class TraitTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly TraitOperationsPrx _prx;

        public TraitTests(string protocolCode)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocolCode)
                .AddTransient<IDispatcher, TraitOperations>()
                .BuildServiceProvider();
            _prx = TraitOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Trait_OperationsAsync()
        {
            var tsa = new TraitStructA("Hello");
            var tsb = new TraitStructB(42);
            var tsab = new TraitStructAB("Foo", 79);

            // Test operation with simple traits.
            Assert.That(await _prx.OpTraitAAsync(tsa), Is.EqualTo("Hello"));
            Assert.That(await _prx.OpTraitAAsync(tsab), Is.EqualTo("Foo"));
            Assert.That(await _prx.OpTraitBAsync(tsb), Is.EqualTo(42));
            Assert.That(await _prx.OpTraitBAsync(tsab), Is.EqualTo(79));

            // Test operation with optional traits.
            Assert.That(await _prx.OpOptionalTraitAsync(tsa), Is.EqualTo("Hello"));
            Assert.That(await _prx.OpOptionalTraitAsync(tsab), Is.EqualTo("Foo"));
            Assert.That(await _prx.OpOptionalTraitAsync(null), Is.Null);

            // Test operation with sequences of traits.
            Assert.That(
                await _prx.OpTraitASeqAsync(new IMyTraitA[] { tsa, tsab }),
                Is.EqualTo(new String[] { "Hello", "Foo" })
            );

            // Test operation with dictionaries with trait values.
            var traitDict = new Dictionary<byte, IMyTraitB>() { [28] = tsb, [97] = tsab };
            var resultDict = new Dictionary<byte, long>() { [28] = 42, [97] = 79 };
            Assert.That(await _prx.OpTraitBDictAsync(traitDict), Is.EqualTo(resultDict));

            // Test operation with structs containing traits.
            var nts = new NestedTraitStruct(tsa);
            var onts = new OptionalNestedTraitStruct(null, tsab);

            Assert.That(await _prx.OpNestedTraitStructAsync(nts), Is.EqualTo("Hello"));
            Assert.That(await _prx.OpOptionalNestedTraitStructAsync(onts), Is.EqualTo(((string?)null, 79)));

            Assert.That(await _prx.OpConvertToAAsync(tsb), Is.Null);
            Assert.That(await _prx.OpConvertToAAsync(tsab), Is.AssignableTo(typeof(IMyTraitA)));
        }
    }

    public class TraitOperations : Service, ITraitOperations
    {
        public ValueTask<string> OpTraitAAsync(
            IMyTraitA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1.GetString());

        public ValueTask<long> OpTraitBAsync(
            IMyTraitB p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1.GetLong());

        public ValueTask<IEnumerable<string>> OpTraitASeqAsync(
            IMyTraitA[] p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1.Select(i => i.GetString()));

        public ValueTask<IEnumerable<KeyValuePair<byte, long>>> OpTraitBDictAsync(
            Dictionary<byte, IMyTraitB> p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetLong()));

        public ValueTask<string> OpNestedTraitStructAsync(
            NestedTraitStruct p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1.T.GetString());

        public ValueTask<(string?, long?)> OpOptionalNestedTraitStructAsync(
            OptionalNestedTraitStruct p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1.T1?.GetString(), p1.T2?.GetLong()));

        public ValueTask<string?> OpOptionalTraitAsync(
            IMyTraitA? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1?.GetString());

        public ValueTask<IMyTraitA?> OpConvertToAAsync(
            IMyTraitB p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1 is IMyTraitA result) ? result : null);
    }

    public partial interface IMyTraitA
    {
        string GetString();
    }

    public partial interface IMyTraitB
    {
        long GetLong();
    }

    public partial record struct TraitStructA : IMyTraitA
    {
        public string GetString() => S;
    }

    public partial record struct TraitStructB : IMyTraitB
    {
        public long GetLong() => L;
    }

    public partial record struct TraitStructAB : IMyTraitA, IMyTraitB
    {
        public string GetString() => S;
        public long GetLong() => L;
    }
}
