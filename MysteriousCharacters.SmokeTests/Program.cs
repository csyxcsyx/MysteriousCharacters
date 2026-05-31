using System.Text;
using System.Text.RegularExpressions;
using MysteriousCharacters.App.Services;

if (args.Length != 2)
{
    throw new InvalidOperationException(
        "Pass the custom dictionary example path and the level-one common-character table path.");
}

var exampleDictionaryPath = args[0];
var commonCharacterTablePath = args[1];

var dictionaryService = new DictionaryService();
var builtInRules = dictionaryService.LoadRules(null);
var transformer = new TextTransformer(builtInRules);

Assert(transformer.RuleCount >= 3500, "Expected all level-one common characters to be covered.");
Assert(transformer.DecodeRuleCount >= 1000, "Expected a substantial reverse lookup table.");
Assert(builtInRules.All(rule => rule.Candidates.Count == 1), "Built-in rules should be compact fixed mappings.");
Assert(transformer.Transform("ABC 123") == "ABC 123", "Non-Chinese text changed.");
Assert(transformer.Transform("ABC 123", TransformDirection.Decode) == "ABC 123", "Decoded non-Chinese text changed.");

VerifyReplacement(transformer, "我想吃饭");
VerifyReplacement(transformer, "马青反");
VerifyReplacement(transformer, "清吗饭");
VerifyReplacement(transformer, "未土日");
VerifyReplacement(transformer, "我看着你的脸，轻刷着和弦；情人节卡片，手写的从前。。。");
VerifyReplacement(transformer, "一乙二十丁厂七卜八人入儿匕几九刁了刀力乃");
VerifyDeterministicReplacement(transformer, "天青色等烟雨，而我在等你。");
VerifyRoundTrip(transformer, "乙八九");
VerifyNeverProduces(transformer, "你", ["您"]);
VerifyNeverProduces(transformer, "他", ["她", "它"]);
VerifyNeverProduces(transformer, "的", ["地", "得"]);
VerifyNeverProduces(transformer, "在", ["再"]);
VerifyNeverProduces(transformer, "做", ["作"]);
VerifyUnmappedCharacterIsPreserved(transformer, "鑫");
VerifyUnmappedCharacterIsPreserved(transformer, "𠀀");
VerifyLevelOneCoverage(transformer, commonCharacterTablePath);

var mergedRules = dictionaryService.LoadRules(exampleDictionaryPath);
var mergedTransformer = new TextTransformer(mergedRules);
Assert(mergedTransformer.RuleCount == transformer.RuleCount + 1, "Custom dictionary rule was not merged.");
VerifyReplacement(mergedTransformer, "鑫");
VerifyRoundTrip(mergedTransformer, "鑫");
VerifySettingsPersistence();

Console.WriteLine($"built_in_rules={transformer.RuleCount}");
Console.WriteLine($"merged_rules={mergedTransformer.RuleCount}");
Console.WriteLine("smoke_tests=passed");

return;

static void VerifyReplacement(TextTransformer transformer, string source)
{
    var transformed = transformer.Transform(source);
    Assert(transformed != source, $"Text was not replaced: {source}");
    AssertNoStructureSymbols(transformed);
    Console.WriteLine($"{source}->{transformed}");
}

static void VerifyUnmappedCharacterIsPreserved(TextTransformer transformer, string source)
{
    var transformed = transformer.Transform(source);
    Assert(transformed == source, $"Unmapped text should stay unchanged: {source}->{transformed}");
    AssertNoStructureSymbols(transformed);
    Console.WriteLine($"preserved={source}");
}

static void VerifyNeverProduces(TextTransformer transformer, string source, string[] forbiddenValues)
{
    var forbidden = forbiddenValues.ToHashSet(StringComparer.Ordinal);
    var transformed = transformer.Transform(source);
    Assert(
        !forbidden.Contains(transformed),
        $"Misleading replacement regression: {source}->{transformed}");

    Console.WriteLine($"readability_checked={source}");
}

static void VerifyDeterministicReplacement(TextTransformer transformer, string source)
{
    var expected = transformer.Transform(source);
    for (var index = 0; index < 20; index++)
    {
        Assert(transformer.Transform(source) == expected, $"Replacement changed between runs: {source}");
    }

    Console.WriteLine($"deterministic={source}->{expected}");
}

static void VerifyRoundTrip(TextTransformer transformer, string source)
{
    var encoded = transformer.Transform(source);
    var decoded = transformer.Transform(encoded, TransformDirection.Decode);
    Assert(decoded == source, $"Expected an exact custom dictionary round trip: {source}->{encoded}->{decoded}");
    Console.WriteLine($"round_trip={source}->{encoded}->{decoded}");
}

static void VerifyLevelOneCoverage(TextTransformer transformer, string commonCharacterTablePath)
{
    var commonCharacters = File
        .ReadLines(commonCharacterTablePath)
        .Select(line => Regex.Match(line, @"^\s*\d{4}\s+(\S+)\s*$"))
        .Where(match => match.Success)
        .Select(match => match.Groups[1].Value)
        .ToList();

    Assert(commonCharacters.Count == 3500, "Expected exactly 3500 common characters.");
    Assert(commonCharacters.Distinct(StringComparer.Ordinal).Count() == 3500, "Common table contains duplicates.");

    foreach (var source in commonCharacters)
    {
        var transformed = transformer.Transform(source);
        Assert(transformed != source, $"Level-one common character was not replaced: {source}");
        Assert(transformed.EnumerateRunes().Count() == 1, $"Replacement must be exactly one character: {source}->{transformed}");
        Assert(
            TextTransformer.IsHanCharacter(transformed.EnumerateRunes().Single()),
            $"Replacement must be a Chinese character: {source}->{transformed}");
    }

    Console.WriteLine($"level_one_coverage={commonCharacters.Count}/{commonCharacters.Count}");
}

static void VerifySettingsPersistence()
{
    var dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"MysteriousCharacters-SmokeTests-{Guid.NewGuid():N}");

    try
    {
        Directory.CreateDirectory(dataDirectory);
        var settingsService = new SettingsService(dataDirectory);
        var settings = settingsService.Load();
        settings.RestoreClipboard = false;
        settings.ShowNotifications = false;
        settingsService.Save(settings);
        var savedSettings = settingsService.Load();
        Assert(!savedSettings.RestoreClipboard, "Clipboard setting was not persisted.");
        Assert(!savedSettings.ShowNotifications, "Notification setting was not persisted.");
        Console.WriteLine("settings_persistence=passed");
    }
    finally
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, true);
        }
    }
}

static void AssertNoStructureSymbols(string text)
{
    foreach (var rune in text.EnumerateRunes())
    {
        Assert(rune.Value is < 0x2E80 or > 0x2FFF, $"Structure symbol leaked into output: {text}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
