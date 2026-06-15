using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LangBoost;

public sealed record TranscriptionResult(string Original, string Traducao);

/// <summary>
/// Google Gemini client. In a single call, sends the audio (WAV inline as base64)
/// and receives the English transcription and the Portuguese translation.
/// </summary>
public sealed class GeminiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string Prompt =
        "You receive a short clip of audio in English taken from a video. " +
        "Transcribe the spoken English exactly into the field 'original'. " +
        "Provide a natural Brazilian Portuguese translation in the field 'traducao'. " +
        "If there is no clear speech, return both fields as empty strings.";

    private readonly string _apiKey;
    private readonly string _model;

    public GeminiClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<TranscriptionResult> TranscribeAndTranslateAsync(byte[] wav, CancellationToken ct = default)
    {
        // The key goes in the x-goog-api-key header (not the query string) so it does not leak into
        // proxy logs, crash dumps or exception messages that may include the request URI.
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = Prompt },
                        new { inline_data = new { mime_type = "audio/wav", data = Convert.ToBase64String(wav) } }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        original = new { type = "STRING" },
                        traducao = new { type = "STRING" }
                    },
                    required = new[] { "original", "traducao" }
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        // Set the key per-request: Http is a shared static instance, so we must not mutate DefaultRequestHeaders.
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("x-goog-api-key", _apiKey);
        using var response = await Http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini {(int)response.StatusCode}: {ExtractError(json)}");
        }

        return ParseResult(json);
    }

    private static TranscriptionResult ParseResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return new TranscriptionResult("", "");

        string text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        if (string.IsNullOrWhiteSpace(text))
            return new TranscriptionResult("", "");

        // The returned text is already the JSON { original, traducao } requested in responseSchema.
        using var inner = JsonDocument.Parse(text);
        var obj = inner.RootElement;
        string original = obj.TryGetProperty("original", out var o) ? o.GetString() ?? "" : "";
        string traducao = obj.TryGetProperty("traducao", out var t) ? t.GetString() ?? "" : "";
        return new TranscriptionResult(original.Trim(), traducao.Trim());
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? json;
        }
        catch { /* keep the raw body */ }
        return json;
    }
}
