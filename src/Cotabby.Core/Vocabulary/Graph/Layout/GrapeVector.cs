namespace Cotabby.Core.Vocabulary;

// Adapted from Grape 1.1.0, MIT Copyright (c) 2023 Zhen Li. See resources/ThirdParty/Grape.
/// <summary>Solver-only double vector. Explicit scalar arithmetic retains the original SIMD2
/// operation order without borrowing WPF geometry or reducing the solver to float precision.</summary>
internal readonly record struct GrapeVector(double X, double Y)
{
    public static GrapeVector operator +(GrapeVector a, GrapeVector b) => new(a.X + b.X, a.Y + b.Y);
    public static GrapeVector operator -(GrapeVector a, GrapeVector b) => new(a.X - b.X, a.Y - b.Y);
    public static GrapeVector operator *(GrapeVector a, double b) => new(a.X * b, a.Y * b);
    public static GrapeVector operator /(GrapeVector a, double b) => new(a.X / b, a.Y / b);
    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(LengthSquared);
    public VocabularyGraphPoint Point => new(X, Y);
    public static GrapeVector From(VocabularyGraphPoint p) => new(p.X, p.Y);
    public GrapeVector Jiggled(ref uint state) => new(Jiggle(X, ref state), Jiggle(Y, ref state));
    private static double Jiggle(double value, ref uint state)
    {
        if (value != 0 && !double.IsNaN(value)) return value;
        state = unchecked(1664525 * state + 1013904223);
        return (state / 4294967296d - .5) * 1e-5;
    }
}
