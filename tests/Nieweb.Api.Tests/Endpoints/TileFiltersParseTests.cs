using Nieweb.Api.Endpoints;
using Nieweb.Filters;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Unit tests for <see cref="ReportEndpoints.TryParseTileFilters"/>.
/// </summary>
public sealed class TileFiltersParseTests
{
    [Fact]
    public void NoFiltersArray_SucceedsWithNull()
    {
        Assert.True(ReportEndpoints.TryParseTileFilters(null, out var a, out var e1));
        Assert.Null(a);
        Assert.Null(e1);
        Assert.True(ReportEndpoints.TryParseTileFilters("", out var b, out _));
        Assert.Null(b);
        Assert.True(ReportEndpoints.TryParseTileFilters("{}", out var c, out _));
        Assert.Null(c);
        Assert.True(ReportEndpoints.TryParseTileFilters("{\"axis\":\"Defect\"}", out var d, out _));
        Assert.Null(d);
        Assert.True(ReportEndpoints.TryParseTileFilters("{\"filters\":[]}", out var e, out _));
        Assert.Null(e);
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

        Assert.True(ReportEndpoints.TryParseTileFilters(json, out var request, out var error));
        Assert.Null(error);
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

        Assert.True(ReportEndpoints.TryParseTileFilters(json, out var request, out _));
        Assert.NotNull(request);
        Assert.Equal(FilterField.PartNumber, request!.Clauses[0].Field);
        Assert.Equal(FilterOperator.NotLike, request.Clauses[0].Operator);
    }

    [Fact]
    public void NumericValuesAreAccepted()
    {
        const string json = """
            { "filters": [ { "field": "BoardNumber", "operator": "Between", "values": [1, 10] } ] }
            """;

        Assert.True(ReportEndpoints.TryParseTileFilters(json, out var request, out _));
        Assert.Equal(["1", "10"], request!.Clauses[0].Values);
    }

    [Fact]
    public void StructurallyInvalidRequest_ReturnsError()
    {
        const string json = """
            { "filters": [ { "field": "Defect", "operator": "Like", "values": ["x"] } ] }
            """;

        Assert.False(ReportEndpoints.TryParseTileFilters(json, out var request, out var error));
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void WrongArity_ReturnsError()
    {
        const string json = """
            { "filters": [ { "field": "BoardNumber", "operator": "Between", "values": [1] } ] }
            """;

        Assert.False(ReportEndpoints.TryParseTileFilters(json, out var request, out var error));
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void UnknownField_ReturnsError()
    {
        const string json = """
            { "filters": [
                { "field": "NotAField", "operator": "Equal", "values": ["x"] },
                { "field": "Package", "operator": "Equal", "values": ["BGA256"] }
            ] }
            """;

        Assert.False(ReportEndpoints.TryParseTileFilters(json, out var request, out var error));
        Assert.Null(request);
        Assert.Contains("filters[0]", error, StringComparison.Ordinal);
        Assert.Contains("NotAField", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FiltersNotArray_ReturnsError()
    {
        Assert.False(ReportEndpoints.TryParseTileFilters(
            """{ "filters": "nope" }""", out var request, out var error));
        Assert.Null(request);
        Assert.Equal("filters must be an array", error);
    }

    [Fact]
    public void UnknownOperator_ReturnsError()
    {
        const string json = """
            { "filters": [
                { "field": "PartNumber", "operator": "NotLike", "values": ["PN-B"] },
                { "field": "PartNumber", "operator": "Lke", "values": ["x"] }
            ] }
            """;

        Assert.False(ReportEndpoints.TryParseTileFilters(json, out var request, out var error));
        Assert.Null(request);
        Assert.Contains("filters[1]", error, StringComparison.Ordinal);
        Assert.Contains("Lke", error, StringComparison.Ordinal);
    }
}
