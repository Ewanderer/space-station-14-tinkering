using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen.OpenKitchen.Components;

/// <summary>
/// Used to store fuzzy ratios of the ingredients used to make this cooking reagent.
/// Like how much egg, flour and milk is in pancake batter.
/// </summary>
public sealed partial class FuzzyMixtureReagentData : ReagentData
{
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Mixture { get; init; } = [];

    public override bool Equals(ReagentData? other)
    {
        if (other is not FuzzyMixtureReagentData otherCookingReagentData)
            return false;
        if (otherCookingReagentData.Mixture.Count != Mixture.Count)
            return false;
        foreach (var component in Mixture)
        {
            if (!otherCookingReagentData.Mixture.TryGetValue(component.Key, out var otherVolume) ||
                otherVolume != component.Value)
                return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        return Mixture.GetHashCode();
    }

    public override ReagentData Clone()
    {
        return new FuzzyMixtureReagentData()
        {
            Mixture = new Dictionary<ProtoId<ReagentPrototype>, FixedPoint2>(Mixture),
        };
    }
}
