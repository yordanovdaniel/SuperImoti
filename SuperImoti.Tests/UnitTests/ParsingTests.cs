using SuperImoti.Web.Data;
using SuperImoti.Web.Data.Import;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Tests.UnitTests;

public class SourceValueParserTests
{
    [Fact]
    public void CleanPhones_SplitsTwoNumbersTheScraperGluedTogether()
    {
        var phones = SourceValueParser.CleanPhones("08984692670886416259,08984692647");

        Assert.Equal("0898469267, 0886416259, 08984692647", phones);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("0888123456", "0888123456")]
    [InlineData("0888123456,0888123456", "0888123456")]
    public void CleanPhones_HandlesEmptyAndDuplicateValues(string? raw, string? expected) =>
        Assert.Equal(expected, SourceValueParser.CleanPhones(raw));

    [Theory]
    [InlineData("EUR", Currency.EUR)]
    [InlineData("лв.", Currency.EUR)] // imot.bg shows these prices in euro despite the label
    [InlineData("$", Currency.USD)]
    [InlineData(null, Currency.EUR)]
    public void ParseCurrency_TreatsOnlyDollarAsDifferentCurrency(string? raw, Currency expected) =>
        Assert.Equal(expected, SourceValueParser.ParseCurrency(raw));

    [Theory]
    [InlineData("830 -> 790", 830)]
    [InlineData("210000 -> 215000", 210000)]
    [InlineData("HTTP 404", null)]
    [InlineData(null, null)]
    public void ParseOldPrice_ReadsThePriceBeforeTheArrow(string? note, int? expected) =>
        Assert.Equal(expected is null ? null : (decimal)expected, SourceValueParser.ParseOldPrice(note));

    [Fact]
    public void ParseUtc_ReadsScraperTimestampsAsUtc()
    {
        var parsed = SourceValueParser.ParseUtc("2026-10-07 20:53:32");

        Assert.Equal(new DateTime(2026, 10, 7, 20, 53, 32, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Null(SourceValueParser.ParseUtc("not a date"));
    }

    [Theory]
    [InlineData("град София", "София")]
    [InlineData("област Перник", "област Перник")]
    [InlineData(null, "София")]
    public void CleanCity_RemovesTheCityPrefix(string? raw, string expected) =>
        Assert.Equal(expected, SourceValueParser.CleanCity(raw));

    [Fact]
    public void BuildTitle_DescribesTheListing()
    {
        Assert.Equal("2-стаен за продажба, Надежда 3", SourceValueParser.BuildTitle("2-стаен", DealKind.Sale, "Надежда 3"));
        Assert.Equal("Гараж под наем, Редута", SourceValueParser.BuildTitle("Гараж", DealKind.Rent, "Редута"));
    }

    [Theory]
    [InlineData("889489ffdf9ca24fc71e4e6913a42a4b75271a4f39a102d7f1881f1b5122f552", true)]
    [InlineData("889489FFDF9CA24FC71E4E6913A42A4B75271A4F39A102D7F1881F1B5122F552", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData(null, false)]
    public void IsContentHash_AcceptsOnlyLowerCaseSha256(string? value, bool expected) =>
        Assert.Equal(expected, SourceValueParser.IsContentHash(value));
}

public class PropertyTypeCatalogTests
{
    [Theory]
    [InlineData("2-СТАЕН", "2-стаен")]
    [InlineData("ПРОМ. ПОМЕЩЕНИЕ", "Пром. помещение")]
    [InlineData("ФРИЗЬОРСКИ/КОЗМЕТИЧЕН САЛОН", "Фризьорски/козметичен салон")]
    public void ToDisplayName_LowerCasesAllButTheFirstLetter(string raw, string expected) =>
        Assert.Equal(expected, PropertyTypeCatalog.ToDisplayName(raw));

    [Fact]
    public void Classify_GroupsKnownTypesAndPutsUnknownLast()
    {
        Assert.Equal(PropertyCategory.Apartment, PropertyTypeCatalog.Classify("2-стаен").Category);
        Assert.Equal(PropertyCategory.Parking, PropertyTypeCatalog.Classify("Гараж").Category);
        Assert.Equal(PropertyCategory.Land, PropertyTypeCatalog.Classify("Парцел").Category);

        var unknown = PropertyTypeCatalog.Classify("Космодрум");
        Assert.Equal(PropertyCategory.Other, unknown.Category);
        Assert.True(unknown.SortOrder > PropertyTypeCatalog.Classify("Паркинг").SortOrder);
    }
}

public class SearchTextBuilderTests
{
    [Fact]
    public void Build_JoinsAndLowerCasesCyrillic() =>
        Assert.Equal("2-стаен в надежда 3 асансьор", SearchTextBuilder.Build("2-стаен в", null, "Надежда 3", " ", "АСАНСЬОР"));

    [Fact]
    public void ToFtsQuery_QuotesEveryWordAsPrefix() =>
        Assert.Equal("\"лозен\"* \"2-стаен\"*", SearchTextBuilder.ToFtsQuery("  Лозен   2-стаен "));

    [Fact]
    public void ToFtsQuery_NeutralisesQuotesAndSkipsPunctuation()
    {
        Assert.Equal("\"a\"\"b\"*", SearchTextBuilder.ToFtsQuery("a\"b - *"));
        Assert.Null(SearchTextBuilder.ToFtsQuery("  - * \" "));
        Assert.Null(SearchTextBuilder.ToFtsQuery(null));
    }
}
