using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Utility;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.OpenKitchen.Components;
using Content.Shared.Kitchen.OpenKitchen.EntitySystems;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Collections.Generic;
using System.Linq;

namespace Content.IntegrationTests.Tests.Kitchen;

[TestFixture]
[TestOf(typeof(FuzzyReactionPrototype))]
public sealed class FuzzyReactionSystemTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestFuzzySolutionContainer
  components:
  - type: Solution
    id: beaker
    solution:
      maxVol: 120";

    private static string[] _reactions = GameDataScrounger.PrototypesOfKind<FuzzyReactionPrototype>();

    [Test]
    [TestCaseSource(nameof(_reactions))]
    [TestOf(typeof(FuzzyReactionPrototype))]
    [Description("Tries an individual fuzzy reaction to see if it succeeds.")]
    public async Task TryFullReaction(string reaction)
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var coordinates = testMap.GridCoords;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

        var reactionPrototype = prototypeManager.Index<FuzzyReactionPrototype>(reaction);

        EntityUid beaker = default;
        Solution solution = null;
        Entity<SolutionComponent>? solutionEnt = default!;

        await server.WaitAssertion(() =>
        {
            beaker = entityManager.SpawnEntity("TestSolutionContainer", coordinates);
            Assert.That(solutionContainerSystem
                .TryGetSolution(beaker, "beaker", out solutionEnt, out solution));
            solutionContainerSystem.SetCanReact(solutionEnt!.Value, false);
            foreach (var (id, reactant) in reactionPrototype.Reactants)
            {
#pragma warning disable NUnit2045
                Assert.That(solutionContainerSystem
                    .TryAddReagent(solutionEnt.Value,
                        id,
                        reactant.Amount,
                        out var quantity,
                        reactionPrototype.MinimumTemperature));
                Assert.That(reactant.Amount, Is.EqualTo(quantity));
#pragma warning restore NUnit2045
            }

            //Get all possible reactions with the current reagents
            var possibleReactions = prototypeManager.EnumeratePrototypes<FuzzyReactionPrototype>()
                .Where(x => x.Reactants.All(id => solution.Contents.Any(s => s.Reagent.Prototype == id.Key)))
                .ToList();

            //Check if the reaction is the first to occur when heated
            foreach (var possibleReaction in possibleReactions.OrderBy(r => r.MinimumTemperature))
            {
                if (possibleReaction.Priority >= reactionPrototype.Priority &&
                    possibleReaction.MinimumTemperature < reactionPrototype.MinimumTemperature &&
                    possibleReaction.MixingCategories == reactionPrototype.MixingCategories)
                {
                    Assert.Fail(
                        $"The {possibleReaction.ID} reaction may occur before {reactionPrototype.ID} when heated.");
                }
            }

            //Check if the reaction is the first to occur when freezing
            foreach (var possibleReaction in possibleReactions.OrderBy(r => r.MaximumTemperature))
            {
                if (possibleReaction.Priority >= reactionPrototype.Priority &&
                    possibleReaction.MaximumTemperature > reactionPrototype.MaximumTemperature &&
                    possibleReaction.MixingCategories == reactionPrototype.MixingCategories)
                {
                    Assert.Fail(
                        $"The {possibleReaction.ID} reaction may occur before {reactionPrototype.ID} when freezing.");
                }
            }

            //Now safe set the temperature and mix the reagents
            solutionContainerSystem.SetTemperature(solutionEnt.Value, reactionPrototype.MinimumTemperature);
            solutionContainerSystem.SetCanReact(solutionEnt.Value, true);

            if (reactionPrototype.MixingCategories != null)
            {
                var dummyEntity = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
                var mixerComponent = entityManager.AddComponent<ReactionMixerComponent>(dummyEntity);
                mixerComponent.ReactionTypes = reactionPrototype.MixingCategories;
                solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
            }
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var expectedProducts = reactionPrototype.Reactants
                .Where(e => e.Value.Catalyst)
                .Select(e => (e.Key, e.Value.Amount))
                .Append((reactionPrototype.Product, reactionPrototype.ProductAmount))
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .ToArray();


            Assert.That(expectedProducts.All(e => solution.GetTotalPrototypeQuantity(e.Key) == e.Amount));
            Assert.That(expectedProducts.Count() == solution.Contents.Count());

            server.EntMan.DeleteEntity(beaker);
        });
    }

    [Test]
    [TestCaseSource(nameof(_reactions))]
    [TestOf(typeof(FuzzyReactionPrototype))]
    [Description("Tries an individual fuzzy reaction to see if it succeed at minimum reactants.")]
    public async Task TryMinimumReaction(string reaction)
    {
        var targets = new Dictionary<string, FixedPoint2>();
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var coordinates = testMap.GridCoords;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

        var reactionPrototype = prototypeManager.Index<FuzzyReactionPrototype>(reaction);

        EntityUid beaker = default;
        Solution solution = null;
        Entity<SolutionComponent>? solutionEnt = default!;

        await server.WaitAssertion(() =>
        {
            beaker = entityManager.SpawnEntity("TestSolutionContainer", coordinates);
            Assert.That(solutionContainerSystem
                .TryGetSolution(beaker, "beaker", out solutionEnt, out solution));
            solutionContainerSystem.SetCanReact(solutionEnt!.Value, false);
            var split = reactionPrototype.MaxDeviation;


            foreach (var (id, reactant) in reactionPrototype.Reactants)
            {
#pragma warning disable NUnit2045
                var targetAmount = split.HasValue ? Shared.FixedPoint.FixedPoint2.Max(reactant.MinAmount ?? reactant.Amount, reactant.Amount.Value - split.Value / reactionPrototype.Reactants.Count) : reactant.MinAmount ?? reactant.Amount;
                targets.Add(id, targetAmount);
                Assert.That(solutionContainerSystem
                    .TryAddReagent(solutionEnt.Value,
                        id,
                        targetAmount,
                        out var quantity,
                        reactionPrototype.MinimumTemperature));
                Assert.That(targetAmount, Is.EqualTo(quantity));
#pragma warning restore NUnit2045
            }

            //Get all possible reactions with the current reagents
            var possibleReactions = prototypeManager.EnumeratePrototypes<FuzzyReactionPrototype>()
                .Where(x => x.Reactants.All(id => solution.Contents.Any(s => s.Reagent.Prototype == id.Key)))
                .ToList();

            //Check if the reaction is the first to occur when heated
            foreach (var possibleReaction in possibleReactions.OrderBy(r => r.MinimumTemperature))
            {
                if (possibleReaction.Priority >= reactionPrototype.Priority &&
                    possibleReaction.MinimumTemperature < reactionPrototype.MinimumTemperature &&
                    possibleReaction.MixingCategories == reactionPrototype.MixingCategories)
                {
                    Assert.Fail(
                        $"The {possibleReaction.ID} reaction may occur before {reactionPrototype.ID} when heated.");
                }
            }

            //Check if the reaction is the first to occur when freezing
            foreach (var possibleReaction in possibleReactions.OrderBy(r => r.MaximumTemperature))
            {
                if (possibleReaction.Priority >= reactionPrototype.Priority &&
                    possibleReaction.MaximumTemperature > reactionPrototype.MaximumTemperature &&
                    possibleReaction.MixingCategories == reactionPrototype.MixingCategories)
                {
                    Assert.Fail(
                        $"The {possibleReaction.ID} reaction may occur before {reactionPrototype.ID} when freezing.");
                }
            }

            //Now safe set the temperature and mix the reagents
            solutionContainerSystem.SetTemperature(solutionEnt.Value, reactionPrototype.MinimumTemperature);
            solutionContainerSystem.SetCanReact(solutionEnt.Value, true);

            if (reactionPrototype.MixingCategories != null)
            {
                var dummyEntity = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
                var mixerComponent = entityManager.AddComponent<ReactionMixerComponent>(dummyEntity);
                mixerComponent.ReactionTypes = reactionPrototype.MixingCategories;
                solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
            }
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            if (!reactionPrototype.Product.HasValue)
            {
                //check if the reaction has occured by changing solution content.
                Assert.That(reactionPrototype.Reactants.All(e => targets[e.Key] != solution.GetTotalPrototypeQuantity(e.Key)));
                return;
            }

            //check for output
            Assert.That(solution.ContainsPrototype(reactionPrototype.Product.Value));

            var fuzzyProductMixture = solution.Contents.FirstOrDefault(e => e.Reagent.Prototype == reactionPrototype.Product.Value);
            var mixtureData = fuzzyProductMixture.Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData) as FuzzyMixtureReagentData;
            Assert.That(mixtureData != null);

            //   var deviation = reactionPrototype.MaxDeviation ?? reactionPrototype.Reactants.Select(e => FixedPoint2.Abs(e.Value.Amount - e.Value.MinAmount ?? 0)).Sum();


            var x = mixtureData.Mixture.ToDictionary(e => e.Key, e => e.Value + reactionPrototype.Reactants[e.Key].Amount);
            //Assert that deviations are in bounds.
            Assert.That(x.All(e => (reactionPrototype.Reactants[e.Key].MinAmount ?? reactionPrototype.Reactants[e.Key].Amount) <= e.Value && e.Value <= (reactionPrototype.Reactants[e.Key].MaxAmount ?? reactionPrototype.Reactants[e.Key].Amount)));
            if (reactionPrototype.MaxDeviation.HasValue)
            {
                Assert.That(mixtureData.Mixture.Select(e => FixedPoint2.Abs(e.Value)).Sum() <= reactionPrototype.MaxDeviation.Value);
            }


            //assert that expansion of solution contains original components.
            Solution expanedSolution = solution.Clone();
            FuzzyReactionSystem.ExpandSolution(out var changed, reactionPrototype, expanedSolution, out var expanded);
            Assert.That(targets.Count == expanedSolution.Contents.Count);
            Assert.That(targets.All(e => expanedSolution.GetTotalPrototypeQuantity(e.Key) == e.Value));
            server.EntMan.DeleteEntity(beaker);
        });
    }

}
