using System.IO;
using Content.Shared.Kitchen.OpenKitchen.Prototypes;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared.Kitchen;

[TestFixture, TestOf(typeof(FuzzyReactionPrototype))]
public sealed class FuzzyReactionPrototypeTests : ContentUnitTest
{
    [Test]
    public void DeserializeReagentPrototype()
    {
        using (TextReader stream = new StringReader(YamlReagentPrototype))
        {
            var yamlStream = new YamlStream();
            yamlStream.Load(stream);
            var document = yamlStream.Documents[0];
            var rootNode = (YamlSequenceNode)document.RootNode;
            var proto = (YamlMappingNode)rootNode[0];

            var defType = proto.GetNode("type").AsString();
            var serializationManager = IoCManager.Resolve<ISerializationManager>();
            serializationManager.Initialize();

            var reaction = serializationManager.Read<FuzzyReactionPrototype>(new MappingDataNode(proto));

            Assert.That(defType, Is.EqualTo("fuzzyReaction"));
            Assert.That(reaction.Name, Is.EqualTo("Pancake Batter"));
        }
    }

    private const string YamlReagentPrototype = @"- type: fuzzyReaction
  id: PancakeBatter
  name: Pancake Batter
  ";
}
