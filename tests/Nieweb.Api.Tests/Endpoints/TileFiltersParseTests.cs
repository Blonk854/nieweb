using Nieweb.Api.Endpoints;
using Nieweb.Filters;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Unit tests for <see cref="ReportEndpoints.ParseTileFilters"/> — the
/// server-side parser for the per-tile Vieweb-style generic operator
/// filter carried in a tile's <c>ConfigJson</c> <c>filters</c> array.
/// The JSON shape must match the SPA writer in
/// <c>src/Nieweb.Web/src/api/filters.ts</c>.
/// </summary>
public sealed class TileFiltersParseTests
{
    [Fact]
    public void NoFiltersArray_ReturnsNull()
    {
        Assert.Null(ReportEndpoints.ParseTileFilters(null));
        Assert.Null(ReportEndpoints.ParseTileFilters(""));
        Assert.Null(ReportEndpoints.ParseTileFilters("{}"));
        Assert.Null(ReportEndpoints.ParseTileFilters("{\"axis\":\"Defect\"}"));
    }

    [Fact]
    public void ParsesFieldOperatorValues()
    {
        const string json = """
            { "filters": [
                { "field": "PartNumber", "operator": "NotLike", "values": ["PN-B"] },
                { "field": "ReferenceDesignator", "operator": "In", "values": ["R1", "U7"] }
            ] }
            """;

        var request = ReportEndpoints.ParseTileFilters(json);

        Assert.NotNull(request);
        Assert.Equal(2, request!.Clauses.Length);

        var first = request.Clauses[0];
        Assert.Equal(FilterField.PartNumber, first.Field);
        Assert.Equal(FilterOperator.NotLike, first.Operator);
        Assert.Equal(["PN-B"], first.Values);

        var second = request.Clauses[1];
        Assert.Equal(FilterField.ReferenceDesignator, second.Field);
        Assert.Equal(FilterOperator.In, second.Operator);
        Assert.Equal(["R1", "U7"], second.Values);
    }

    [Fact]
    public void EnumNamesAreCaseInsensitive()
    {
        const string json = """
            { "filters": [ { "field": "partnumber", "operator": "notlike", "values": ["x"] } ] }
            """;

        var request = ReportEndpoints.ParseTileFilters(json);

        Assert.NotNull(request);
        Assert.Equal(FilterField.PartNumber, request!.Clauses[0].Field);
        Assert.Equal(FilterOperator.NotLike, request.Clauses[0].Operator);
    }

    [Fact]
    public void NumericValuesAreAccepted()
    {
        // BoardNumber is an integer field; JSON numbers are read verbatim.
        const string json = """
            { "filters": [ { "field": "BoardNumber", "operator": "Between", "values": [1, 10] } ] }
            """;

        var request = ReportEndpoints.ParseTileFilters(json);

        Assert.NotNull(request);
        Assert.Equal(["1", "10"], request!.Clauses[0].Values);
    }

    [Fact]
    public void StructurallyInvalidRequest_ReturnsNull()
    {
        // "Like" is not allowed on the Defect field (set-membership only),
        // so FilterValidator rejects it and the parser returns null.
        const string json = """
            { "filters": [ { "field": "Defect", "operator": "Like", "values": ["x"] } ] }
            """;

        Assert.Null(ReportEndpoints.ParseTileFilters(json));
    }

    [Fact]
    public void WrongArity_ReturnsNull()
    {
        // Between requires exactly two values.
        const string json = """
            { "filters": [ { "field": "BoardNumber", "operator": "Between", "values": [1] } ] }
            """;

        Assert.Null(ReportEndpoints.ParseTileFilters(json));
    }

    [Fact]
    public void UnknownFieldClauseIsSkipped_RemainingKept()
    {
        const string json = """
            { "filters": [
                { "field": "NotAField", "operator": "Equal", "values": ["x"] },
                { "field": "Package", "operator": "Equal", "values": ["BGA256"] }
            ] }
            """;

        var request = ReportEndpoints.ParseTileFilters(json);

        Assert.NotNull(request);
        Assert.Single(request!.Clauses);
        Assert.Equal(FilterField.Package, request.Clauses[0].Field);
    }
}
