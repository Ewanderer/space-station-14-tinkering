using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Content.Shared.Kitchen.OpenKitchen.Prototypes;

/// <summary>
/// For single reagents like pancake batter, bread dough, apple juice, blood the instructions on how to turn it into a meal.
/// </summary>
[Prototype]
public sealed partial class ReagentMealPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Which reagent does this handler work for?
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = default!;
    /// <summary>
    /// What meal type should the meal node be?
    /// </summary>
    [DataField]
    public ProtoId<MealTypePrototype> MealType = default!;

    /// <summary>
    /// How much capacity does the meal node get per unit of the solution in the hull.
    /// </summary>
    [DataField]
    public FixedPoint2 CapacityFactor = 1;
    /// <summary>
    /// Modifies derived from fuzzy factors.
    /// </summary>
    [DataField]
    public List<MealFuzzyFactorPrototype> FuzzyFactors = [];

}

/// <summary>
/// Prototype for fuzzy factors to a reagenet meal
/// </summary>
[DataDefinition]
public sealed partial class MealFuzzyFactorPrototype
{
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = default!;
    /// <summary>
    /// lower boundary
    /// </summary>
    [DataField]
    public FixedPoint2 Minimum = 0;

    [DataField]
    public FixedPoint2 Maximum = 0;
    /// <summary>
    /// factor to capacity at or below minimum
    /// </summary>
    [DataField]
    public FixedPoint2? CapacityFactorAtMinimum;
    /// <summary>
    /// factor to capacity at or above maximum
    /// </summary>
    [DataField]
    public FixedPoint2? CapacityFactorAtMaximum;

    [DataField]
    public FixedPoint2? TasteFactorAtMinimum;
    [DataField]
    public FixedPoint2? TasteFactorAtMaximum;
}
