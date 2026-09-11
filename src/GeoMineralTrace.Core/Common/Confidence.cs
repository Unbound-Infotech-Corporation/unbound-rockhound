namespace GeoMineralTrace.Core.Common;

/// <summary>
/// Bounded confidence in [0, 1]. Explicitly represents uncertainty rather than
/// implying certainty. Values near 0.5 are weakly informative.
/// </summary>
public readonly record struct Confidence
{
    public double Value { get; }

    public Confidence(double value) => Value = Math.Clamp(value, 0.0, 1.0);

    public static Confidence None => new(0);
    public static Confidence Low => new(0.25);
    public static Confidence Medium => new(0.5);
    public static Confidence High => new(0.75);
    public static Confidence VeryHigh => new(0.9);

    public static Confidence From(double value) => new(value);

    public Confidence CombineIndependent(Confidence other) =>
        From(1.0 - (1.0 - Value) * (1.0 - other.Value));

    public override string ToString() => $"{Value:P0}";
}
