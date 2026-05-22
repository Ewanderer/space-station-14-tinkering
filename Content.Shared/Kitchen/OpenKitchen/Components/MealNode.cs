using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Kitchen.OpenKitchen.Components;

/// <summary>
/// Container for Meal Information.
/// Meal Nodes form a tree structure, which can be evaluated against know recipe structures to know the meal.
/// They hold the data about the cooking progress. (Thermals)
/// </summary>
[DataDefinition]
[Serializable]
[NetSerializable]
public sealed partial class MealNode : ISerializationHooks, IRobustCloneable<MealNode>
{
    /// <summary>
    /// The starting type of a meal node.
    /// This is something like: RawPancake, EmptyBowl, WaterInAPot.
    /// Is used during evaluation to determine the real type.
    /// </summary>
    [DataField]
    public ProtoId<MealTypePrototype> MealType { get; set; }

    /// <summary>
    /// The meal type after evaluation.
    /// </summary>
    [DataField]
    public ProtoId<MealTypePrototype>? ActualMealType { get; set; }
    /// <summary>
    /// Quality setup from hull/input object
    /// </summary>
    [DataField]
    public FixedPoint2 BaseQuality { get; set; }

    [DataField]
    public FixedPoint2 ActualQuality { get; set; }

    /// <summary>
    /// The name of the meal.
    /// </summary>
    [DataField]
    public string? ActualName { get; set; } // TODO Replace with some localizable descriptor system.

    /// <summary>
    /// The description of a meal when examined.
    /// </summary>
    [DataField]
    public string? ActualDescription { get; set; } //TODO: Replace with some localizable descriptor system.

    /// <summary>
    /// The taste string for eating the meal.
    /// </summary>
    [DataField]
    public string? ActualTaste { get; set; } // TODO Replace with some localizable descriptor system.

    /// <summary>
    /// To convert temperature information to a degree of how cooked the meal is.
    /// </summary>
    [DataField]
    public float Cookedness { get; set; }

    /// <summary>
    /// The total volume of hull + ingredient volumes this node can hold.
    /// calculated when the meal node is setup.
    /// </summary>
    [DataField]
    public FixedPoint2 Capacity { get; set; }

    /// <summary>
    /// Sum of hull.Volume and ingredient.Volumes.
    /// updated when node has new ingredient added or hull is grown.
    /// use to hasten checks on adding ingredients to the node.
    /// </summary>
    [DataField]
    public FixedPoint2 Volume { get; set; }


    /// <summary>
    /// This is the optional solution on which this meal node was based on.
    /// It has a value unless we just have something like a bowl or plates default meal node used to store a meal portion
    /// inside.
    /// </summary>
    [DataField]
    public Solution? HullSolution { get; set; }

    /// <summary>
    /// A list of items which have been added to this meal.
    /// Used to determine the actual type of the meal.
    /// </summary>
    public List<MealNode> Ingredients { get; set; } = [];

    public MealNode Clone()
    {
        return new MealNode
        {
            Cookedness = Cookedness,
            ActualDescription = ActualDescription,
            ActualName = ActualName,
            ActualMealType = ActualMealType,
            ActualTaste = ActualTaste,
            HullSolution = HullSolution?.Clone(),
            Ingredients = Ingredients.Select(e => e.Clone()).ToList(),
            MealType = MealType,
        };
    }
}
