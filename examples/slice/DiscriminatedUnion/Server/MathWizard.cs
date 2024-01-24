// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using TwoD;

namespace DiscriminatedUnionServer;

/// <summary>A MathWizard is an IceRPC service that implements Slice interface 'AreaCalculator'.</summary>
[SliceService]
internal partial class MathWizard : IAreaCalculatorService
{
    public ValueTask<double> ComputeAreaAsync(
        Shape shape,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Computing area for shape {shape}");

        // The Dunet-generated Match method accepts functions in declaration order.
        // See https://github.com/domn1995/dunet
        double area = shape.Match<double>(
            square => square.Side * square.Side,
            circle => Math.PI * circle.Radius * circle.Radius,
            rectangle => rectangle.Width * rectangle.Height,
            ellipse => Math.PI * ellipse.MajorAxis * ellipse.MinorAxis / 4);

        return new(area);
    }
}
