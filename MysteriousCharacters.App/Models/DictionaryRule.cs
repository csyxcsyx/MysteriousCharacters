namespace MysteriousCharacters.App.Models;

public sealed class DictionaryDocument
{
    public int Version { get; set; } = 1;

    public List<DictionaryRule> Rules { get; set; } = [];
}

public sealed class RadicalFamilyDocument
{
    public int Version { get; set; } = 1;

    public List<RadicalFamily> Families { get; set; } = [];
}

public sealed class RadicalFamily
{
    public string Base { get; set; } = string.Empty;

    public List<string> Derived { get; set; } = [];

    public int Weight { get; set; } = 10;
}

public sealed class DictionaryRule
{
    public string Source { get; set; } = string.Empty;

    public List<ReplacementCandidate> Candidates { get; set; } = [];
}

public sealed class ReplacementCandidate
{
    public string Text { get; set; } = string.Empty;

    public ReplacementType Type { get; set; }

    public int Weight { get; set; } = 1;
}
