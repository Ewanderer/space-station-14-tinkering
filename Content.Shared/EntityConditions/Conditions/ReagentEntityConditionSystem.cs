using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityConditions.Conditions;

/// <summary>
/// Returns true if this solution entity has an amount of reagent in it within a specified minimum and maximum.
/// </summary>
/// <inheritdoc cref="EntityConditionSystem{T, TCondition}"/>
public sealed partial class ReagentEntityConditionSystem : EntityConditionSystem<SolutionComponent, ReagentCondition>
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SolutionComponent, EntityConditionEvent<ReagentCondition, Solution>>(ConditionOnSolution);
        SubscribeLocalEvent<SolutionComponent, EntityConditionScaleEvent<ReagentCondition, Solution>>(
            ConditionScaleOnSolution);
        SubscribeLocalEvent<SolutionComponent, EntityConditionScaleEvent<ReagentCondition, EntityUid>>(
            ConditionScale);
    }

    private void ConditionScale(Entity<SolutionComponent> ent, ref EntityConditionScaleEvent<ReagentCondition, EntityUid> args)
    {
        args.Result = EvaluateScale(args.Condition, ent.Comp.Solution);
    }

    private void ConditionScaleOnSolution(Entity<SolutionComponent> ent,
        ref EntityConditionScaleEvent<ReagentCondition, Solution> args)
    {
        if (args.SourceObject == null)
            return;
        args.Result = EvaluateScale(args.Condition, args.SourceObject);
    }

    private static float EvaluateScale(ReagentCondition condition, Solution solution)
    {
        return ((solution.Temperature - condition.Min) / (condition.Max - condition.Min)).Float();
    }

    private void ConditionOnSolution(Entity<SolutionComponent> ent,
        ref EntityConditionEvent<ReagentCondition, Solution> args)
    {
        if (args.SourceObject == null)
            return;
        args.Result = CheckOnCondition(args.Condition, args.SourceObject);
    }

    protected override void Condition(Entity<SolutionComponent> entity,
        ref EntityConditionEvent<ReagentCondition, EntityUid> args)
    {
        var soln = entity.Comp.Solution;
        args.Result = CheckOnCondition(args.Condition, soln);
    }

    private static bool CheckOnCondition(ReagentCondition condition, Solution soln)
    {
        var quant = soln.GetTotalPrototypeQuantity(condition.Reagent);
        return quant >= condition.Min && quant <= condition.Max;
    }
}

/// <inheritdoc cref="EntityCondition"/>
public sealed partial class ReagentCondition : EntityConditionBase<ReagentCondition>
{
    [DataField]
    public FixedPoint2 Min = FixedPoint2.Zero;

    [DataField]
    public FixedPoint2 Max = FixedPoint2.MaxValue;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    public override string EntityConditionGuidebookText(IPrototypeManager prototype)
    {
        if (!prototype.Resolve(Reagent, out var reagentProto))
            return String.Empty;

        return Loc.GetString("entity-condition-guidebook-reagent-threshold",
            ("reagent", reagentProto.LocalizedName),
            ("max", Max == FixedPoint2.MaxValue ? int.MaxValue : Max.Float()),
            ("min", Min.Float()));
    }
}
