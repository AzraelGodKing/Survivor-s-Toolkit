namespace SurvivorsToolkit.Tests;

public class TweakTests
{
    const string Items = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <items>
            <item name="resourceWood" count="1">
                <property name="Stacknumber" value="500" />
                <property name="EconomicValue" value="1" />
            </item>
            <item name="gunPistol">
                <property name="Stacknumber" value="1" />
            </item>
            <item>
                <property name="Nameless" value="1" />
            </item>
        </items>
        """;

    [Fact]
    public void ReadsTheThingsAndWhatTheyHaveNow()
    {
        var things = Tweaks.Things(Items);
        Assert.Equal(new[] { "resourceWood", "gunPistol" }, things.Select(t => t.Name));
        var wood = things[0];
        Assert.Equal("item", wood.Element);
        Assert.Equal("500", wood.Properties.Single(p => p.Name == "Stacknumber").Value);
        Assert.Equal(("count", "1"), wood.Attributes.Single());
        Assert.Equal("items", Tweaks.Root(Items));
    }

    [Fact]
    public void SaysWhenTheFileIsntXml() => Assert.Throws<FormatException>(() => Tweaks.Things("<items><item"));

    [Fact]
    public void BuildsTheXpathOfOneThingsProperty()
    {
        var t = Tweaks.SetProperty("items.xml", "items", "item", "resourceWood", "Stacknumber", "5000", "500");
        Assert.Equal("/items/item[@name='resourceWood']/property[@name='Stacknumber']/@value", t.Xpath);
        Assert.Equal("resourceWood: Stacknumber = 5000 (was 500)", t.Describe);
    }

    [Fact]
    public void BuildsTheXpathOfEveryThingInTheFile()
    {
        var t = Tweaks.SetProperty("items.xml", "items", "item", null, "Stacknumber", "5000");
        Assert.Equal("/items/item/property[@name='Stacknumber']/@value", t.Xpath);
        Assert.Equal("every item: Stacknumber = 5000", t.Describe);
    }

    [Fact]
    public void BuildsTheXpathOfAnAttribute() =>
        Assert.Equal("/items/item[@name='resourceWood']/@count",
            Tweaks.SetAttribute("items.xml", "items", "item", "resourceWood", "count", "2").Xpath);

    [Fact]
    public void WritesAConfigThatTheGamesPatcherReads()
    {
        var tweaks = new[]
        {
            Tweaks.SetProperty("items.xml", "items", "item", "resourceWood", "Stacknumber", "5000", "500"),
            Tweaks.SetProperty("blocks.xml", "blocks", "block", null, "MaxDamage", "1000"),
        };
        var xml = Tweaks.Patch("items.xml", tweaks);
        Assert.Contains("""<set xpath="/items/item[@name='resourceWood']/property[@name='Stacknumber']/@value">5000</set>""", xml);
        Assert.DoesNotContain("blocks", xml);
        // It is the shape the game's XML loader wants, and its own patches read back.
        var patches = ModAnalysis.ReadPatches("items.xml", xml);
        Assert.Equal("set", Assert.Single(patches).Op);
    }

    [Fact]
    public void MakesAModFolderWithAConfigPerFile()
    {
        using var g = new FakeGame();
        var tweaks = new[]
        {
            Tweaks.SetProperty("items.xml", "items", "item", "resourceWood", "Stacknumber", "5000", "500"),
            Tweaks.SetProperty("blocks.xml", "blocks", "block", null, "MaxDamage", "1000"),
        };
        var folder = Tweaks.Build(g.Install.GameMods, "My tweaks", "1.0", "Azrael", tweaks);
        Assert.True(File.Exists(Path.Combine(folder, "Config", "items.xml")));
        Assert.True(File.Exists(Path.Combine(folder, "Config", "blocks.xml")));
        var info = ModInfo.Load(Path.Combine(folder, ModInfo.FileName));
        Assert.Equal("My tweaks", info.Name);
        Assert.Equal("Azrael", info.Author);
        // The game reads it as a mod, and its patches check out.
        var mod = ModLibrary.Scan(g.Install).Mods.Single(m => m.FolderName == "My tweaks");
        var (patches, broken) = new ModAnalysis(null).XmlOf(mod);
        Assert.Empty(broken);
        Assert.Equal(2, patches.Count);
    }

    [Fact]
    public void WontWriteOverAModThatIsAlreadyThere()
    {
        using var g = new FakeGame();
        var tweaks = new[] { Tweaks.SetProperty("items.xml", "items", "item", "a", "b", "c") };
        Tweaks.Build(g.Install.GameMods, "My tweaks", "1.0", "Azrael", tweaks);
        Assert.Throws<IOException>(() => Tweaks.Build(g.Install.GameMods, "My tweaks", "1.0", "Azrael", tweaks));
    }

    [Fact]
    public void NeedsAtLeastOneTweak() =>
        Assert.Throws<ArgumentException>(() => Tweaks.Build(Path.GetTempPath(), "x", "1.0", "a", Array.Empty<Tweak>()));

    [Fact]
    public void ListsTheGamesOwnConfigFiles()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Install.ConfigFolder);
        File.WriteAllText(Path.Combine(g.Install.ConfigFolder, "items.xml"), Items);
        File.WriteAllText(Path.Combine(g.Install.ConfigFolder, "blocks.xml"), "<blocks />");
        File.WriteAllText(Path.Combine(g.Install.ConfigFolder, "notes.txt"), "not xml");
        Assert.Equal(new[] { "blocks.xml", "items.xml" }, Tweaks.ConfigFiles(g.Install));
        Assert.Equal("resourceWood", Tweaks.Read(g.Install, "items.xml")[0].Name);
    }
}
