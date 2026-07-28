using Nieweb.Filters;

using Xunit;

namespace Nieweb.Reports.Tests.Filters;

public class FilterEvaluatorTests
{
    /// <summary>
    /// Minimal <see cref="IFilterRowValues"/> test double: a fixed map of
    /// field → tokens. Fields absent from the map return no tokens.
    /// </summary>
    private sealed class FakeRow(Dictionary<FilterField, string[]> values) : IFilterRowValues
    {
        public IReadOnlyCollection<string> GetValues(FilterField field)
            => values.TryGetValue(field, out var v) ? v : [];
    }

    private static FakeRow Row(FilterField field, params string[] tokens)
        => new(new Dictionary<FilterField, string[]> { [field] = tokens });

    private static FilterClause Clause(FilterField field, FilterOperator op, params string[] values)
        => new(field, op, [.. values]);

    // ---- Equal / Different (string) ---------------------------------------

    [Theory]
    [InlineData("BGA256", true)]
    [InlineData("bga256", true)] // ordinal-ignore-case
    [InlineData("QFN44", false)]
    public void Equal_String_IsCaseInsensitive(string rowValue, bool expected)
    {
        var row = Row(FilterField.Package, rowValue);
        var clause = Clause(FilterField.Package, FilterOperator.Equal, "BGA256");
        Assert.Equal(expected, FilterEvaluator.Matches(clause, row));
    }

    [Fact]
    public void Different_IsLogicalNegationOfEqual()
    {
        var match = Row(FilterField.Package, "BGA256");
        var noMatch = Row(FilterField.Package, "QFN44");
        var clause = Clause(FilterField.Package, FilterOperator.Different, "BGA256");
        Assert.False(FilterEvaluator.Matches(clause, match));
        Assert.True(FilterEvaluator.Matches(clause, noMatch));
    }

    [Fact]
    public void Different_MatchesWhenRowHasNoValue()
    {
        var empty = new FakeRow([]);
        var clause = Clause(FilterField.Package, FilterOperator.Different, "BGA256");
        Assert.True(FilterEvaluator.Matches(clause, empty));
    }

    [Fact]
    public void Equal_DoesNotMatchWhenRowHasNoValue()
    {
        var empty = new FakeRow([]);
        var clause = Clause(FilterField.Package, FilterOperator.Equal, "BGA256");
        Assert.False(FilterEvaluator.Matches(clause, empty));
    }

    // ---- In / NotIn -------------------------------------------------------

    [Theory]
    [InlineData("R12", true)]
    [InlineData("U7", true)]
    [InlineData("C3", false)]
    public void In_MatchesSetMembership(string rowValue, bool expected)
    {
        var row = Row(FilterField.ReferenceDesignator, rowValue);
        var clause = Clause(FilterField.ReferenceDesignator, FilterOperator.In, "R12", "U7");
        Assert.Equal(expected, FilterEvaluator.Matches(clause, row));
    }

    [Fact]
    public void NotIn_IsNegationOfIn_AndKeepsRowsWithNoValue()
    {
        var inSet = Row(FilterField.ReferenceDesignator, "R12");
        var outOfSet = Row(FilterField.ReferenceDesignator, "C3");
        var empty = new FakeRow([]);
        var clause = Clause(FilterField.ReferenceDesignator, FilterOperator.NotIn, "R12", "U7");
        Assert.False(FilterEvaluator.Matches(clause, inSet));
        Assert.True(FilterEvaluator.Matches(clause, outOfSet));
        Assert.True(FilterEvaluator.Matches(clause, empty));
    }

    // ---- Like / NotLike (substring, case-insensitive) ---------------------

    [Theory]
    [InlineData("MAIN-BOARD-A", "board", true)]
    [InlineData("MAIN-BOARD-A", "BOARD", true)]
    [InlineData("MAIN-BOARD-A", "panel", false)]
    public void Like_IsCaseInsensitiveSubstring(string rowValue, string needle, bool expected)
    {
        var row = Row(FilterField.PartNumber, rowValue);
        var clause = Clause(FilterField.PartNumber, FilterOperator.Like, needle);
        Assert.Equal(expected, FilterEvaluator.Matches(clause, row));
    }

    [Fact]
    public void NotLike_IsNegationOfLike()
    {
        var contains = Row(FilterField.PartNumber, "MAIN-BOARD-A");
        var missing = Row(FilterField.PartNumber, "SIDE-A");
        var clause = Clause(FilterField.PartNumber, FilterOperator.NotLike, "board");
        Assert.False(FilterEvaluator.Matches(clause, contains));
        Assert.True(FilterEvaluator.Matches(clause, missing));
    }

    // ---- Between / NotBetween (integer) -----------------------------------

    [Theory]
    [InlineData("5", true)]
    [InlineData("1", true)]  // inclusive lower
    [InlineData("10", true)] // inclusive upper
    [InlineData("0", false)]
    [InlineData("11", false)]
    public void Between_Integer_IsInclusive(string rowValue, bool expected)
    {
        var row = Row(FilterField.BoardNumber, rowValue);
        var clause = Clause(FilterField.BoardNumber, FilterOperator.Between, "1", "10");
        Assert.Equal(expected, FilterEvaluator.Matches(clause, row));
    }

    [Fact]
    public void NotBetween_IsNegationOfBetween()
    {
        var inside = Row(FilterField.BoardNumber, "5");
        var outside = Row(FilterField.BoardNumber, "42");
        var clause = Clause(FilterField.BoardNumber, FilterOperator.NotBetween, "1", "10");
        Assert.False(FilterEvaluator.Matches(clause, inside));
        Assert.True(FilterEvaluator.Matches(clause, outside));
    }

    // ---- <= / >= ----------------------------------------------------------

    [Theory]
    [InlineData(FilterOperator.LessThanOrEqual, "10", true)]
    [InlineData(FilterOperator.LessThanOrEqual, "11", false)]
    [InlineData(FilterOperator.GreaterThanOrEqual, "10", true)]
    [InlineData(FilterOperator.GreaterThanOrEqual, "9", false)]
    public void OrderedOperators_Integer(FilterOperator op, string rowValue, bool expected)
    {
        var row = Row(FilterField.BoardNumber, rowValue);
        var clause = Clause(FilterField.BoardNumber, op, "10");
        Assert.Equal(expected, FilterEvaluator.Matches(clause, row));
    }

    [Fact]
    public void OrderedOperators_NonNumericToken_FailsClosed()
    {
        // BoardNumber is an integer field; a non-numeric token can never
        // satisfy an ordered comparison (neither <= nor >=).
        var row = Row(FilterField.BoardNumber, "not-a-number");
        Assert.False(FilterEvaluator.Matches(
            Clause(FilterField.BoardNumber, FilterOperator.LessThanOrEqual, "10"), row));
        Assert.False(FilterEvaluator.Matches(
            Clause(FilterField.BoardNumber, FilterOperator.GreaterThanOrEqual, "10"), row));
    }

    [Fact]
    public void Ordered_String_ComparesAlphanumerically()
    {
        // Bar codes admit <= / >= in Vieweb (alphanumeric sort).
        var row = Row(FilterField.PanelBarcode, "B100");
        Assert.True(FilterEvaluator.Matches(
            Clause(FilterField.PanelBarcode, FilterOperator.GreaterThanOrEqual, "B0"), row));
        Assert.False(FilterEvaluator.Matches(
            Clause(FilterField.PanelBarcode, FilterOperator.LessThanOrEqual, "A9"), row));
    }

    // ---- set-membership fields with many tokens ---------------------------

    [Fact]
    public void Defect_MatchesWhenAnyOfSeveralTokensMatches()
    {
        // A single tested object can carry multiple defect bits; the row
        // exposes each as a token. "In {MISSING, BILLBOARD}" matches when
        // ANY token is in the set.
        var row = Row(FilterField.Defect, "MISALIGNMENT", "MISSING");
        var clause = Clause(FilterField.Defect, FilterOperator.In, "MISSING", "BILLBOARD");
        Assert.True(FilterEvaluator.Matches(clause, row));
    }

    [Fact]
    public void Defect_NotIn_ExcludesRowWhenAnyTokenMatches()
    {
        var row = Row(FilterField.Defect, "MISALIGNMENT", "MISSING");
        var clause = Clause(FilterField.Defect, FilterOperator.NotIn, "MISSING");
        Assert.False(FilterEvaluator.Matches(clause, row));
    }

    // ---- request-level AND ------------------------------------------------

    [Fact]
    public void Request_AndsAllClauses()
    {
        var row = new FakeRow(new Dictionary<FilterField, string[]>
        {
            [FilterField.Package] = ["BGA256"],
            [FilterField.ReferenceDesignator] = ["U7"],
        });

        var passesBoth = new FilterRequest(
        [
            Clause(FilterField.Package, FilterOperator.Equal, "BGA256"),
            Clause(FilterField.ReferenceDesignator, FilterOperator.In, "U7", "U8"),
        ]);
        var failsSecond = new FilterRequest(
        [
            Clause(FilterField.Package, FilterOperator.Equal, "BGA256"),
            Clause(FilterField.ReferenceDesignator, FilterOperator.Equal, "R1"),
        ]);

        Assert.True(FilterEvaluator.Matches(passesBoth, row));
        Assert.False(FilterEvaluator.Matches(failsSecond, row));
    }

    [Fact]
    public void Request_EmptyMatchesEverything()
    {
        var row = Row(FilterField.Package, "BGA256");
        Assert.True(FilterEvaluator.Matches(FilterRequest.Empty, row));
        Assert.True(FilterEvaluator.Matches(new FilterRequest(default), row));
    }

    // ---- fail-closed on malformed clause ----------------------------------

    [Fact]
    public void Clause_WithWrongArity_NeverMatches()
    {
        // Between requires two values; a one-value clause is malformed and
        // must fail closed rather than throw.
        var row = Row(FilterField.BoardNumber, "5");
        var malformed = new FilterClause(
            FilterField.BoardNumber, FilterOperator.Between, ["1"]);
        Assert.False(FilterEvaluator.Matches(malformed, row));
    }

    [Fact]
    public void Clause_WithDefaultValues_NeverMatches()
    {
        var row = Row(FilterField.Package, "BGA256");
        var malformed = new FilterClause(
            FilterField.Package, FilterOperator.Equal, default);
        Assert.False(FilterEvaluator.Matches(malformed, row));
    }
}
