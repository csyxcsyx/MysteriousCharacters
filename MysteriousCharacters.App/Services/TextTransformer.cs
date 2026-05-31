using System.Text;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class TextTransformer
{
    private Dictionary<string, IReadOnlyList<ReplacementCandidate>> _rules = [];

    public TextTransformer(IReadOnlyList<DictionaryRule> rules)
    {
        ReplaceRules(rules);
    }

    public int RuleCount => _rules.Count;

    public void ReplaceRules(IReadOnlyList<DictionaryRule> rules)
    {
        _rules = rules.ToDictionary(
            rule => rule.Source,
            rule => (IReadOnlyList<ReplacementCandidate>)rule.Candidates,
            StringComparer.Ordinal);
    }

    public string Transform(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            var element = rune.ToString();
            if (_rules.TryGetValue(element, out var candidates))
            {
                builder.Append(PickPreferred(candidates)?.Text ?? element);
            }
            else
            {
                builder.Append(element);
            }
        }

        return builder.ToString();
    }

    public static bool IsHanCharacter(Rune rune)
    {
        var value = rune.Value;
        return value == 0x3007 ||
               value is >= 0x3400 and <= 0x4DBF or
               >= 0x4E00 and <= 0x9FFF or
               >= 0xF900 and <= 0xFAFF or
               >= 0x20000 and <= 0x2FA1F or
               >= 0x30000 and <= 0x323AF;
    }

    private static ReplacementCandidate? PickPreferred(IReadOnlyList<ReplacementCandidate> candidates)
    {
        var radicalCandidates = candidates
            .Where(candidate =>
                candidate.Type is ReplacementType.AddRadical or ReplacementType.RemoveRadical)
            .ToList();
        if (radicalCandidates.Count > 0)
        {
            return PickWeighted(radicalCandidates);
        }

        var homophoneCandidates = candidates
            .Where(candidate => candidate.Type == ReplacementType.Homophone)
            .ToList();
        if (homophoneCandidates.Count > 0)
        {
            return PickWeighted(homophoneCandidates);
        }

        var similarCandidates = candidates
            .Where(candidate => candidate.Type == ReplacementType.Similar)
            .ToList();
        return similarCandidates.Count > 0 ? PickWeighted(similarCandidates) : null;
    }

    private static ReplacementCandidate PickWeighted(IReadOnlyList<ReplacementCandidate> candidates)
    {
        var totalWeight = candidates.Sum(candidate => candidate.Weight);
        var selectedWeight = Random.Shared.Next(totalWeight);

        foreach (var candidate in candidates)
        {
            selectedWeight -= candidate.Weight;
            if (selectedWeight < 0)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }
}
