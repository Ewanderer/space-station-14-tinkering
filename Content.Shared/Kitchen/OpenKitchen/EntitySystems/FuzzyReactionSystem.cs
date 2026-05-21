using System.Collections.Frozen;
using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Armor;
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
    /// The maximum number of reactions that may occur when a solution is changed.
    /// </summary>
    private const int MaxReactionIterations = 20;

    /// <summary>
    /// Foam reaction protoId.
    /// </summary>
    public static readonly ProtoId<ReactionPrototype> FoamReaction = "Foam";

    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedEntityEffectsSystem _entityEffects = default!;

    [Dependency] private INetManager _netMan = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedMealSystem _sharedMealSystem = default!;

    /// <summary>
    /// A cache of all reactions indexed by one of their required reactants.
    /// </summary>
    private FrozenDictionary<string, List<FuzzyReactionPrototype>> _reactions = default!;

    /// <summary>
    /// A cache of all reactions indexed by at most ONE of their required reactants.
    /// I.e., even if a reaction has more than one reagent, it will only ever appear once in this dictionary.
    /// </summary>
    private FrozenDictionary<string, List<FuzzyReactionPrototype>> _reactionsSingle = default!;

    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeReactionCache();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    /// <summary>
    /// Handles building the reaction cache.
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
    /// Updates the reaction cache when the prototypes are reloaded.
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
            return false;

        if (solution.Temperature > reaction.MaximumTemperature)
            return false;

        //check if the right mixing category is used
        if (mixerComponent == null && reaction.MixingCategories != null ||
            mixerComponent != null && reaction.MixingCategories != null &&
            reaction.MixingCategories.Except(mixerComponent.ReactionTypes).Any())
            return false;

        //check for cancellation
        var attempt = new FuzzyReactionAttemptEvent(reaction, solutionEntity);
        RaiseLocalEvent(solutionEntity, ref attempt);
        if (attempt.Cancelled)
            return false;

        // are all reactants in solution?
        foreach (var reactant in reaction.Reactants)
        {
            var reactantQuantity = solution.GetTotalPrototypeQuantity(reactant.Key);
            if (reactantQuantity <= FixedPoint2.Zero)
                return false;
            // catalyst is not consumed, so will not limit the reaction. But it still needs to be present, and
            // for quantized reactions we need to have a minimum amount
            if (reactant.Value.Catalyst && reaction.Quantized && reactantQuantity < reactant.Value.Amount)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Perform a reaction on a solution. This assumes all reaction criteria are met.
    /// Removes the reactants from the solution, adds products, and returns a list of products.
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
        var reagentData = new FuzzyMixtureReagentData() { ReactionAmount = unitReactions };
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

        if (reaction.Product != null)
        {
            //compile do id

            //var id = new ReagentId(reaction.Product, [reagentData]); // I dare you to uncomment this. Assembly Checker does not accept it!
            var id = new ReagentId(reaction.Product, new List<ReagentData>());
            id.Data!.Add(reagentData.Clone());
            //Create product
            solution.AddReagent(id, (reaction.ProductAmount + deviations.Sum()) * unitReactions);
            if (reaction.ConserveEnergy)
            {
                var newCap = solution.GetHeatCapacity(_prototypeManager);
                if (newCap > 0)
                    solution.Temperature = energy / newCap;
            }
        }

        OnReaction(soln, reaction, null, unitReactions);

        //spawn and setup output entity (bread dough)
        if (reaction.OutputEntity.HasValue && reaction.Product.HasValue)
        {
            FixedPoint2 removed = FixedPoint2.Zero;
            while (true)
            {
                //remove product
                removed = soln.Comp.Solution.RemoveReagent(reaction.Product, reaction.ProductAmount);
                //if product has been exhausted, stop
                if (removed == 0)
                    break;
                //create entity
                var spawnedEntity = SpawnNextToOrDrop(reaction.OutputEntity.Value, soln);
                //add meal node
                var component = EnsureComp<MealTreeContainerComponent>(spawnedEntity);
                //setup meal node
                var quantity = new ReagentQuantity(reaction.Product, removed, new());
                quantity.Reagent.Data!.Add(reagentData.Clone());
                component.MealTree = _sharedMealSystem.MakeNodeFromReagentQuantity(quantity);
            }
        }

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
    /// Performs all chemical reactions that can be run on a solution.
    /// Removes the reactants from the solution, then returns a solution with all products.
    /// WARNING: Does not trigger reactions between solution and new products.
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
            bool expaned = false;
            //resolve any existing amount of the reaction product to its base components (in an attempt to grow the fuzzy mixture, either with other raw material or even other fuzzy mixtures of the same solution)
            if (reaction.Product != null)
            {
                actualSolution = actualSolution.Clone();

                ExpandSolution(out changed, reaction, actualSolution, out expaned);
            }

            //check basic conditions.
            if (!CanReactPrecheck(soln, reaction, mixerComponent, actualSolution))
                continue;
            if (!expaned)
                actualSolution = soln.Comp.Solution;
            //calculate ratios.
            var amount = FindBalancedMatch(actualSolution, reaction, out var ratios);
            if (amount == null)
                continue;
            //replace components solution with the actual solution, so that perform reaction works properly.
            soln.Comp.Solution = actualSolution;
            //check the amount of output from the reaction to see, if we actually expanded an existing solution or just recombination of self
            changed -= amount.Value * reaction.ProductAmount;
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
    /// Given a solutuin containing the product of an fuzzy reaction prototype,
    /// revereses the reagent back into the solution components (ignoring any catalyst.)
    /// used mainly as a helper for growing/adjusting existing solutions.
    /// </summary>
    /// <param name="changed"></param>
    /// <param name="reaction"></param>
    /// <param name="actualSolution"></param>
    /// <param name="expaned"></param>
    public static void ExpandSolution(out FixedPoint2 changed, FuzzyReactionPrototype reaction, Solution actualSolution, out bool expaned)
    {
        changed = 0;
        expaned = false;
        if (!reaction.Product.HasValue || reaction.ProductAmount == FixedPoint2.Zero)
            return;
        foreach (var splitTarget in actualSolution.Contents
                     .Where(e => e.Reagent.Prototype == reaction.Product!.Value)
                     .ToArray())
        {
            var mixtureData =
                splitTarget.Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData) as
                    FuzzyMixtureReagentData;
            //     var totalDeviation = mixtureData?.Mixture.Values.Select(e => FixedPoint2.Abs(e)).Sum() ?? FixedPoint2.Zero;
            var totalDeviation = mixtureData?.Mixture.Values.Sum() ?? FixedPoint2.Zero;
            //get the original unit reaction value.

            actualSolution.RemoveReagent(splitTarget);

            var unitReactions = mixtureData?.ReactionAmount ?? splitTarget.Quantity / (reaction.ProductAmount + totalDeviation);
            //use ratios + deviations to regenerate solution.
            changed += splitTarget.Quantity;

            foreach (var reactant in reaction.Reactants)
            {
                var deviation = mixtureData?.Mixture[reactant.Key] ?? FixedPoint2.Zero;
                //recreate original ammount and scale to current remaining volume.
              //  var amount = unitReactions * (reactant.Value.Amount + deviation) * (unitReactions / splitTarget.Quantity);
                var amount = unitReactions * (reactant.Value.Amount + deviation);

                actualSolution.AddReagent(reactant.Key, amount, false);
            }
            expaned = true;
        }
    }

    /// <summary>
    /// Given a solution maximise the reaction, before getting the error.
    /// </summary>
    /// <param name="soln"></param>
    /// <param name="reaction"></param>
    /// <param name="deviations"></param>
    /// <returns></returns>
    private FixedPoint2? FindBalancedMatch(Solution soln, FuzzyReactionPrototype reaction, out FixedPoint2[] deviations)
    {
        //calcuate deviations
        deviations = reaction.Reactants.Select(e => FixedPoint2.Zero).ToArray();
        //get available data
        var quantity = reaction.Reactants.Keys.Select(e => soln.GetTotalPrototypeQuantity(e)).ToArray();

        var coefficents = reaction.Reactants.Values.Select(e => e.Amount).ToArray();
        //calculate limits
        var deviationLow = reaction.Reactants.Values.Select(e => e.MinAmount.HasValue ? e.MinAmount - e.Amount : 0)
              .Select(e => e!.Value)
              .ToArray();
        var deviationHigh = reaction.Reactants.Values.Select(e => e.MaxAmount.HasValue ? e.MaxAmount - e.Amount : 0)
            .Select(e => e!.Value)
            .ToArray();

        //Get local T at minimum, maximum and optimum foreach reactant to the solution content.
        //at amout t would be optimal
        var optimums = reaction.Reactants.Values.Select((e, idx) => quantity[idx] / e.Amount).ToArray();
        //at min amount -> t would be maximal
        var maximums = reaction.Reactants.Values.Select((e, idx) => e.MinAmount.HasValue ? quantity[idx] / e.MinAmount.Value : optimums[idx]).ToArray();
        //at max amout t would be minimal
        var minimums = reaction.Reactants.Values.Select((e, idx) => e.MaxAmount.HasValue ? quantity[idx] / e.MaxAmount.Value : optimums[idx]).ToArray();

        //pick the average Optimum T and clamp to minimum and maxium
        var bestOptimum = optimums.Sum() / optimums.Length;
        for (var i = 0; i < optimums.Length; i++)
        {
            //constraint into smaller and smaller windows
            bestOptimum = FixedPoint2.Clamp(bestOptimum, minimums[i], maximums[i]);
        }
        if (bestOptimum == FixedPoint2.Zero)
            return null;
        //generate deviations at i with respect to maximum deviation
        for (var i = 0; i < deviations.Length; i++)
        {
            var remainder = (quantity[i] - (bestOptimum * coefficents[i])) / bestOptimum;

            deviations[i] = FixedPoint2.Clamp(remainder, deviationLow[i], deviationHigh[i]);
            //skip if 0
            if (remainder == FixedPoint2.Zero)
                continue;
        }
        //adjust deviations until they meet maximum deviation requirement.
        if (reaction.MaxDeviation.HasValue && reaction.MaxDeviation < deviations.Select(e => FixedPoint2.Abs(e)).Sum())
        {
            var dividers = deviations.Count(e => e != FixedPoint2.Zero);
            while (dividers > 0)
            {
                //check how much deviation is happening
                var deviationBudget = deviations.Select(e => FixedPoint2.Abs(e)).Sum();
                //if within limits, we are done.
                if (deviationBudget <= reaction.MaxDeviation)
                    return bestOptimum;
                //split burden equally by all dividers.
                var allowedShift = deviationBudget / dividers;
                if (allowedShift == FixedPoint2.Zero)
                {
                    //cannot reach shift requirement.
                    return null;
                }
                for (var i = 0; i < deviations.Length; i++)
                {
                    //skip already optimal ingredients
                    if (deviations[i] == FixedPoint2.Zero)
                        continue;
                    //move closer to 0
                    if (deviations[i] < 0)
                    {
                        deviations[i] = FixedPoint2.Min(0, deviations[i] + allowedShift);
                    }
                    else
                    {
                        deviations[i] = FixedPoint2.Max(0, deviations[i] - allowedShift);
                    }
                    //once at 0, we archived peak error absorption.
                    if (deviations[i] == FixedPoint2.Zero)
                    {
                        dividers--;
                        break;
                    }
                }
            }
            //burden cannot be split anymore and yet max error to big. reaction not viable.
            if (reaction.MaxDeviation < deviations.Select(e => FixedPoint2.Abs(e)).Sum())
                return null;
        }

        //return best t.
        return bestOptimum;
    }

    /// <summary>
    /// Continually react a solution until no more reactions occur, with a volume constraint.
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
/// Raised directed at the owner of a solution to determine whether the reaction should be allowed to occur.
/// </summary>
/// <reamrks>
/// Some solution containers (e.g., bloodstream, smoke, foam) use this to block certain reactions from occurring.
/// </reamrks>
[ByRefEvent]
public record struct FuzzyReactionAttemptEvent(FuzzyReactionPrototype Reaction, Entity<SolutionComponent> Solution)
{
    public readonly FuzzyReactionPrototype Reaction = Reaction;
    public readonly Entity<SolutionComponent> Solution = Solution;
    public bool Cancelled = false;
}
