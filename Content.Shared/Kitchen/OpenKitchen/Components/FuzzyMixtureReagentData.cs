using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Kitchen.OpenKitchen.Components;

/// <summary>
/// Used to store fuzzy ratios of the ingredients used to make this cooking reagent.
/// Like how much egg, flour and milk is in pancake batter.
/// </summary>
[ImplicitDataDefinitionForInheritors]
[Serializable]
[NetSerializable]
public sealed partial class FuzzyMixtureReagentData : ReagentData
{
    /// <summary>
    /// the ratios used to construct the mixture.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Mixture = [];

    /// <summary>
    /// because of rounding errors, the original reaction ammount must be stored.
    /// </summary>
    [DataField]
    public FixedPoint2 ReactionAmount = FixedPoint2.Zero;

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
        return new FuzzyMixtureReagentData
        {
            Mixture = new Dictionary<ProtoId<ReagentPrototype>, FixedPoint2>(Mixture),
            ReactionAmount = ReactionAmount,
        };
    }
}
