using LangBoost;
using Xunit;

namespace LangBoost.Tests;

/// <summary>
/// Regression tests for GeminiClient response parsing. The API returns the requested
/// { original, traducao } object as a JSON string inside candidates[0].content.parts[0].text.
/// </summary>
public class GeminiParseTests
{
    private static string Envelope(string innerJson)
    {
        // Builds the Gemini generateContent envelope with the given inner text payload.
        string escaped = System.Text.Json.JsonSerializer.Serialize(innerJson);
        return $$"""
        { "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }
        """;
    }

    [Fact]
    public void ParseResult_ValidResponse_ParsesBothFields()
    {
        var json = Envelope("{\"original\":\"Hello world\",\"traducao\":\"Olá mundo\"}");

        var result = GeminiClient.ParseResult(json);

        Assert.Equal("Hello world", result.Original);
        Assert.Equal("Olá mundo", result.Traducao);
    }

    [Fact]
    public void ParseResult_TrimsWhitespaceAroundValues()
    {
        var json = Envelope("{\"original\":\"  Hi  \",\"traducao\":\"\\n Oi \\t\"}");

        var result = GeminiClient.ParseResult(json);

        Assert.Equal("Hi", result.Original);
        Assert.Equal("Oi", result.Traducao);
    }

    [Fact]
    public void ParseResult_EmptyCandidatesArray_ReturnsEmpty()
    {
        var result = GeminiClient.ParseResult("{ \"candidates\": [] }");

        Assert.Equal("", result.Original);
        Assert.Equal("", result.Traducao);
    }

    [Fact]
    public void ParseResult_MissingCandidatesProperty_ReturnsEmpty()
    {
        var result = GeminiClient.ParseResult("{ \"foo\": 1 }");

        Assert.Equal("", result.Original);
        Assert.Equal("", result.Traducao);
    }

    [Fact]
    public void ParseResult_EmptyText_ReturnsEmpty()
    {
        var json = Envelope("   ");

        var result = GeminiClient.ParseResult(json);

        Assert.Equal("", result.Original);
        Assert.Equal("", result.Traducao);
    }

    [Fact]
    public void ParseResult_MissingInnerFields_DefaultToEmpty()
    {
        var json = Envelope("{\"original\":\"only original\"}");

        var result = GeminiClient.ParseResult(json);

        Assert.Equal("only original", result.Original);
        Assert.Equal("", result.Traducao);
    }

    [Fact]
    public void ExtractError_WithErrorMessage_ReturnsMessage()
    {
        var json = "{ \"error\": { \"code\": 400, \"message\": \"API key not valid\" } }";

        Assert.Equal("API key not valid", GeminiClient.ExtractError(json));
    }

    [Fact]
    public void ExtractError_NonJsonBody_ReturnsRawBody()
    {
        var body = "Service Unavailable";

        Assert.Equal(body, GeminiClient.ExtractError(body));
    }

    [Fact]
    public void ExtractError_JsonWithoutErrorMessage_ReturnsRawBody()
    {
        var body = "{ \"something\": \"else\" }";

        Assert.Equal(body, GeminiClient.ExtractError(body));
    }
}
