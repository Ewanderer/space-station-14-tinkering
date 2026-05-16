using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared.Kitchen.OpenKitchen.Prototypes;

/// <summary>
/// Unlike Chemical reactions requiring precise ratios,
/// Fuzzy Reaction allow for a deviation from the ideal recipe.
/// While nearly identical from normal reactions, they are separated to
/// avoid cluttering the reaction system code
/// and thus overall improve performance as there are way less fuzzy reactions than regular ones.
/// </summary>
[Prototype]
public sealed partial class FuzzyReactionPrototype : IPrototype, IComparable<FuzzyReactionPrototype>
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Reactants required for the reaction to occur.
    /// </summary>
    [DataField("reactants",
        customTypeSerializer: typeof(PrototypeIdDictionarySerializer<FuzzyReactantPrototype, ReagentPrototype>))]
    public Dictionary<string, FuzzyReactantPrototype> Reactants = new();

    /// <summary>
    ///     The minimum temperature the reaction can occur at.
    /// </summary>
    [DataField("minTemp")]
    public float MinimumTemperature = 0.0f;

    /// <summary>
    ///     If true, this reaction will attempt to conserve thermal energy.
    /// </summary>
    [DataField("conserveEnergy")]
    public bool ConserveEnergy = true;

    /// <summary>
    ///     The maximum temperature the reaction can occur at.
    /// </summary>
    [DataField("maxTemp")]
    public float MaximumTemperature = float.PositiveInfinity;

    /// <summary>
    ///     The required mixing categories for an entity to mix the solution with for the reaction to occur
    /// </summary>
    [DataField("requiredMixerCategories")]
    public List<ProtoId<MixingCategoryPrototype>>? MixingCategories;

    /// <summary>
    /// Reagents put out by reaction. fuzzy can only have one actual outcome.
    /// </summary>
    [DataField("product")]
    public ProtoId<ReagentPrototype>? Product;

    /// <summary>
    /// Amount of the reaction created.
    /// </summary>
    [DataField("productAmount")]
    public FixedPoint2 ProductAmount;

    /// <summary>
    /// Effects to be triggered when the reaction occurs.
    /// </summary>
    [DataField("effects")] public EntityEffect[] Effects = [];

    /// <summary>
    /// How dangerous is this effect? Stuff like bicaridine should be low, while things like methamphetamine
    /// or potas/water should be high.
    /// </summary>
    [DataField("impact", serverOnly: true)]
    public LogImpact Impact = LogImpact.Low;

    // TODO SERV3: Empty on the client, (de)serialize on the server with module manager is server module
    [DataField("sound", serverOnly: true)] public SoundSpecifier Sound { get; private set; } =
        new SoundPathSpecifier("/Audio/Effects/Chemistry/bubbles.ogg");

    /// <summary>
    /// If true, this reaction will only consume only integer multiples of the reactant amounts. If there are not
    /// enough reactants, the reaction does not occur. Useful for spawn-entity reactions (e.g. creating cheese).
    /// </summary>
    [DataField("quantized")] public bool Quantized = false;

    /// <summary>
    /// Determines the order in which reactions occur. This should used to ensure that (in general) descriptive /
    /// pop-up generating and explosive reactions occur before things like foam/area effects.
    /// </summary>
    [DataField("priority")]
    public int Priority;

    /// <summary>
    /// Determines whether or not this reaction creates a new chemical (false) or if it's a breakdown for existing chemicals (true)
    /// Used in the chemistry guidebook to make divisions between recipes and reaction sources.
    /// </summary>
    /// <example>
    /// Mixing together two reagents to get a third -> false
    /// Heating a reagent to break it down into 2 different ones -> true
    /// </example>
    [DataField]
    public bool Source;

    /// <summary>
    /// Total deviation from ideal ratio permitted by this recipe.
    /// If null does not apply
    /// </summary>
    [DataField("maxDeviation")]
    public FixedPoint2? MaxDeviation = null;


    /// <summary>
    ///     Comparison for creating a sorted set of reactions. Determines the order in which reactions occur.
    /// </summary>
    public int CompareTo(FuzzyReactionPrototype? other)
    {
        if (other == null)
            return -1;

        if (Priority != other.Priority)
            return other.Priority - Priority;

        return string.Compare(ID, other.ID, StringComparison.Ordinal);
    }
}

/// <summary>
/// Prototype for chemical reaction reactants.
/// </summary>
[DataDefinition]
public sealed partial class FuzzyReactantPrototype
{
    /// <summary>
    /// Minimum amount of the reactant needed for the reaction to occur.
    /// </summary>
    [DataField("amount")]
    public FixedPoint2 Amount { get; private set; } = FixedPoint2.New(1);

    /// <summary>
    /// minimum amount of the reactant allowed for the reaction
    /// if null is amount.
    /// </summary>
    [DataField("minAmount")]
    public FixedPoint2? MinAmount { get; private set; } = FixedPoint2.New(1);

    /// <summary>
    /// maximum amount of the reactant allowed for the reaction
    /// if null is amount.
    /// </summary>
    [DataField("maxAmount")]
    public FixedPoint2? MaxAmount { get; private set; } = FixedPoint2.New(1);

    /// <summary>
    /// Whether or not the reactant is a catalyst. Catalysts aren't removed when a reaction occurs.
    /// </summary>
    [DataField("catalyst")]
    public bool Catalyst { get; private set; }
}
