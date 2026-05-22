using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.OpenKitchen.EntitySystems;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Content.Shared.Kitchen.OpenKitchen.Prototypes;

/// <summary>
/// A recipe is a rule set to go from an potential meal type
/// to an actual meal type.during evaluation.
/// and assign base Quality.
/// </summary>
[Prototype]
public sealed partial class MealRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// What base type the meal node must have or be derived from
    /// </summary>
    public ProtoId<MealTypePrototype> IsMadeFrom = default!;
    /// <summary>
    /// The meal type to which this recipe evaluates.
    /// </summary>
    public ProtoId<MealTypePrototype> ResultingType = default!;

    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtFrozen = 100;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtRaw = 100;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtUndercooked = 100;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtMedium = 100;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtWellDone = 100;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtCrispy = 100;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null the recipe is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityAtBurned = 100;

    /// <summary>
    /// how must the child nodes look like to qualify the node for this recipe?
    /// </summary>
    public IngredientRequirmentPrototype[] Ingredients = [];

}

[DataDefinition]
public sealed partial class IngredientRequirmentPrototype
{
    /// <summary>
    /// A descriptive name, what part of the meal this makes.
    /// examples: sauße, garnish, main, side, cake bottom, cake layer,
    /// </summary>
    public string Name = default!;
    /// <summary>
    /// What can go into this slot
    /// </summary>
    public ProtoId<MealTypePrototype>[] PermittedTypes = [];
    /// <summary>
    /// how often a node of this type is permitted.
    /// to dynamically scale a salad.
    /// </summary>
    public int MaximumAmount = 1;
    /// <summary>
    /// Foreach child matching this requirment and within maximum ammout,
    /// add this flat bonus to the quality calculation.
    /// </summary>
    public FixedPoint2 QualityBonusForUniqueExtra = 0;

    /// <summary>
    /// If ture, the recipe withh not qualify if a child node qualifies for this requirment.
    /// </summary>
    public bool Forbidden = false;


    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtFrozen = 1;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtRaw = 1;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtUndercooked = 1;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtMedium = 1;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtWellDone = 1;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtCrispy = 1;
    /// <summary>
    /// The base value of this recipe while node is frozen.
    /// if null a child is not viable at this degree of cookedness.
    /// </summary>
    public FixedPoint2? QualityFactorAtBurned = 1;


}
