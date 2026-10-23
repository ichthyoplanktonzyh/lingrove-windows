using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyGraphSimulationOwnershipTests
{
    [Fact]
    public void InputAndOutputParticlesAreDetachedAcrossStepsAndIndependentSimulations()
    {
        List<VocabularyGraphPoint> positions = [new(-100, 50), new(200, -75)];
        List<VocabularyGraphPoint> velocities = [default, default];
        List<VocabularyGraphSimulation.Edge> edges = [new(0, 1)];
        var parameters = new VocabularyGraphSimulation.Parameters(.008, 320, .14, 105);
        var first = new VocabularyGraphSimulation(positions, velocities, edges, parameters);
        var second = new VocabularyGraphSimulation(positions, velocities, edges, parameters);
        var original = first.Current; var frozenPositions = original.Positions.ToArray(); var frozenVelocities = original.Velocities.ToArray();
        positions.Clear(); velocities.Clear(); edges.Clear();
        var stepped = first.Step(0, new(400, 100));
        Assert.Equal(new VocabularyGraphPoint(400, 100), stepped.Positions[0]); Assert.Equal(default, stepped.Velocities[0]);
        Assert.Equal(frozenPositions, original.Positions); Assert.Equal(frozenVelocities, original.Velocities);
        Assert.Equal(frozenPositions, second.Current.Positions); Assert.Equal(frozenVelocities, second.Current.Velocities);
        Assert.NotSame(original.Positions, first.Current.Positions);
        for (int i = 0; i < 3; i++)
        {
            var third = new VocabularyGraphSimulation(frozenPositions, frozenVelocities, [new(0, 1)], parameters);
            Assert.Equal(second.Step().Positions, third.Step().Positions);
            // Each comparison starts from the same original stream, rather than advancing second.
            second = new VocabularyGraphSimulation(frozenPositions, frozenVelocities, [new(0, 1)], parameters);
        }
    }
    [Fact]
    public void InvalidDirectParticleCallsFailBeforeMutationInsteadOfUnsafeBufferAccess()
    {
        var parameters = new VocabularyGraphSimulation.Parameters(.008, 320, .14, 105);
        Assert.Throws<ArgumentException>(() => new VocabularyGraphSimulation([], [], [], parameters));
        Assert.Throws<ArgumentException>(() => new VocabularyGraphSimulation([default], [], [], parameters));
        Assert.Throws<ArgumentException>(() => new VocabularyGraphSimulation([default], [default], [new(-1, 0)], parameters));
        var simulation = new VocabularyGraphSimulation([new(60, 0)], [default], [], parameters); var original = simulation.Current;
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(1, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Step(-1, default));
        Assert.Equal(original.Positions, simulation.Current.Positions); Assert.Equal(original.Velocities, simulation.Current.Velocities);
    }
}
