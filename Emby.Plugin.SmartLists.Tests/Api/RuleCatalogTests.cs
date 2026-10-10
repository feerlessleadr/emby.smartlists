using System.Text.Json;
using Emby.Plugin.SmartLists.Api.Controllers;
using Emby.Plugin.SmartLists.Core.QueryEngine;

namespace Emby.Plugin.SmartLists.Tests.Api;

public class RuleCatalogTests
{
    private static JsonElement Catalog() => JsonSerializer.SerializeToElement(RuleCatalog.Build());

    [Fact]
    public void Info_ReportsTheNameVersionAndApiVersion()
    {
        var info = JsonSerializer.SerializeToElement(RuleCatalog.BuildInfo("1.2.3.0"));

        Assert.Equal("SmartLists", info.GetProperty("Name").GetString());
        Assert.Equal("1.2.3.0", info.GetProperty("PluginVersion").GetString());
        Assert.Equal(RuleCatalog.ApiVersion, info.GetProperty("ApiVersion").GetInt32());
    }

    [Fact]
    public void Catalog_ListsEveryRuleFieldWithATypeAndOperators()
    {
        var fields = Catalog().GetProperty("Fields").EnumerateArray().ToList();
        var names = fields.Select(f => f.GetProperty("Name").GetString()).ToHashSet();

        // Everything the rule builder in the web page offers is in the catalog (ItemType is internal).
        foreach (var category in Enum.GetValues<FieldCategory>().Where(c => c != FieldCategory.SimilarityComparison))
        {
            foreach (var field in FieldRegistry.GetFieldsByCategory(category).Where(f => f.Name != "ItemType"))
            {
                Assert.Contains(field.Name, names);
            }
        }

        foreach (var field in fields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("Label").GetString()), field.ToString());
            Assert.False(string.IsNullOrWhiteSpace(field.GetProperty("Type").GetString()), field.ToString());
            Assert.True(field.GetProperty("Operators").GetArrayLength() > 0, field.GetProperty("Name").GetString());
        }
    }

    [Fact]
    public void Catalog_DescribesTypesAndFixedValuesForTheFieldsAnAppNeeds()
    {
        var fields = Catalog().GetProperty("Fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("Name").GetString()!);

        Assert.Equal("Boolean", fields["NextUnwatched"].GetProperty("Type").GetString());
        Assert.True(fields["NextUnwatched"].GetProperty("UserSpecific").GetBoolean());
        Assert.Equal("Text", fields["LibraryName"].GetProperty("Type").GetString());
        Assert.Equal("Numeric", fields["ProductionYear"].GetProperty("Type").GetString());
        Assert.Equal("Date", fields["DateCreated"].GetProperty("Type").GetString());

        var playback = fields["PlaybackStatus"].GetProperty("Values").EnumerateArray()
            .Select(v => v.GetProperty("Value").GetString()).ToList();
        Assert.Equal(["Played", "InProgress", "Unplayed"], playback);

        // A free-text field has no fixed values.
        Assert.Equal(JsonValueKind.Null, fields["Name"].GetProperty("Values").ValueKind);
    }

    [Fact]
    public void Catalog_ReferencedOperatorsAreAllDefined()
    {
        var catalog = Catalog();
        var defined = catalog.GetProperty("Operators").EnumerateArray()
            .Select(o => o.GetProperty("Value").GetString()).ToHashSet();

        foreach (var field in catalog.GetProperty("Fields").EnumerateArray())
        {
            foreach (var op in field.GetProperty("Operators").EnumerateArray())
            {
                Assert.Contains(op.GetString(), defined);
            }
        }
    }

    [Fact]
    public void Catalog_CarriesTheChoicesForSortsAndDates()
    {
        var catalog = Catalog();

        Assert.Equal(RuleCatalog.ApiVersion, catalog.GetProperty("ApiVersion").GetInt32());
        Assert.Contains("Episode", catalog.GetProperty("MediaTypes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(["hours", "days", "weeks", "months", "years"],
            catalog.GetProperty("RelativeDateUnits").EnumerateArray().Select(u => u.GetProperty("Value").GetString()));
        Assert.Equal(7, catalog.GetProperty("Weekdays").GetArrayLength());
        Assert.Contains("Most Recently Watched Round Robin",
            catalog.GetProperty("Sorts").EnumerateArray().Select(s => s.GetProperty("Value").GetString()));
        Assert.Contains("OnAllChanges",
            catalog.GetProperty("AutoRefreshModes").EnumerateArray().Select(m => m.GetProperty("Value").GetString()));
    }
}
