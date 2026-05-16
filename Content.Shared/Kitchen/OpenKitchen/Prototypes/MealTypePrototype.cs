using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen.OpenKitchen.Prototypes;

/// <summary>
/// Meal Types are used to describe a meal node.
/// mostly used for recipes to sort themselves out.
/// </summary>
[Prototype]
public sealed partial class MealTypePrototype : IPrototype
{
    /// <summary>
    /// Effects to be triggered when the meal is eaten.
    /// </summary>
    [DataField("effects")]
    public EntityEffect[] ConsumptionEffects = [];

    /// <summary>
    /// How is this meal related to other meals?
    /// A salami pizza is, at its core still a pizza.
    /// A apple is a fruit. So a recipe calling for any fruit would accept an apple.
    /// </summary>
    [DataField("parents")]
    public ProtoId<MealTypePrototype>[]? ParentMealTypes;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;


    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;
}
