// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using TwoD;

namespace DiscriminatedUnionServer;

/// <summary>A MathWizard is an IceRPC service that implements Slice interface 'AreaCalculator'.</summary>
[Service]
internal partial class MathWizard : IAreaCalculatorService
{
    public ValueTask<double> ComputeAreaAsync(
        Shape shape,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Computing area for shape {shape}");

        // Shape is a checked enum: the decoded shape is always one of these four variants.
        double area = shape switch
        {
            Shape.Square(var side) => side * side,
            Shape.Circle(var radius) => Math.PI * radius * radius,
            Shape.Rectangle(var width, var height) => width * height,
            Shape.Ellipse(var majorAxis, var minorAxis) => Math.PI * majorAxis * minorAxis / 4,
        };

        return new(area);
    }
}
