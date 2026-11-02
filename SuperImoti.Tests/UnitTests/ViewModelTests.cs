using System.ComponentModel.DataAnnotations;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.Services;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Tests.UnitTests;

public class ListingFilterTests
{
    [Fact]
    public void ToQueryString_KeepsFiltersAndLeavesOutDefaults()
    {
        var filter = new ListingFilter
        {
            Q = "Лозенец",
            Deal = DealKind.Rent,
            District = 12,
            MinPrice = 300.5m,
            Features = [3, 7, 3],
            Photos = true,
            Sort = ListingSort.Recommended,
            Size = ListingFilter.DefaultPageSize,
        };

        var query = filter.ToQueryString(page: 2);

        Assert.Equal(
            "?q=%D0%9B%D0%BE%D0%B7%D0%B5%D0%BD%D0%B5%D1%86&deal=Rent&district=12&minPrice=300.5&features=3&features=7&photos=true&page=2",
            query);
    }

    [Fact]
    public void ToQueryString_IsEmptyWithoutFilters() => Assert.Equal(string.Empty, new ListingFilter().ToQueryString());

    [Theory]
    [InlineData(5, 24)]
    [InlineData(48, 48)]
    public void PageSize_AcceptsOnlyKnownSizes(int requested, int expected) =>
        Assert.Equal(expected, new ListingFilter { Size = requested }.PageSize);

    [Fact]
    public void Validate_ReportsMinimumAboveMaximum()
    {
        var filter = new ListingFilter { MinPrice = 500, MaxPrice = 100, MinArea = 80, MaxArea = 40 };

        var errors = Validate(filter);

        Assert.Contains(errors, e => e.ErrorMessage!.Contains("цена"));
        Assert.Contains(errors, e => e.ErrorMessage!.Contains("площ"));
    }

    [Fact]
    public void SortKey_FallsBackToRecommended() =>
        Assert.Equal(ListingSort.Recommended, new ListingFilter { Sort = "drop table" }.SortKey);

    internal static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}

public class ListingFormModelTests
{
    private static ListingFormModel ValidModel() => new()
    {
        Title = "Светъл тристаен апартамент",
        DealKind = DealKind.Sale,
        PropertyTypeId = 1,
        DistrictId = 1,
        Price = 150000,
        Area = 85,
        Description = "Тухлена сграда, южно изложение, близо до метро.",
        ContactPhone = "0888 123 456",
    };

    [Fact]
    public void ValidModel_HasNoErrors() => Assert.Empty(ListingFilterTests.Validate(ValidModel()));

    [Fact]
    public void MissingRequiredFields_AreReportedInBulgarian()
    {
        var errors = ListingFilterTests.Validate(new ListingFormModel());

        Assert.Contains(errors, e => e.ErrorMessage == "Въведете заглавие.");
        Assert.Contains(errors, e => e.ErrorMessage == "Въведете площ.");
        Assert.Contains(errors, e => e.ErrorMessage == "Изберете квартал.");
    }

    [Fact]
    public void PriceIsRequiredUnlessOnRequest()
    {
        var model = ValidModel();
        model.Price = null;
        Assert.Contains(ListingFilterTests.Validate(model), e => e.MemberNames.Contains(nameof(ListingFormModel.Price)));

        model.PriceOnRequest = true;
        Assert.Empty(ListingFilterTests.Validate(model));
    }

    [Fact]
    public void FloorCannotBeAboveTheBuilding()
    {
        var model = ValidModel();
        model.Floor = 9;
        model.TotalFloors = 6;

        Assert.Contains(ListingFilterTests.Validate(model), e => e.MemberNames.Contains(nameof(ListingFormModel.Floor)));
    }

    [Theory]
    [InlineData("0888<script>", false)]
    [InlineData("+359 888 123 456", true)]
    [InlineData("0888123456, 0899123456", true)]
    public void ContactPhone_AllowsOnlyPhoneCharacters(string phone, bool valid)
    {
        var model = ValidModel();
        model.ContactPhone = phone;

        Assert.Equal(valid, ListingFilterTests.Validate(model).Count == 0);
    }
}

public class ListingCsvExporterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("Лозенец", "Лозенец")]
    [InlineData("ул. Шипка, 12", "\"ул. Шипка, 12\"")]
    [InlineData("кв. \"Изток\"", "\"кв. \"\"Изток\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("-5", "-5")]
    public void Escape_QuotesWhenNeededAndBlocksFormulas(string? value, string expected) =>
        Assert.Equal(expected, ListingCsvExporter.Escape(value));
}

public class PagingTests
{
    [Theory]
    [InlineData(0, 100, 24, 1)]
    [InlineData(3, 100, 24, 3)]
    [InlineData(99, 100, 24, 5)]
    [InlineData(4, 0, 24, 1)]
    public void ClampPage_KeepsThePageInRange(int requested, int total, int size, int expected) =>
        Assert.Equal(expected, PagedResult<int>.ClampPage(requested, total, size));

    [Fact]
    public void Window_ShowsNeighboursAndGaps()
    {
        var pager = new PaginationViewModel { Page = 10, TotalPages = 20, PageUrl = p => $"?page={p}" };

        Assert.Equal(new int?[] { 1, null, 8, 9, 10, 11, 12, null, 20 }, pager.Window().ToArray());
    }

    [Fact]
    public void Fmt_UsesBulgarianNumberFormat()
    {
        Assert.Matches(@"^135\s000 €$", Fmt.Money(135000m));
        Assert.Equal("При запитване", Fmt.Money(null));
        Assert.Equal("Партер от 4", Fmt.Floor(0, 4));
        Assert.Matches(@"^2\s045 €/м²$", Fmt.PerSqm(2045m));
    }
}
