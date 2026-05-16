using Robust.Shared.GameStates;

namespace Content.Shared.Kitchen.OpenKitchen.Components;

[RegisterComponent] [NetworkedComponent]
public sealed partial class MealTreeContainerComponent : Component
{
    /// <summary>
    /// The Root of the MealNode Tree contained here.
    /// </summary>
    [DataField]
    public MealNode? MealTree;
}
