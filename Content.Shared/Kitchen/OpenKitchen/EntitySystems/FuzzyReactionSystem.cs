using System.Collections.Frozen;
using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.OpenKitchen.Components;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Kitchen.OpenKitchen.EntitySystems;

public sealed partial class FuzzyReactionSystem : EntitySystem
{
    /// <summary>
    /// Foam reaction protoId.
    /// </summary>
    public static readonly ProtoId<ReactionPrototype> FoamReaction = "Foam";

    /// <summary>
    ///     The maximum number of reactions that may occur when a solution is changed.
    /// </summary>
    private const int MaxReactionIterations = 20;

    [Dependency] private INetManager _netMan = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedEntityEffectsSystem _entityEffects = default!;

    /// <summary>
    /// A cache of all reactions indexed by at most ONE of their required reactants.
    /// I.e., even if a reaction has more than one reagent, it will only ever appear once in this dictionary.
    /// </summary>
    private FrozenDictionary<string, List<FuzzyReactionPrototype>> _reactionsSingle = default!;

    /// <summary>
    ///     A cache of all reactions indexed by one of their required reactants.
    /// </summary>
    private FrozenDictionary<string, List<FuzzyReactionPrototype>> _reactions = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeReactionCache();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    /// <summary>
    ///     Handles building the reaction cache.
    /// </summary>
    private void InitializeReactionCache()
    {
        // Construct single-reaction dictionary.
        var dict = new Dictionary<string, List<FuzzyReactionPrototype>>();
        foreach (var reaction in _prototypeManager.EnumeratePrototypes<FuzzyReactionPrototype>())
        {
            // For this dictionary we only need to cache based on the first reagent.
            var reagent = reaction.Reactants.Keys.First();
            var list = dict.GetOrNew(reagent);
            list.Add(reaction);
        }

        _reactionsSingle = dict.ToFrozenDictionary();

        dict.Clear();
        foreach (var reaction in _prototypeManager.EnumeratePrototypes<FuzzyReactionPrototype>())
        {
            foreach (var reagent in reaction.Reactants.Keys)
            {
                var list = dict.GetOrNew(reagent);
                list.Add(reaction);
            }
        }

        _reactions = dict.ToFrozenDictionary();
    }

    /// <summary>
    ///     Updates the reaction cache when the prototypes are reloaded.
    /// </summary>
    /// <param name="eventArgs">The set of modified prototypes.</param>
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs eventArgs)
    {
        if (eventArgs.WasModified<FuzzyReactionPrototype>())
            InitializeReactionCache();
    }

    /// <summary>
    /// Checks if a solution can undergo a specified reaction. (in theory)
    /// </summary>
    /// <param name="solutionEntity">The solution to check.</param>
    /// <param name="reaction">The reaction to check.</param>
    /// <param name="mixerComponent">The mixing component used for this reaction</param>
    /// <param name="actualSolution"></param>
    /// <returns></returns>
    private bool CanReactPrecheck(Entity<SolutionComponent> solutionEntity,
        FuzzyReactionPrototype reaction,
        ReactionMixerComponent? mixerComponent,
        Solution actualSolution)
    {
        var solution = actualSolution;
        //check temperature limit
        if (solution.Temperature < reaction.MinimumTemperature)
        {
            return false;
        }

        if (solution.Temperature > reaction.MaximumTemperature)
        {
            return false;
        }

        //check if the right mixing category is used
        if ((mixerComponent == null && reaction.MixingCategories != null) ||
            mixerComponent != null && reaction.MixingCategories != null &&
            reaction.MixingCategories.Except(mixerComponent.ReactionTypes).Any())
        {
            return false;
        }

        //check for cancellation
        var attempt = new FuzzyReactionAttemptEvent(reaction, solutionEntity);
        RaiseLocalEvent(solutionEntity, ref attempt);
        if (attempt.Cancelled)
        {
            return false;
        }

        // are all reactants in solution?
        foreach (var reactant in reaction.Reactants)
        {
            var reactantQuantity = solution.GetTotalPrototypeQuantity(reactant.Key);
            if (reactantQuantity <= FixedPoint2.Zero)
                return false;
            // catalyst is not consumed, so will not limit the reaction. But it still needs to be present, and
            // for quantized reactions we need to have a minimum amount
            if (reactant.Value.Catalyst && reaction.Quantized && reactantQuantity < reactant.Value.Amount)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Perform a reaction on a solution. This assumes all reaction criteria are met.
    ///     Removes the reactants from the solution, adds products, and returns a list of products.
    /// </summary>
    private ProtoId<ReagentPrototype>? PerformReaction(Entity<SolutionComponent> soln,
        FuzzyReactionPrototype reaction,
        FixedPoint2 unitReactions,
        FixedPoint2[] deviations)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;

        var energy = reaction.ConserveEnergy ? solution.GetThermalEnergy(_prototypeManager) : 0;
        //store the deviations inside the mixture (thus we can later perfectly reconstruct the underlying solution)
        var reagentData = new FuzzyMixtureReagentData();
        //Remove reactants
        var reactants = reaction.Reactants.ToArray();
        for (var i = 0; i < reaction.Reactants.Count; i++)
        {
            var reactant = reactants[i];
            if (!reactant.Value.Catalyst)
            {
                var amountToRemove = unitReactions * (reactant.Value.Amount + deviations[i]);
                reagentData.Mixture.Add(reactant.Key, deviations[i]);
                solution.RemoveReagent(reactant.Key, amountToRemove, ignoreReagentData: true);
            }
        }

        foreach (var reactant in reaction.Reactants)
        {
            if (!reactant.Value.Catalyst)
            {
                var amountToRemove = unitReactions * reactant.Value.Amount;
                solution.RemoveReagent(reactant.Key, amountToRemove, ignoreReagentData: true);
            }
        }

        if (reaction.Product != null)
        {
            //compile do id

           //var id = new ReagentId(reaction.Product, [reagentData]); // I dare you to uncomment this. Assembly Checker does not accept it!
            var id = new ReagentId(reaction.Product, new());
            id.Data!.Add(reagentData);
            //Create product
            solution.AddReagent(id, reaction.ProductAmount * unitReactions);
            if (reaction.ConserveEnergy)
            {
                var newCap = solution.GetHeatCapacity(_prototypeManager);
                if (newCap > 0)
                    solution.Temperature = energy / newCap;
            }
        }

        OnReaction(soln, reaction, null, unitReactions);

        return reaction.Product;
    }

    private void OnReaction(Entity<SolutionComponent> soln,
        FuzzyReactionPrototype reaction,
        ReagentPrototype? reagent,
        FixedPoint2 unitReactions)
    {
        var posFound = _transformSystem.TryGetMapOrGridCoordinates(soln, out var gridPos);

        _adminLogger.Add(LogType.ChemicalReaction,
            reaction.Impact,
            $"Chemical reaction {reaction.ID:reaction} occurred with strength {unitReactions:strength} on entity {ToPrettyString(soln):metabolizer} at Pos:{(posFound ? $"{gridPos:coordinates}" : "[Grid or Map not Found]")}");

        _entityEffects.ApplyEffects(soln, reaction.Effects, unitReactions);

        // Someday, some brave soul will thread through an optional actor
        // argument in from every call of OnReaction up, all just to pass
        // it to PlayPredicted. I am not that brave soul.
        if (_netMan.IsServer)
            _audio.PlayPvs(reaction.Sound, soln);
    }

    /// <summary>
    ///     Performs all chemical reactions that can be run on a solution.
    ///     Removes the reactants from the solution, then returns a solution with all products.
    ///     WARNING: Does not trigger reactions between solution and new products.
    /// </summary>
    private bool ProcessReactions(Entity<SolutionComponent> soln,
        SortedSet<FuzzyReactionPrototype> reactions,
        ReactionMixerComponent? mixerComponent)
    {
        //track if something was maded.
        ProtoId<ReagentPrototype>? product = null;
        var changed = FixedPoint2.Zero;
        // attempt to perform any applicable reaction
        foreach (var reaction in reactions)
        {
            var actualSolution = soln.Comp.Solution;

            //resolve any existing amount of the reaction product to its base components (in an attempt to grow the fuzzy mixture, either with other raw material or even other fuzzy mixtures of the same solution)
            if (reaction.Product != null)
            {
                actualSolution = actualSolution.Clone();
                foreach (var splitTarget in actualSolution.Contents
                             .Where(e => e.Reagent.Prototype == reaction.Product.Value)
                             .ToArray())
                {
                    var mixtureData =
                        splitTarget.Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData) as
                            FuzzyMixtureReagentData;
                    //record how much we split apart
                    changed += splitTarget.Quantity;
                    //update solution
                    actualSolution.Contents.Remove(splitTarget);
                    //reconstruct components of the solution.
                    foreach (var reactant in reaction.Reactants)
                    {
                        //get deviation
                        if (mixtureData == null || mixtureData.Mixture.TryGetValue(reactant.Key, out var deviation))
                            deviation = FixedPoint2.Zero;
                        actualSolution.AddReagent(reactant.Key,
                            (reactant.Value.Amount + deviation) * splitTarget.Quantity);
                    }
                }
            }

            //check basic conditions.
            if (!CanReactPrecheck(soln, reaction, mixerComponent, actualSolution))
            {
                continue;
            }

            //calculate ratios.
            var amount = FindBestMatch(actualSolution, reaction, out var ratios);
            if (amount == null)
                continue;
            //replace components solution with the actual solution, so that perform reaction works properly.
            soln.Comp.Solution = actualSolution;
            //check the amount of output from the reaction to see, if we actually expanded an existing solution or just recombination of self
            changed -= amount.Value*reaction.ProductAmount;
            product = PerformReaction(soln, reaction, amount.Value, ratios);
            break;
        }

        // did any actual reaction (with change to product ratios) occur?
        if (product == null || changed <= 0)
            return false;


        // Add any reactions associated with the new products. This may re-add reactions that were already iterated
        // over previously. The new product may mean the reactions are applicable again and need to be processed.
        if (_reactions.TryGetValue(product, out var reactantReactions))
            reactions.UnionWith(reactantReactions);
        return true;
    }

    /// <summary>
    /// Tries to match a solution best to a fuzziness reaction.
    /// Given n∈N, n>0: Number of Reactants
    /// Q1,...,Qn: Quantities of Reactant available in solution
    /// A1,...,An: Ideal Amount of reactant for the reaction at t=1.
    /// B_low_1,...,B_low_n: Lower Bound for devation of rectant n.
    /// B_high_1,...,B_high_n: High Bound for devation of rectant n.
    /// t∈R>0: Reaction Quantity.
    /// maxDeviation: Total maximum allowed deviation within reaction.
    /// d1,...,dn: Deviations of reactant i from ideal
    /// we try to satisfy the conditions for all i 1≤i≤n:
    /// t * (Ai*di) ≤ Qi
    /// B_low_i ≤ di ≤ B_high_i
    /// ∑i=1 n |di-Ai| ≤ maxDeviation
    /// while maximising t by adjusting di
    /// </summary>
    /// <param name="soln"></param>
    /// <param name="reaction"></param>
    /// <param name="deviations">the di values</param>
    /// <returns>The total reaction amount, aka factor by which to execute (Ai*di)</returns>
    private FixedPoint2? FindBestMatch(Solution soln,
        FuzzyReactionPrototype reaction,
        out FixedPoint2[] deviations)
    {
        //setup with 0 deviation
        deviations = reaction.Reactants.Select(e => FixedPoint2.Zero).ToArray();
        //extract variables
        var quantity = reaction.Reactants.Keys.Select(e => soln.GetTotalPrototypeQuantity(e)).ToArray();
        var deviationLow = reaction.Reactants.Values.Select(e => e.MinAmount.HasValue ? (e.Amount - e.MinAmount) : 0)
            .Select(e => e!.Value)
            .ToArray();
        var amount = reaction.Reactants.Values.Select(e => e.Amount).ToArray();
        var deviationHigh = reaction.Reactants.Values.Select(e => e.MaxAmount.HasValue ? (e.MaxAmount - e.Amount) : 0)
            .Select(e => e!.Value)
            .ToArray();
        var maxDeviation = reaction.MaxDeviation;
        var catalyst = reaction.Reactants.Values.Select(e => e.Catalyst).ToArray();
        //get maximum at idea value (deviations are all 0)
        var tMax = FixedPoint2.MaxValue;
        CalculateTMax(reaction, deviations, catalyst, quantity, amount, ref tMax);
        //round if quantized
        if (reaction.Quantized)
            tMax = (int)tMax;
        if (tMax == FixedPoint2.Zero)
        {
            //if failed -> try at minimum
            deviations = deviationLow.ToArray();
            CalculateTMax(reaction, deviations, catalyst, quantity, amount, ref tMax);
            //round if quantized
            if (reaction.Quantized)
                tMax = (int)tMax;
            //still no good reaction not good.
            if (tMax == FixedPoint2.Zero)
                return null;
        }
        //if succeeded feed any available reminder into deviation while within the limits

        //iteratively approach a good solution. a solution is stable when no more deviation changes are possible.
        var divider =
            reaction.Reactants.Count; //slowly increase max step size as we eliminate potential deviation candidates.
        while (divider > 0)
        {
            var totalAbsoluteDeviation = FixedPoint2.FromCents(deviations.Sum(e => Math.Abs(e.Value)));
            for (var i = 0; i < reaction.Reactants.Count; i++)
            {
                //calculate the remainder
                var remainderToShift = (quantity[i] - (deviations[i] + amount[i]) * tMax) / tMax;
                //skip over if no remainder
                if (remainderToShift == FixedPoint2.Zero)
                {
                    divider--;
                    continue;
                }

                //allow shifting up
                if (reaction.MaxDeviation.HasValue)
                {
                    //we can in one iteration only move a little.
                    var allowedShift = totalAbsoluteDeviation / divider;
                    if (allowedShift == FixedPoint2.Zero)
                    {
                        divider--;
                        continue;
                    }

                    if (totalAbsoluteDeviation > reaction.MaxDeviation.Value && deviations[i] < 0)
                    {
                        //we need! to absorb remainder until deviation is 0
                        deviations[i] = FixedPoint2.Min(deviations[i] + allowedShift, 0);
                    }
                    else if (totalAbsoluteDeviation < reaction.MaxDeviation.Value && deviations[i] < maxDeviation)
                    {
                        //we can absorb reminder up to maximum deviation.
                        allowedShift = FixedPoint2.Min(allowedShift,
                            reaction.MaxDeviation.Value - totalAbsoluteDeviation);
                        //of course only to local limit
                        deviations[i] = FixedPoint2.Min(deviations[i] + allowedShift, deviationHigh[i]);
                    }

                    if (deviations[i] == maxDeviation)
                    {
                        divider--;
                    }
                }
                else
                {
                    //move to maximum deviation
                    deviations[i] = FixedPoint2.Min(deviations[i] + remainderToShift, deviationHigh[i]);
                    divider--;
                }
            }
        }

        {
            //validate solution of t + deviations just in case
            var totalAbsoluteDeviation = FixedPoint2.Zero;
            for (var i = 0; i < reaction.Reactants.Count; i++)
            {
                //check bounds of deviation
                if (deviations[i] < deviationLow[i] || deviationHigh[i] < deviations[i])
                    return null;
                //check limit of q.
                if (tMax * (amount[i] + deviations[i]) > quantity[i])
                    return null;
                //sum deviations together
                totalAbsoluteDeviation += FixedPoint2.Abs(deviations[i]);
            }

            //validate deviation does not exceed maximum
            if (maxDeviation.HasValue && totalAbsoluteDeviation > maxDeviation)
                return null;
        }
        return tMax;
    }

    private static void CalculateTMax(FuzzyReactionPrototype reaction,
        FixedPoint2[] deviations,
        bool[] catalyst,
        FixedPoint2[] quantity,
        FixedPoint2[] amount,
        ref FixedPoint2 tMax)
    {
        for (var i = 0; i < reaction.Reactants.Count; i++)
        {
            if (catalyst[i])
                continue;
            var unitReactions = quantity[i] / (amount[i] + deviations[i]);
            if (unitReactions < tMax)
            {
                tMax = unitReactions;
            }
        }
    }

    /// <summary>
    ///     Continually react a solution until no more reactions occur, with a volume constraint.
    /// </summary>
    public void FullyReactSolution(Entity<SolutionComponent> soln, ReactionMixerComponent? mixerComponent = null)
    {
        // construct the initial set of reactions to check.
        SortedSet<FuzzyReactionPrototype> reactions = new();
        foreach (var reactant in soln.Comp.Solution.Contents)
        {
            if (_reactionsSingle.TryGetValue(reactant.Reagent.Prototype, out var reactantReactions))
                reactions.UnionWith(reactantReactions);
        }

        // Repeatedly attempt to perform reactions, ending when there are no more applicable reactions, or when we
        // exceed the iteration limit.
        for (var i = 0; i < MaxReactionIterations; i++)
        {
            if (!ProcessReactions(soln, reactions, mixerComponent))
                return;
        }

        Log.Error($"{nameof(Solution)} {soln.Owner} could not finish reacting in under {MaxReactionIterations} loops.");
    }
}

/// <summary>
///     Raised directed at the owner of a solution to determine whether the reaction should be allowed to occur.
/// </summary>
/// <reamrks>
///     Some solution containers (e.g., bloodstream, smoke, foam) use this to block certain reactions from occurring.
/// </reamrks>
[ByRefEvent]
public record struct FuzzyReactionAttemptEvent(FuzzyReactionPrototype Reaction, Entity<SolutionComponent> Solution)
{
    public readonly FuzzyReactionPrototype Reaction = Reaction;
    public readonly Entity<SolutionComponent> Solution = Solution;
    public bool Cancelled = false;
}
