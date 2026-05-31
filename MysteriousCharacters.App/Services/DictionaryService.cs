using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class DictionaryService
{
    private const string EmbeddedDictionaryName = "default-dictionary.json";
    private const string EmbeddedRadicalFamiliesName = "radical-families.json";
    private const string EmbeddedCommonCharacterRulesName = "common-character-rules.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DictionaryService()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MysteriousCharacters");
        InstalledCustomDictionaryPath = Path.Combine(DataDirectory, "custom-dictionary.json");
    }

    public string DataDirectory { get; }

    public string InstalledCustomDictionaryPath { get; }

    public IReadOnlyList<DictionaryRule> LoadRules(string? customDictionaryPath)
    {
        var rules = ReadEmbeddedRules(EmbeddedDictionaryName)
            .Concat(ReadEmbeddedRules(EmbeddedCommonCharacterRulesName))
            .Concat(ReadEmbeddedRadicalFamilyRules())
            .ToList();

        if (!string.IsNullOrWhiteSpace(customDictionaryPath) && File.Exists(customDictionaryPath))
        {
            rules.AddRange(ReadRules(File.ReadAllText(customDictionaryPath)));
        }

        return MergeRules(rules);
    }

    public int ImportCustomDictionary(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);
        var rules = ReadRules(json);

        Directory.CreateDirectory(DataDirectory);
        File.Copy(sourcePath, InstalledCustomDictionaryPath, true);
        return rules.Count;
    }

    private IReadOnlyList<DictionaryRule> ReadEmbeddedRules(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"内置词典资源“{resourceName}”不存在。");
        using var reader = new StreamReader(stream);
        return ReadRules(reader.ReadToEnd());
    }

    private IReadOnlyList<DictionaryRule> ReadEmbeddedRadicalFamilyRules()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedRadicalFamiliesName)
            ?? throw new InvalidOperationException("内置偏旁家族资源不存在。");
        using var reader = new StreamReader(stream);
        var document = JsonSerializer.Deserialize<RadicalFamilyDocument>(reader.ReadToEnd(), _jsonOptions)
            ?? throw new InvalidDataException("偏旁家族 JSON 内容为空。");

        var rules = new List<DictionaryRule>();
        foreach (var family in document.Families)
        {
            ValidateHanCharacter(family.Base, "偏旁家族的 base");
            if (family.Weight <= 0 || family.Derived.Count == 0)
            {
                throw new InvalidDataException($"偏旁家族“{family.Base}”缺少有效派生字或权重。");
            }

            var derivedCharacters = family.Derived
                .Distinct(StringComparer.Ordinal)
                .Where(character => !string.Equals(character, family.Base, StringComparison.Ordinal))
                .ToList();
            foreach (var derived in derivedCharacters)
            {
                ValidateHanCharacter(derived, $"偏旁家族“{family.Base}”的派生字");
            }

            rules.Add(new DictionaryRule
            {
                Source = family.Base,
                Candidates = derivedCharacters
                    .Select(derived => new ReplacementCandidate
                    {
                        Text = derived,
                        Type = ReplacementType.AddRadical,
                        Weight = family.Weight
                    })
                    .ToList()
            });

            rules.AddRange(derivedCharacters.Select(derived => new DictionaryRule
            {
                Source = derived,
                Candidates =
                [
                    new ReplacementCandidate
                    {
                        Text = family.Base,
                        Type = ReplacementType.RemoveRadical,
                        Weight = family.Weight
                    }
                ]
            }));
        }

        return rules;
    }

    private IReadOnlyList<DictionaryRule> ReadRules(string json)
    {
        var document = JsonSerializer.Deserialize<DictionaryDocument>(json, _jsonOptions)
            ?? throw new InvalidDataException("词典 JSON 内容为空。");

        foreach (var rule in document.Rules)
        {
            ValidateHanCharacter(rule.Source, "每条词典规则的 source");

            if (rule.Candidates.Count == 0 ||
                rule.Candidates.Any(candidate =>
                    string.IsNullOrWhiteSpace(candidate.Text) || candidate.Weight <= 0))
            {
                throw new InvalidDataException($"词典规则“{rule.Source}”缺少有效候选字或权重。");
            }

            foreach (var candidate in rule.Candidates)
            {
                ValidateHanCharacter(candidate.Text, $"词典规则“{rule.Source}”的候选字");
            }
        }

        return document.Rules;
    }

    private static void ValidateHanCharacter(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{fieldName} 必须是单个真实汉字。");
        }

        var runes = value.EnumerateRunes().ToList();
        if (runes.Count != 1 || !TextTransformer.IsHanCharacter(runes[0]))
        {
            throw new InvalidDataException($"{fieldName} 必须是单个真实汉字，不能使用偏旁描述符或其他符号。");
        }
    }

    private static IReadOnlyList<DictionaryRule> MergeRules(IEnumerable<DictionaryRule> rules)
    {
        return rules
            .GroupBy(rule => rule.Source, StringComparer.Ordinal)
            .Select(group => new DictionaryRule
            {
                Source = group.Key,
                Candidates = group
                    .SelectMany(rule => rule.Candidates)
                    .GroupBy(candidate => (candidate.Text, candidate.Type))
                    .Select(candidateGroup => new ReplacementCandidate
                    {
                        Text = candidateGroup.Key.Text,
                        Type = candidateGroup.Key.Type,
                        Weight = candidateGroup.Sum(candidate => candidate.Weight)
                    })
                    .ToList()
            })
            .ToList();
    }
}
