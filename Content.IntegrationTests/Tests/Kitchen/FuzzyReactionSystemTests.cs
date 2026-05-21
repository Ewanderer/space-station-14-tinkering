using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Utility;
using Content.Server.Forensics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen.OpenKitchen.Components;
using Content.Shared.Kitchen.OpenKitchen.EntitySystems;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using NUnit.Framework.Constraints;
using NUnit.Framework.Internal;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
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
      maxVol: 1000";

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


    [Test]
    [TestCaseSource(nameof(_reactions))]
    [TestOf(typeof(FuzzyReactionPrototype))]
    [Description("Tries an individual fuzzy reaction to see if it succeed at random ratios.")]
    public async Task TryRandomReaction(string reaction)
    {
        var pair = Pair;
        var server = pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var coordinates = testMap.GridCoords;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        //make randomizer.
        var randomizer = Server.ResolveDependency<IRobustRandom>();
        //set random based to reaction name.
        randomizer.SetSeed(reaction.GetHashCode());

        var reactionPrototype = prototypeManager.Index<FuzzyReactionPrototype>(reaction);

        EntityUid beaker = default;
        Solution solution = null;
        Entity<SolutionComponent>? solutionEnt = default!;

        for (int tries = 0; tries < 10; tries++)
        {
            var targets = new Dictionary<string, FixedPoint2>();
            await server.WaitAssertion(() =>
            {
                beaker = entityManager.SpawnEntity("TestSolutionContainer", coordinates);
                Assert.That(solutionContainerSystem
                    .TryGetSolution(beaker, "beaker", out solutionEnt, out solution));
                solutionContainerSystem.SetCanReact(solutionEnt!.Value, false);
                var split = reactionPrototype.MaxDeviation.HasValue ? reactionPrototype.MaxDeviation.Value / reactionPrototype.Reactants.Count : -1; ;

                int i = 0;
                foreach (var (id, reactant) in reactionPrototype.Reactants)
                {
#pragma warning disable NUnit2045

                    FixedPoint2 targetAmount;
                    if (split >= 0)
                    {
                        if (i % 2 == 0)
                        {
                            targetAmount = randomizer.NextDouble((-split + reactant.Amount).Double(), reactant.Amount.Double());
                        }
                        else
                        {
                            targetAmount = randomizer.NextDouble(reactant.Amount.Double(), (+split + reactant.Amount).Double());
                        }

                        targetAmount = FixedPoint2.Clamp(targetAmount, (reactant.MinAmount ?? reactant.Amount), (reactant.MaxAmount ?? reactant.Amount));
                    }
                    else
                    {
                        if (i % 2 == 0)
                        {
                            targetAmount = randomizer.NextDouble((reactant.MinAmount ?? reactant.Amount).Double(), reactant.Amount.Double());
                        }
                        else
                        {
                            targetAmount = randomizer.NextDouble(reactant.Amount.Double(), (reactant.MaxAmount ?? reactant.Amount).Double());
                        }
                    }
                    i++;

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
                foreach (var target in targets)
                {
                    if (expanedSolution.GetTotalPrototypeQuantity(target.Key) != target.Value)
                        Assert.Fail($"Output Solution invalid after {tries} tries");
                }

                server.EntMan.DeleteEntity(beaker);
            });
        }
    }


    [Test]
    [TestCaseSource(nameof(_reactions))]
    [TestOf(typeof(FuzzyReactionPrototype))]
    [Description("Tries an individual fuzzy reactions with expansion to balance it to 0 deviation in setup + one expansion step.")]
    public async Task TryBalancingReaction(string reaction)
    {
        var pair = Pair;
        var server = pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var coordinates = testMap.GridCoords;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

        var reactionPrototype = prototypeManager.Index<FuzzyReactionPrototype>(reaction);
        //get a common demoniator by multiplying each unique coeffient and double (to have at least 1 item)
        var multiple = reactionPrototype.Reactants.Values.Select(e => e.Amount).Distinct().Aggregate((e, f) => e * f) * 2;
        //ensure multiple is a whole number
        if (multiple.Int() != multiple)
        {
            multiple *= 100;
            multiple = multiple.Int();
        }

        EntityUid beaker = default;
        Solution solution = null;
        Entity<SolutionComponent>? solutionEnt = default!;

        await server.WaitAssertion(() =>
        {
            beaker = entityManager.SpawnEntity("TestSolutionContainer", coordinates);
            Assert.That(solutionContainerSystem
                .TryGetSolution(beaker, "beaker", out solutionEnt, out solution));
            solutionContainerSystem.SetCanReact(solutionEnt!.Value, false);
            solution.MaxVolume = FixedPoint2.MaxValue;


            foreach (var (id, reactant) in reactionPrototype.Reactants)
            {
#pragma warning disable NUnit2045
                Assert.That(solutionContainerSystem
                    .TryAddReagent(solutionEnt.Value,
                        id,
                        reactant.Amount,
                        out var quantity,
                        reactionPrototype.MinimumTemperature));
#pragma warning restore NUnit2045
            }

            //Now safe set the temperature and mix the reagents
            solutionContainerSystem.SetTemperature(solutionEnt.Value, reactionPrototype.MinimumTemperature);
            solutionContainerSystem.SetCanReact(solutionEnt.Value, true);
            ReactionMixerComponent? mixerComponent = null;
            if (reactionPrototype.MixingCategories != null)
            {
                var dummyEntity = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
                mixerComponent = entityManager.AddComponent<ReactionMixerComponent>(dummyEntity);
                mixerComponent.ReactionTypes = reactionPrototype.MixingCategories;
                solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
            }
            //add the remainders.
            solutionContainerSystem.SetCanReact(solutionEnt.Value, false);
            foreach (var (id, reactant) in reactionPrototype.Reactants)
            {
#pragma warning disable NUnit2045
                Assert.That(solutionContainerSystem
                    .TryAddReagent(solutionEnt.Value,
                        id,
                        reactant.Amount * (multiple - 1),
                        out var quantity,
                        reactionPrototype.MinimumTemperature));
#pragma warning restore NUnit2045
            }
            solutionContainerSystem.SetCanReact(solutionEnt.Value, true);

            solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            solution = solutionEnt.Value.Comp.Solution;
            var expectedProducts = reactionPrototype.Reactants
                .Where(e => e.Value.Catalyst)
                .Select(e => (e.Key, e.Value.Amount))
                .Append((reactionPrototype.Product, reactionPrototype.ProductAmount))
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .ToArray();
            Assert.That(expectedProducts.All(e => solution.GetTotalPrototypeQuantity(e.Key) == e.Amount * multiple));
            Assert.That(expectedProducts.Count() == solution.Contents.Count());
            var fuzzyProductMixture = solution.Contents.FirstOrDefault(e => e.Reagent.Prototype == reactionPrototype.Product.Value);
            var mixtureData = fuzzyProductMixture.Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData) as FuzzyMixtureReagentData;
            Assert.That(mixtureData != null);

            Assert.That(mixtureData!.Mixture.Values.All(e => e == 0));

            server.EntMan.DeleteEntity(beaker);
        });
    }

    [Test]
    [TestCaseSource(nameof(_reactions))]
    [TestOf(typeof(FuzzyReactionPrototype))]
    [Description("Tries an individual fuzzy reactions with expansion to balance it to 0 deviation in setup + many small steps.")]
    public async Task TryBalancingIterativelyReaction(string reaction)
    {
        var pair = Pair;
        var server = pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var coordinates = testMap.GridCoords;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

        var reactionPrototype = prototypeManager.Index<FuzzyReactionPrototype>(reaction);
        //get a common demoniator by multiplying each unique coeffient and double (to have at least 1 item)
        var multiple = reactionPrototype.Reactants.Values.Select(e => e.Amount).Distinct().Aggregate((e, f) => e * f) * 2;
        //ensure multiple is a whole number
        if (multiple.Int() != multiple)
        {
            multiple *= 100;
            multiple = multiple.Int();
        }

        EntityUid beaker = default;
        Solution solution = null;
        Entity<SolutionComponent>? solutionEnt = default!;

        await server.WaitAssertion(() =>
        {
            beaker = entityManager.SpawnEntity("TestSolutionContainer", coordinates);
            Assert.That(solutionContainerSystem
                .TryGetSolution(beaker, "beaker", out solutionEnt, out solution));
            solutionContainerSystem.SetCanReact(solutionEnt!.Value, false);
            solution.MaxVolume = FixedPoint2.MaxValue;


            foreach (var (id, reactant) in reactionPrototype.Reactants)
            {
#pragma warning disable NUnit2045
                Assert.That(solutionContainerSystem
                    .TryAddReagent(solutionEnt.Value,
                        id,
                        reactant.Amount,
                        out var quantity,
                        reactionPrototype.MinimumTemperature));
#pragma warning restore NUnit2045
            }

            //Now safe set the temperature and mix the reagents
            solutionContainerSystem.SetTemperature(solutionEnt.Value, reactionPrototype.MinimumTemperature);
            solutionContainerSystem.SetCanReact(solutionEnt.Value, true);
            ReactionMixerComponent? mixerComponent = null;
            if (reactionPrototype.MixingCategories != null)
            {
                var dummyEntity = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
                mixerComponent = entityManager.AddComponent<ReactionMixerComponent>(dummyEntity);
                mixerComponent.ReactionTypes = reactionPrototype.MixingCategories;
                solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
            }
            //add the remainder over many iterations.
            for (var iteration = 1; iteration < multiple; iteration++)
            {
                solutionContainerSystem.SetCanReact(solutionEnt.Value, false);
                foreach (var (id, reactant) in reactionPrototype.Reactants)
                {
#pragma warning disable NUnit2045
                    Assert.That(solutionContainerSystem
                        .TryAddReagent(solutionEnt.Value,
                            id,
                            reactant.Amount,
                            out var quantity,
                            reactionPrototype.MinimumTemperature));
#pragma warning restore NUnit2045
                }
                solutionContainerSystem.SetCanReact(solutionEnt.Value, true);
                solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
            }
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            solution = solutionEnt.Value.Comp.Solution;
            //look for products and catalyst. allow for trace ammouts of other reactants
            var expectedProducts = reactionPrototype.Reactants
                .Where(e => e.Value.Catalyst)
                .Select(e => new Tuple<string, FixedPoint2>(e.Key, e.Value.Amount))
                .Concat(reactionPrototype.Reactants.Where(e => !e.Value.Catalyst).Select(e => new Tuple<string, FixedPoint2>(e.Key, 0)))
                .Append(new(reactionPrototype.Product, reactionPrototype.ProductAmount))
                .Where(e => !string.IsNullOrWhiteSpace(e.Item1))
                .ToArray();
            //check the difference, it should be for all potential items in solution less than 0.5
            Assert.That(expectedProducts.All(e => FixedPoint2.Abs(solution.GetTotalPrototypeQuantity(e.Item1) - e.Item2 * multiple) < 0.5));

            Assert.That(expectedProducts.Count() >= solution.Contents.Count());
            var fuzzyProductMixture = solution.Contents.FirstOrDefault(e => e.Reagent.Prototype == reactionPrototype.Product.Value);
            var mixtureData = fuzzyProductMixture.Reagent.Data?.FirstOrDefault(e => e is FuzzyMixtureReagentData) as FuzzyMixtureReagentData;
            Assert.That(mixtureData != null);
            //deviations should also be close to 0.
            Assert.That(mixtureData!.Mixture.Values.All(e => FixedPoint2.Abs(e) < 0.5));

            server.EntMan.DeleteEntity(beaker);
        });
    }

    [Test]
    [TestCaseSource(nameof(_reactions))]
    [TestOf(typeof(FuzzyReactionPrototype))]
    [Description("Tries an individual fuzzy reaction to see if it succeeds.")]
    public async Task TryRecombinationReaction(string reaction)
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var coordinates = testMap.GridCoords;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var fuzzyReactionSystem = server.System<FuzzyReactionSystem>();

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

            //now split in 2
            var split = solutionEnt.Value.Comp.Solution.SplitSolution(solutionEnt.Value.Comp.Solution.Volume / 2);
            //expand split without allowing recombination
            split.CanReact = false;
            //mix back in.
            FuzzyReactionSystem.ExpandSolution(out _, reactionPrototype, split, out _);

            solutionEnt.Value.Comp.Solution.AddSolution(split, prototypeManager);

            //force reaction again.
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
            //assert we have obtained the correct product in the correct amout.
            var expectedProducts = reactionPrototype.Reactants
                .Where(e => e.Value.Catalyst)
                .Select(e => (e.Key, e.Value.Amount))
                .Append((reactionPrototype.Product, reactionPrototype.ProductAmount))
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .ToArray();
            solution = solutionEnt.Value.Comp.Solution;

            Assert.That(expectedProducts.All(e => solution.GetTotalPrototypeQuantity(e.Key) == e.Amount));
            Assert.That(expectedProducts.Count() == solution.Contents.Count());
          //  Assert.That(solution.Contents.Any(e=>e.Reagent.Data?.Any(d=)))

            server.EntMan.DeleteEntity(beaker);
        });
    }


}
