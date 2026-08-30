using NexusOptimizer.Core.Health;

namespace NexusOptimizer.App.Services;

/// <summary>Condivide l'ultima lettura diagnostica con la dashboard senza rieseguire scansioni.</summary>
public sealed class HealthAssessmentCache
{
    public HealthAssessment? Current { get; private set; }

    public event Action<HealthAssessment>? Updated;

    public void Publish(HealthAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        Current = assessment;
        Updated?.Invoke(assessment);
    }
}
