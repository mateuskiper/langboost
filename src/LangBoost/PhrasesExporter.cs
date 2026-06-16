using System.IO;
using System.Text.Json;

namespace LangBoost;

/// <summary>
/// Writes curated English phrases as JSON Lines: one <c>{"english": "..."}</c> object per line.
/// The value is serialised with System.Text.Json so quotes/specials are escaped correctly.
/// </summary>
public static class PhrasesExporter
{
    public static void Write(string path, IEnumerable<string> phrases)
    {
        // Serialise only the string value (proper escaping) and wrap it so the line matches
        // the requested format exactly, including the space after the colon.
        var lines = phrases.Select(p => "{\"english\": " + JsonSerializer.Serialize(p) + "}");
        File.WriteAllLines(path, lines);
    }
}
