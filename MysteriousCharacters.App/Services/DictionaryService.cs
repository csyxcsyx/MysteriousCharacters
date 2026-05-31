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
        var rules = ReadEmbeddedRules(EmbeddedCommonCharacterRulesName).ToList();

        if (!string.IsNullOrWhiteSpace(customDictionaryPath) && File.Exists(customDictionaryPath))
        {
            rules.AddRange(ReadRules(File.ReadAllText(customDictionaryPath)));
        }

        return OverrideRules(rules);
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

    private static IReadOnlyList<DictionaryRule> OverrideRules(IEnumerable<DictionaryRule> rules)
    {
        var rulesBySource = new Dictionary<string, DictionaryRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            rulesBySource[rule.Source] = rule;
        }

        return rulesBySource.Values.ToList();
    }
}
