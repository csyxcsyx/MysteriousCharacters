#!/usr/bin/env python3
"""Generate offline replacement rules for the 3500 level-one common characters."""

from __future__ import annotations

import argparse
import json
import re
import unicodedata
from collections import defaultdict
from pathlib import Path


IDC_CHARACTERS = set("⿰⿱⿲⿳⿴⿵⿶⿷⿸⿹⿺⿻")
TERNARY_IDC_CHARACTERS = set("⿲⿳")
REGION_TAG_PATTERN = re.compile(r"\[[^\]]+\]")
ENTITY_PATTERN = re.compile(r"&[^;]+;")
COMMON_LINE_PATTERN = re.compile(r"^\s*\d{4}\s+(\S+)\s*$")
READABLE_RADICAL_COMPONENTS = set(
    "人刀力口土女子山巾广弓心手日月木水火牛犬王田目石示禾竹米羊耳肉衣言贝车走足金门雨马鱼鸟草丝食"
)
MIN_READABLE_REMOVAL_SOURCE_STROKES = 7
MIN_READABLE_PRESERVED_COMPONENT_STROKES = 3
MAX_READABLE_REMOVED_COMPONENT_STROKES = 6
CURATED_READABLE_FALLBACKS = {
    "嫩": ("嫰", "Similar"),
    "囊": ("馕", "AddRadical"),
}
MISLEADING_EQUIVALENT_GROUPS = [
    "你您",
    "他她它",
    "的地得",
    "在再",
    "做作",
    "以已",
]
MISLEADING_EQUIVALENT_PAIRS = {
    frozenset((left, right))
    for group in MISLEADING_EQUIVALENT_GROUPS
    for index, left in enumerate(group)
    for right in group[index + 1 :]
}

# Radical variants are normalized to a familiar standalone Chinese character
# when possible. Candidates still have to belong to the supplied common table.
COMPONENT_ALIASES = {
    "亻": "人",
    "氵": "水",
    "扌": "手",
    "忄": "心",
    "灬": "火",
    "刂": "刀",
    "艹": "草",
    "龵": "手",
    "⺶": "羊",
    "⺼": "月",
    "牜": "牛",
    "犭": "犬",
    "礻": "示",
    "衤": "衣",
    "钅": "金",
    "饣": "食",
    "纟": "丝",
    "讠": "言",
    "攵": "文",
    "辶": "走",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--common-table", required=True, type=Path)
    parser.add_argument("--ids", required=True, type=Path)
    parser.add_argument("--unihan-readings", required=True, type=Path)
    parser.add_argument("--unihan-irg-sources", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    return parser.parse_args()


def load_common_characters(path: Path) -> list[str]:
    characters: list[str] = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        match = COMMON_LINE_PATTERN.match(line)
        if match:
            characters.append(match.group(1))

    if len(characters) != 3500 or len(set(characters)) != 3500:
        raise ValueError(
            f"Expected 3500 unique common characters, got {len(characters)} "
            f"entries and {len(set(characters))} unique values."
        )
    if any(len(character) != 1 for character in characters):
        raise ValueError("The level-one table must contain single BMP characters.")

    return characters


def parse_ids_node(tokens: list[str], index: int = 0) -> tuple[object, int]:
    token = tokens[index]
    if token not in IDC_CHARACTERS:
        return token, index + 1

    children: list[object] = []
    next_index = index + 1
    arity = 3 if token in TERNARY_IDC_CHARACTERS else 2
    for _ in range(arity):
        child, next_index = parse_ids_node(tokens, next_index)
        children.append(child)
    return (token, children), next_index


def extract_top_level_components(expression: str, common_set: set[str]) -> set[str]:
    expression = REGION_TAG_PATTERN.sub("", expression)
    expression = ENTITY_PATTERN.sub("", expression)
    tokens = [character for character in expression if not character.isspace()]
    if not tokens:
        return set()

    try:
        root, _ = parse_ids_node(tokens)
    except (IndexError, RecursionError):
        return set()

    if not isinstance(root, tuple):
        return set()

    components: set[str] = set()
    for child in root[1]:
        if not isinstance(child, str):
            continue
        normalized = COMPONENT_ALIASES.get(child, child)
        if normalized in common_set:
            components.add(normalized)
    return components


def load_ids_components(path: Path, common_set: set[str]) -> dict[str, set[str]]:
    components_by_character: dict[str, set[str]] = defaultdict(set)
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue

        fields = line.split("\t")
        if len(fields) < 3:
            continue

        character = fields[1]
        if character not in common_set:
            continue

        for expression in fields[2:]:
            components_by_character[character].update(
                component
                for component in extract_top_level_components(expression, common_set)
                if component != character
            )

    return components_by_character


def normalize_pinyin(value: str) -> str:
    normalized = unicodedata.normalize("NFD", value.lower())
    return "".join(
        character
        for character in normalized
        if unicodedata.category(character) != "Mn" and not character.isdigit()
    )


def load_mandarin_readings(path: Path, common_set: set[str]) -> dict[str, set[str]]:
    readings_by_character: dict[str, set[str]] = defaultdict(set)
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue

        codepoint, property_name, value = line.split("\t", 2)
        if property_name != "kMandarin":
            continue

        character = chr(int(codepoint[2:], 16))
        if character not in common_set:
            continue

        for reading in re.split(r"[\s,]+", value):
            normalized = normalize_pinyin(reading)
            if normalized:
                readings_by_character[character].add(normalized)

    return readings_by_character


def load_radical_strokes(path: Path, common_set: set[str]) -> dict[str, tuple[int, int]]:
    radical_strokes: dict[str, tuple[int, int]] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue

        codepoint, property_name, value = line.split("\t", 2)
        if property_name != "kRSUnicode":
            continue

        character = chr(int(codepoint[2:], 16))
        if character not in common_set:
            continue

        first_value = value.split()[0].replace("'", "")
        radical, residual_strokes = first_value.split(".", 1)
        radical_strokes[character] = (int(radical), int(residual_strokes))

    return radical_strokes


def load_total_strokes(path: Path, common_set: set[str]) -> dict[str, int]:
    total_strokes: dict[str, int] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue

        codepoint, property_name, value = line.split("\t", 2)
        if property_name != "kTotalStrokes":
            continue

        character = chr(int(codepoint[2:], 16))
        if character not in common_set:
            continue

        values = [int(match) for match in re.findall(r"\d+", value)]
        if values:
            total_strokes[character] = min(values)

    return total_strokes


def select_ranked(
    values: set[str] | list[str],
    source: str,
    ranks: dict[str, int],
    limit: int,
) -> list[str]:
    return sorted(
        (value for value in values if value != source),
        key=lambda value: (ranks[value], value),
    )[:limit]


def select_ranked_additions(
    values: set[str],
    source: str,
    ranks: dict[str, int],
    total_strokes: dict[str, int],
    limit: int,
) -> list[str]:
    source_strokes = total_strokes.get(source, 0)
    return sorted(
        (value for value in values if value != source),
        key=lambda value: (
            max(0, total_strokes.get(value, source_strokes) - source_strokes),
            ranks[value],
            value,
        ),
    )[:limit]


def select_readable_removals(
    source: str,
    components: set[str],
    ranks: dict[str, int],
    total_strokes: dict[str, int],
) -> list[str]:
    source_strokes = total_strokes.get(source)
    if source_strokes is None or source_strokes < MIN_READABLE_REMOVAL_SOURCE_STROKES:
        return []

    candidates: list[tuple[int, int, int, str]] = []
    for component in components:
        component_strokes = total_strokes.get(component)
        if component_strokes is None or component_strokes < MIN_READABLE_PRESERVED_COMPONENT_STROKES:
            continue

        removed_strokes = source_strokes - component_strokes
        if removed_strokes < 1 or removed_strokes > MAX_READABLE_REMOVED_COMPONENT_STROKES:
            continue

        other_components = components - {component}
        if component in READABLE_RADICAL_COMPONENTS:
            continue
        if not any(other in READABLE_RADICAL_COMPONENTS for other in other_components):
            continue

        candidates.append((removed_strokes, -component_strokes, ranks[component], component))

    return [candidate[3] for candidate in sorted(candidates)[:3]]


def add_candidates(
    candidates: list[dict[str, object]],
    values: list[str],
    replacement_type: str,
    weight: int,
) -> None:
    existing = {(candidate["text"], candidate["type"]) for candidate in candidates}
    for value in values:
        key = (value, replacement_type)
        if key not in existing:
            candidates.append({"text": value, "type": replacement_type, "weight": weight})
            existing.add(key)


def remove_misleading_candidates(
    source: str,
    candidates: list[dict[str, object]],
) -> list[dict[str, object]]:
    return [
        candidate
        for candidate in candidates
        if frozenset((source, str(candidate["text"]))) not in MISLEADING_EQUIVALENT_PAIRS
    ]


def build_rules(
    common_characters: list[str],
    components_by_character: dict[str, set[str]],
    readings_by_character: dict[str, set[str]],
    radical_strokes: dict[str, tuple[int, int]],
    total_strokes: dict[str, int],
) -> tuple[list[dict[str, object]], dict[str, int]]:
    common_set = set(common_characters)
    ranks = {character: index for index, character in enumerate(common_characters)}

    add_radical_by_component: dict[str, set[str]] = defaultdict(set)
    for derived, components in components_by_character.items():
        for component in components:
            if component in common_set and component != derived:
                add_radical_by_component[component].add(derived)

    characters_by_reading: dict[str, set[str]] = defaultdict(set)
    for character, readings in readings_by_character.items():
        for reading in readings:
            characters_by_reading[reading].add(character)

    characters_by_radical: dict[int, set[str]] = defaultdict(set)
    for character, (radical, _) in radical_strokes.items():
        characters_by_radical[radical].add(character)

    statistics = defaultdict(int)
    rules: list[dict[str, object]] = []

    for source in common_characters:
        candidates: list[dict[str, object]] = []

        add_radical = select_ranked_additions(
            add_radical_by_component[source],
            source,
            ranks,
            total_strokes,
            10,
        )
        remove_radical = select_readable_removals(
            source,
            components_by_character[source],
            ranks,
            total_strokes,
        )
        add_candidates(candidates, add_radical, "AddRadical", 16)
        add_candidates(candidates, remove_radical, "RemoveRadical", 16)
        if add_radical:
            statistics["sources_with_add_radical_candidates"] += 1
        if remove_radical:
            statistics["sources_with_remove_radical_candidates"] += 1
        if add_radical or remove_radical:
            statistics["sources_with_radical_candidates"] += 1

        homophones: set[str] = set()
        for reading in readings_by_character[source]:
            homophones.update(characters_by_reading[reading])
        selected_homophones = select_ranked(homophones, source, ranks, 5)
        add_candidates(candidates, selected_homophones, "Homophone", 6)
        if selected_homophones:
            statistics["sources_with_homophone_candidates"] += 1

        similar: list[str] = []
        radical_stroke = radical_strokes.get(source)
        if radical_stroke:
            radical, residual_strokes = radical_stroke
            similar = sorted(
                (
                    character
                    for character in characters_by_radical[radical]
                    if character != source
                    and total_strokes.get(character, 0) >= total_strokes.get(source, 0)
                ),
                key=lambda character: (
                    abs(radical_strokes[character][1] - residual_strokes),
                    ranks[character],
                    character,
                ),
            )[:4]
        add_candidates(candidates, similar, "Similar", 2)
        if similar:
            statistics["sources_with_similar_candidates"] += 1

        filtered_candidates = remove_misleading_candidates(source, candidates)
        if len(filtered_candidates) != len(candidates):
            statistics["sources_with_filtered_misleading_candidates"] += 1
        candidates = filtered_candidates

        if not candidates:
            fallback_rule = CURATED_READABLE_FALLBACKS.get(source)
            if fallback_rule is None:
                raise ValueError(f"No readable replacement candidate for {source}.")
            fallback, replacement_type = fallback_rule
            add_candidates(candidates, [fallback], replacement_type, 1)
            statistics["sources_with_curated_fallback"] += 1

        statistics["candidate_pool_size"] += len(candidates)
        rules.append({"source": source, "candidates": candidates[:1]})

    statistics["common_characters"] = len(common_characters)
    statistics["generated_rules"] = len(rules)
    statistics["generated_candidates"] = sum(len(rule["candidates"]) for rule in rules)
    return rules, dict(statistics)


def main() -> None:
    args = parse_args()
    common_characters = load_common_characters(args.common_table)
    common_set = set(common_characters)

    components = load_ids_components(args.ids, common_set)
    readings = load_mandarin_readings(args.unihan_readings, common_set)
    radical_strokes = load_radical_strokes(args.unihan_irg_sources, common_set)
    total_strokes = load_total_strokes(args.unihan_irg_sources, common_set)
    rules, statistics = build_rules(
        common_characters,
        components,
        readings,
        radical_strokes,
        total_strokes,
    )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps({"version": 1, "rules": rules}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    args.report.write_text(
        json.dumps(statistics, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(statistics, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
