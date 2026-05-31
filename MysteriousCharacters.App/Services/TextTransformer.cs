using System.Text;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class TextTransformer
{
    private Dictionary<string, string> _encodeRules = [];
    private Dictionary<string, string> _decodeRules = [];

    public TextTransformer(IReadOnlyList<DictionaryRule> rules)
    {
        ReplaceRules(rules);
    }

    public int RuleCount => _encodeRules.Count;

    public int DecodeRuleCount => _decodeRules.Count;

    public void ReplaceRules(IReadOnlyList<DictionaryRule> rules)
    {
        var mappings = rules
            .Select(rule => new
            {
                rule.Source,
                Candidate = PickPreferred(rule.Candidates)
            })
            .Where(mapping => mapping.Candidate is not null)
            .ToList();

        _encodeRules = mappings.ToDictionary(
            mapping => mapping.Source,
            mapping => mapping.Candidate!.Text,
            StringComparer.Ordinal);

        // Plain Han-character substitutions cannot always be reversed without
        // ambiguity. Prefer structural relationships when several sources
        // produce the same encoded character.
        _decodeRules = mappings
            .GroupBy(mapping => mapping.Candidate!.Text, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(mapping => GetPriority(mapping.Candidate!.Type))
                    .ThenByDescending(mapping => mapping.Candidate!.Weight)
                    .Select(mapping => mapping.Source)
                    .First(),
                StringComparer.Ordinal);
    }

    public string Transform(string text, TransformDirection direction = TransformDirection.Encode)
    {
        var rules = direction == TransformDirection.Encode ? _encodeRules : _decodeRules;
        var builder = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            var element = rune.ToString();
            builder.Append(rules.GetValueOrDefault(element, element));
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
        return candidates
            .OrderBy(candidate => GetPriority(candidate.Type))
            .ThenByDescending(candidate => candidate.Weight)
            .FirstOrDefault();
    }

    private static int GetPriority(ReplacementType type)
    {
        return type switch
        {
            ReplacementType.AddRadical => 0,
            ReplacementType.RemoveRadical => 1,
            ReplacementType.Homophone => 2,
            ReplacementType.Similar => 3,
            _ => int.MaxValue
        };
    }
}

public enum TransformDirection
{
    Encode,
    Decode
}
