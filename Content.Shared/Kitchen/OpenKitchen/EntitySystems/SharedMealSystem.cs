using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.Components;
using Content.Shared.Kitchen.OpenKitchen.Components;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen.OpenKitchen.EntitySystems;

/// <summary>
/// Contains public API's to safely interact with a MealNode.
/// </summary>
public sealed partial class SharedMealSystem : EntitySystem
{
    /// <summary>
    /// For consistency within the system
    /// and to reduce headache in recipe prototypes,
    /// Cookedness is mapped to certain fixed degrees.
    /// </summary>
    public enum CookDegree
    {
        Frozen,
        Raw,
        Undercooked,
        Medium,
        WellDone,
        Crispy,
        Burnt,
    }


    [Dependency] private IPrototypeManager _proto = default!;

    private ReagentMealPrototype _fallbackPrototype = default!;

    static readonly ProtoId<ReagentMealPrototype> FallbackPrototypeId = "QuestionableIngredient";

    Dictionary<ProtoId<ReagentPrototype>, ReagentMealPrototype> _reagentsToMealDictionary = new();

    public override void Initialize()
    {
        base.Initialize();
     //   _fallbackPrototype = _proto.Index(FallbackPrototypeId);
        InitializeReagentsToMealDictionary();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs eventArgs)
    {
        if (eventArgs.WasModified<ReagentMealPrototype>())
            InitializeReagentsToMealDictionary();
    }

    private void InitializeReagentsToMealDictionary()
    {
        _reagentsToMealDictionary.Clear();
        foreach (var prototype in _proto.EnumeratePrototypes<ReagentMealPrototype>())
            _reagentsToMealDictionary.Add(prototype.Reagent, prototype);
    }

    /// <summary>
    /// A unified mapping of Cookedness to the description of the meals node degree of cookedness.
    /// Is used to determine actual meal.
    /// </summary>
    public static CookDegree GetDegreeOfCoockedness(MealNode node)
    {
        if (node.Cookedness < 0)
            return CookDegree.Frozen;
        if (node.Cookedness < 25)
            return CookDegree.Raw;
        if (node.Cookedness < 50)
            return CookDegree.Undercooked;
        if (node.Cookedness < 75)
            return CookDegree.Medium;
        if (node.Cookedness < 100)
            return CookDegree.WellDone;
        if (node.Cookedness < 125)
            return CookDegree.Crispy;
        return CookDegree.Burnt;
    }


    /// <summary>
    /// Adds a solution to a meal.
    /// Either by joining it into the hull
    /// or creating new containers for it.
    /// input solution will be modified
    /// </summary>
    /// <param name="solution"></param>
    /// <param name="meal"></param>
    /// <param name="removedAmount"></param>
    public void AddToMeal(Solution solution, MealNode meal, out FixedPoint2 removedAmount)
    {
        removedAmount = FixedPoint2.Min(meal.Capacity - meal.Volume, solution.Volume);
        if (removedAmount == FixedPoint2.Zero)
            return;
        var removedSolution = solution.SplitSolution(removedAmount);
        //break off any part of the solution suitable for hull
        foreach (var item in removedSolution.Contents.ToArray())
        {
            if (ReagentCanBeAddedToHull(item.Reagent, meal))
            {
                removedSolution.RemoveReagent(item.Reagent, item.Quantity);
                meal.HullSolution!.AddReagent(item);
                //update hull setup.
                SetupMealFromHullSolution(meal);
            }
        }

        //make a container from the remainder of removed solution.
        foreach (var item in removedSolution.Contents)
            meal.Ingredients.Add(MakeNodeFromReagentQuantity(item));
        //fill meal volume.
        meal.Volume += removedAmount;
    }

    private void SetupMealFromHullSolution(MealNode meal)
    {
        if (meal.HullSolution == null || meal.HullSolution.Contents.Count > 1)
            return;
        var hull = meal.HullSolution.First();
        //find the matching recipe or use fallback.
        if (!_reagentsToMealDictionary.TryGetValue(hull.Reagent.Prototype, out var mealPrototype))
            mealPrototype = _fallbackPrototype;

        FixedPoint2 mixtureCapacityFactor = 1;
        FixedPoint2 mixtureFlavorFactor = 1;
        var mixture = (hull.Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData) as FuzzyMixtureReagentData)?.Mixture;
        //evaluate fuzzyness of mixture to adjust key hull statistics.
        foreach (var part in mealPrototype.FuzzyFactors)
        {
            if (mixture == null || !mixture.TryGetValue(part.Reagent, out var deviation))
                deviation = 0;
            deviation = FixedPoint2.Clamp(deviation, part.Minimum, part.Maximum);
            var t = (deviation - part.Minimum) / (part.Maximum - part.Minimum);
            if (part.CapacityFactorAtMaximum.HasValue && part.CapacityFactorAtMinimum.HasValue)
                mixtureCapacityFactor *= part.CapacityFactorAtMinimum.Value + t * (part.CapacityFactorAtMaximum.Value - part.CapacityFactorAtMinimum.Value);
        }

        //setup total capacity.
        meal.Capacity = mealPrototype.CapacityFactor * hull.Quantity * mixtureCapacityFactor;
    }

    /// <summary>
    /// Try to feed a non solution entity,
    /// like an apple or bar of uranium to a meal.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="meal"></param>
    /// <returns></returns>
    public bool TryAddToMeal(EntityUid entity, MealNode meal)
    {
        //calculate the remaining space in the meal.
        var remainingSpace = meal.Capacity - meal.Volume;
        if (remainingSpace <= 0)
            return false;
        // check if entity has proto meal component
        if (TryComp(entity, out StaticMealComponent? protoMeal))
        {
            var protoMealContainer = MakeMealFromProtoMeal(new Entity<StaticMealComponent>(entity, protoMeal));
            //check capacity vs meal
            if (protoMealContainer.Volume > remainingSpace)
                return false;
            meal.Ingredients.Add(protoMealContainer);
            meal.Volume += protoMealContainer.Volume;
            return true;
        }

        Solution? addedSolution = null;
        // check if juiceable
        if (TryComp(entity, out ExtractableComponent? extractableComponent) &&
            (extractableComponent.JuiceSolution?.Contents.Any() ?? false))
            addedSolution = extractableComponent.JuiceSolution;
        else if (TryComp(entity, out SolutionComponent? solutionComponent)) //use raw solution
            addedSolution = solutionComponent.Solution;

        //if no match or too big, cannot add.
        if (addedSolution == null || addedSolution.Volume > remainingSpace)
            return false;
        //adding to meal as a raw solution.
        AddToMeal(addedSolution, meal, out _);
        return true;
    }

    /// <summary>
    /// Sets the actual values of a meal node
    /// </summary>
    /// <param name="target"></param>
    public void EvaluateMealTree(MealNode target)
    {

    }

    private bool ReagentCanBeAddedToHull(ReagentId reagentId, MealNode mealNode)
    {
        if (mealNode.HullSolution == null)
            return false;
        //the only joinable hulls are made from one reagent!
        if (mealNode.HullSolution.Contents.Count > 1)
            return false;

        //check is reagent prototype is equal
        if (reagentId.Prototype != mealNode.HullSolution.Contents.First().Reagent.Prototype)
            return false;
        //check if there is a mixture that can/must be expanded.
        var mixture = reagentId.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData);
        if (mixture != null && !mixture.Equals(mealNode.HullSolution.Contents.First().Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData)))
            return false;

        return true;
    }

    private MealNode MakeMealFromProtoMeal(Entity<StaticMealComponent> protoMeal)
    {
        Solution? solution = null;
        switch (protoMeal.Comp.HullSource)
        {
            case StaticMealComponent.HullSourceOption.NoHull:
                break;
            case StaticMealComponent.HullSourceOption.ProtoMealComponent:
                solution = protoMeal.Comp.HullSolution;
                break;
            case StaticMealComponent.HullSourceOption.ExtractableComponent:
                if (TryComp(protoMeal, out ExtractableComponent? extractableComponent))
                    solution = extractableComponent.JuiceSolution;
                break;
            case StaticMealComponent.HullSourceOption.SolutionComponent:
                if (TryComp(protoMeal, out SolutionComponent? solutionComponent))
                    solution = solutionComponent.Solution;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return new MealNode
        {
            Capacity = protoMeal.Comp.Capacity,
            MealType = _proto.Index(protoMeal.Comp.Prototype),
            HullSolution = solution,
            Volume = protoMeal.Comp.HullSolution?.Volume ?? FixedPoint2.Zero,
        };
    }

    public MealNode MakeNodeFromReagentQuantity(ReagentQuantity reagtentQuantity)
    {
        //check
        var mealNode = new MealNode() { HullSolution = new Solution([reagtentQuantity]) };
        SetupMealFromHullSolution(mealNode);
        return mealNode;
    }
}
