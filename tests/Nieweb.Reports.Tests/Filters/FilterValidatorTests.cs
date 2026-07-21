using System.Collections.Immutable;

using Nieweb.Filters;

using Xunit;

namespace Nieweb.Reports.Tests.Filters;

public class FilterOperatorMetadataTests
{
    [Theory]
    [InlineData(FilterOperator.Equal, FilterOperatorArity.Single)]
    [InlineData(FilterOperator.Different, FilterOperatorArity.Single)]
    [InlineData(FilterOperator.Like, FilterOperatorArity.Single)]
    [InlineData(FilterOperator.NotLike, FilterOperatorArity.Single)]
    [InlineData(FilterOperator.LessThanOrEqual, FilterOperatorArity.Single)]
    [InlineData(FilterOperator.GreaterThanOrEqual, FilterOperatorArity.Single)]
    [InlineData(FilterOperator.In, FilterOperatorArity.List)]
    [InlineData(FilterOperator.NotIn, FilterOperatorArity.List)]
    [InlineData(FilterOperator.Between, FilterOperatorArity.Range)]
    [InlineData(FilterOperator.NotBetween, FilterOperatorArity.Range)]
    public void GetArity_ReturnsExpectedForEveryOperator(FilterOperator op, FilterOperatorArity expected)
    {
        Assert.Equal(expected, FilterOperatorMetadata.GetArity(op));
    }

    [Theory]
    [InlineData(FilterOperator.Like, FilterValueKind.String, true)]
    [InlineData(FilterOperator.Like, FilterValueKind.Integer, false)]
    [InlineData(FilterOperator.NotLike, FilterValueKind.Decimal, false)]
    [InlineData(FilterOperator.NotLike, FilterValueKind.DateTimeUtc, false)]
    [InlineData(FilterOperator.Equal, FilterValueKind.Boolean, true)]
    [InlineData(FilterOperator.Between, FilterValueKind.Boolean, false)]
    [InlineData(FilterOperator.GreaterThanOrEqual, FilterValueKind.Integer, true)]
    public void SupportsValueKind_EnforcesLikeOnStringsAndBoolEqualityOnly(
        FilterOperator op, FilterValueKind kind, bool expected)
    {
        Assert.Equal(expected, FilterOperatorMetadata.SupportsValueKind(op, kind));
    }
}

public class FilterFieldMetadataTests
{
    // Vieweb §3.1.2 rows verbatim: each X in the printed operator
    // table becomes a member of the expected set.
    public static TheoryData<FilterField, FilterOperator[]> AllowedByField()
    {
        var stringSetOnly = new[]
        {
            FilterOperator.Equal, FilterOperator.Different,
            FilterOperator.In, FilterOperator.NotIn,
            FilterOperator.Like, FilterOperator.NotLike,
        };
        var ordered = new[]
        {
            FilterOperator.Equal, FilterOperator.Different,
            FilterOperator.In, FilterOperator.NotIn,
            FilterOperator.Between, FilterOperator.NotBetween,
            FilterOperator.LessThanOrEqual, FilterOperator.GreaterThanOrEqual,
        };
        var setMembership = new[]
        {
            FilterOperator.Equal, FilterOperator.Different,
            FilterOperator.In, FilterOperator.NotIn,
        };
        var fullTen = new[]
        {
            FilterOperator.Equal, FilterOperator.Different,
            FilterOperator.In, FilterOperator.NotIn,
            FilterOperator.Between, FilterOperator.NotBetween,
            FilterOperator.Like, FilterOperator.NotLike,
            FilterOperator.LessThanOrEqual, FilterOperator.GreaterThanOrEqual,
        };
        var equalOnly = new[] { FilterOperator.Equal };

        return new TheoryData<FilterField, FilterOperator[]>
        {
            { FilterField.BoardNumber, ordered },
            { FilterField.PnpMachine, stringSetOnly },
            { FilterField.PnpSubElement1, stringSetOnly },
            { FilterField.PnpSubElement2, stringSetOnly },
            { FilterField.PnpSubElement3, stringSetOnly },
            { FilterField.PnpSubElement4, stringSetOnly },
            { FilterField.PartNumber, stringSetOnly },
            { FilterField.InspectedObject, setMembership },
            { FilterField.Product, stringSetOnly },
            { FilterField.Package, stringSetOnly },
            { FilterField.RepairStatus, setMembership },
            { FilterField.RepairComment, stringSetOnly },
            { FilterField.ReferenceDesignator, stringSetOnly },
            { FilterField.Defect, setMembership },
            { FilterField.PanelBarcode, fullTen },
            { FilterField.BoardIdCode, fullTen },
            { FilterField.AoiMachine, stringSetOnly },
            { FilterField.PanelStatus, equalOnly },
            { FilterField.BoardStatus, equalOnly },
        };
    }

    [Theory]
    [MemberData(nameof(AllowedByField))]
    public void GetAllowedOperators_MatchesVieweb312Table(
        FilterField field, FilterOperator[] expected)
    {
        var actual = FilterFieldMetadata.GetAllowedOperators(field);
        Assert.Equal(expected.ToHashSet(), actual.ToHashSet());
    }

    [Fact]
    public void GetAllowedOperators_UnknownField_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FilterFieldMetadata.GetAllowedOperators((FilterField)999));
    }

    [Fact]
    public void GetValueKind_BoardNumberIsInteger_BarcodesAreStrings()
    {
        Assert.Equal(FilterValueKind.Integer, FilterFieldMetadata.GetValueKind(FilterField.BoardNumber));
        Assert.Equal(FilterValueKind.String, FilterFieldMetadata.GetValueKind(FilterField.PanelBarcode));
        Assert.Equal(FilterValueKind.String, FilterFieldMetadata.GetValueKind(FilterField.BoardIdCode));
    }
}

public class FilterValidatorTests
{
    [Fact]
    public void Validate_EmptyRequest_IsValid()
    {
        var result = FilterValidator.Validate(FilterRequest.Empty);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EqualOnPanelStatus_Ok()
    {
        var clause = new FilterClause(
            FilterField.PanelStatus, FilterOperator.Equal,
            ImmutableArray.Create("0"));
        var result = FilterValidator.Validate(clause);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_BetweenOnPanelStatus_Rejected()
    {
        var clause = new FilterClause(
            FilterField.PanelStatus, FilterOperator.Between,
            ImmutableArray.Create("0", "2"));
        var result = FilterValidator.Validate(clause);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_LikeOnBoardNumber_RejectedForValueKind()
    {
        var clause = new FilterClause(
            FilterField.BoardNumber, FilterOperator.Like,
            ImmutableArray.Create("1"));
        var result = FilterValidator.Validate(clause);
        Assert.False(result.IsValid);
        // Two failures: operator not in allowed set for BoardNumber
        // (Vieweb table lists only ordered ops for BoardNumber) AND
        // Like doesn't accept integer kinds.
        Assert.True(result.Errors.Length >= 1);
    }

    [Fact]
    public void Validate_InOnPartNumber_RequiresAtLeastOneValue()
    {
        var clause = new FilterClause(
            FilterField.PartNumber, FilterOperator.In,
            ImmutableArray<string>.Empty);
        var result = FilterValidator.Validate(clause);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Key == "Values");
    }

    [Fact]
    public void Validate_BetweenOnBoardNumber_RequiresExactlyTwoValues()
    {
        var singleValue = new FilterClause(
            FilterField.BoardNumber, FilterOperator.Between,
            ImmutableArray.Create("1"));
        var singleResult = FilterValidator.Validate(singleValue);
        Assert.False(singleResult.IsValid);

        var threeValues = new FilterClause(
            FilterField.BoardNumber, FilterOperator.Between,
            ImmutableArray.Create("1", "2", "3"));
        var threeResult = FilterValidator.Validate(threeValues);
        Assert.False(threeResult.IsValid);

        var okValues = new FilterClause(
            FilterField.BoardNumber, FilterOperator.Between,
            ImmutableArray.Create("1", "5"));
        Assert.True(FilterValidator.Validate(okValues).IsValid);
    }

    [Fact]
    public void Validate_EqualOnBoardNumber_RejectsNonInteger()
    {
        var clause = new FilterClause(
            FilterField.BoardNumber, FilterOperator.Equal,
            ImmutableArray.Create("abc"));
        var result = FilterValidator.Validate(clause);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Key == "Values[0]" && e.Message.Contains("integer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_LikeOnBarcode_AcceptsAnyString()
    {
        var clause = new FilterClause(
            FilterField.PanelBarcode, FilterOperator.Like,
            ImmutableArray.Create("2025W47"));
        var result = FilterValidator.Validate(clause);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownField_RejectedBeforeArityChecks()
    {
        var clause = new FilterClause(
            (FilterField)999, FilterOperator.Equal,
            ImmutableArray.Create("x"));
        var result = FilterValidator.Validate(clause);
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Field", result.Errors[0].Key);
    }

    [Fact]
    public void Validate_UnknownOperator_Rejected()
    {
        var clause = new FilterClause(
            FilterField.PartNumber, (FilterOperator)999,
            ImmutableArray.Create("x"));
        var result = FilterValidator.Validate(clause);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Key == "Operator");
    }

    [Fact]
    public void Validate_Request_AggregatesErrorsWithClauseIndex()
    {
        var request = new FilterRequest(ImmutableArray.Create(
            new FilterClause(FilterField.PartNumber, FilterOperator.Equal,
                ImmutableArray.Create("ok")),
            new FilterClause(FilterField.PanelStatus, FilterOperator.Between,
                ImmutableArray.Create("0", "2"))));

        var result = FilterValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.All(result.Errors, e => Assert.StartsWith("[1].", e.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Request_AllValid_ReturnsSuccess()
    {
        var request = new FilterRequest(ImmutableArray.Create(
            new FilterClause(FilterField.PartNumber, FilterOperator.In,
                ImmutableArray.Create("R0402", "R0603")),
            new FilterClause(FilterField.BoardNumber, FilterOperator.Between,
                ImmutableArray.Create("1", "8")),
            new FilterClause(FilterField.PanelBarcode, FilterOperator.Like,
                ImmutableArray.Create("2025W47"))));

        var result = FilterValidator.Validate(request);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
