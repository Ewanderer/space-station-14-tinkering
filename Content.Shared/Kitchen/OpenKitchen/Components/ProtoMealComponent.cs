using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen.OpenKitchen.Components;

/// <summary>
/// This is the first clue to the shared meal system,
/// what container type to assign for this entity.
/// </summary>
[RegisterComponent] [NetworkedComponent]
public sealed partial class ProtoMealComponent : Component
{
    public enum HullSourceOption
    {
        NoHull,
        ProtoMealComponent,
        ExtractableComponent,
        SolutionComponent,
    }

    public HullSourceOption HullSource;

    [DataField]
    public ProtoId<MealTypePrototype> Prototype { get; set; }

    [DataField]
    public FixedPoint2 Capacity { get; set; }

    [DataField]
    public Solution? HullSolution { get; set; }
}
