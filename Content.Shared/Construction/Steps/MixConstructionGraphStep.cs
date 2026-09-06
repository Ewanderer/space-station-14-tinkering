using Content.Shared.Chemistry.Components;
using Content.Shared.Examine;

namespace Content.Shared.Construction.Steps;

[DataDefinition]
public sealed partial class MixConstructionGraphStep : ConstructionGraphStep
{
    /// <summary>
    /// If not null requires a mixable solution with this solution name.
    /// </summary>
    [DataField("solutionName")]
    public string SolutionName
        = string.Empty;

    public override void DoExamine(ExaminedEvent examinedEvent)
    {

    }

    public override ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry()
        {
            Localization = "construction-presenter-mix-step",
        };
    }

    public bool EntityValid(EntityUid entityUid, EntityManager entityManager, IComponentFactory factory)
    {
        if (string.IsNullOrWhiteSpace(SolutionName))
            return true;
        if (!entityManager.TryGetComponent(entityUid, out MixableSolutionComponent? solutionComponent))
            return false;
        if (solutionComponent.Solution != SolutionName)
            return false;

        return true;
    }
}
