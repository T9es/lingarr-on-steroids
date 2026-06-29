import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TRANSLATIONS_DIR = ROOT / 'Lingarr.Server' / 'Statics' / 'Translations'
FRONTEND_DIR = ROOT / 'Lingarr.Client' / 'src'
LOCALES = ['en', 'nl', 'de', 'fr', 'es', 'zh', 'pl']

BRAND_KEY_PREFIXES = (
    'services.serviceNames.',
)

ENGLISH_VALUE_ALLOWLIST = {
    'common.currentVersion',
    'common.devBuild',
    'help.about.version',
    'onboarding.instanceCard.url',
    'onboarding.integration.radarr',
    'onboarding.integration.sonarr',
    'settings.chutes.mode.payg',
    'settings.custom.key_placeholder',
    'settings.services.localAiModelPlaceholder',
    'settings.services.localAiPlaceholder',
    'settings.subtitle.groupValidation',
    'settings.subtitle.outputModeBoth',
    'statistics.inLabel',
    'translationState.notApplicable'
}


class PairList(list):
    pass


def deep_merge(left, right):
    merged = dict(left)
    for key, value in right.items():
        if key in merged and isinstance(merged[key], dict) and isinstance(value, dict):
            merged[key] = deep_merge(merged[key], value)
        else:
            merged[key] = value
    return merged


def parse_json_with_duplicate_tracking(text):
    raw = json.loads(text, object_pairs_hook=PairList)
    duplicates = []

    def convert(node, path=()):
        if isinstance(node, PairList):
            result = {}
            for key, value in node:
                child_path = path + (key,)
                converted_value = convert(value, child_path)
                if key in result:
                    duplicates.append('.'.join(child_path))
                    if isinstance(result[key], dict) and isinstance(converted_value, dict):
                        result[key] = deep_merge(result[key], converted_value)
                    else:
                        result[key] = converted_value
                else:
                    result[key] = converted_value
            return result
        if isinstance(node, list):
            return [convert(item, path) for item in node]
        return node

    return convert(raw), duplicates


def flatten_translations(node, prefix=''):
    flat = {}
    if not isinstance(node, dict):
        return flat

    for key, value in node.items():
        full_key = f'{prefix}.{key}' if prefix else key
        if isinstance(value, dict):
            flat.update(flatten_translations(value, full_key))
        else:
            flat[full_key] = value
    return flat


def scan_frontend_keys():
    patterns = [
        re.compile(r"""translate\(\s*['"]([^'"]+)['"]"""),
        re.compile(r"""v-translate=["']([^"']+)["']""")
    ]

    keys = {}
    for path in FRONTEND_DIR.rglob('*'):
        if path.suffix not in {'.ts', '.vue'}:
            continue

        text = path.read_text(encoding='utf-8')
        for pattern in patterns:
            for match in pattern.finditer(text):
                key = match.group(1)
                keys.setdefault(key, set()).add(path.relative_to(ROOT).as_posix())
    return keys


def looks_like_translatable_text(value):
    if not isinstance(value, str):
        return False
    if not value.strip():
        return False
    if re.fullmatch(r'[\d./:_ -]+', value):
        return False
    if value.startswith('/'):
        return False
    if '://' in value:
        return False
    return True


def is_allowed_english_value(key):
    return key in ENGLISH_VALUE_ALLOWLIST or any(
        key.startswith(prefix) for prefix in BRAND_KEY_PREFIXES
    )


def contains_suspicious_replacement(value):
    if not isinstance(value, str):
        return False

    return bool(
        re.search(r'(?<=\w)\?(?=\w)|\?\?', value)
        or '\ufffd' in value
    )


def main():
    errors = []
    warnings = []
    locale_data = {}
    duplicate_map = {}

    for locale in LOCALES:
        path = TRANSLATIONS_DIR / f'{locale}.json'
        if not path.exists():
            errors.append(f'Missing translation file: {path.relative_to(ROOT)}')
            continue

        try:
            text = path.read_text(encoding='utf-8')
            data, duplicates = parse_json_with_duplicate_tracking(text)
        except json.JSONDecodeError as error:
            errors.append(
                f'Invalid JSON in {path.relative_to(ROOT)}: line {error.lineno}, column {error.colno}: {error.msg}'
            )
            continue

        locale_data[locale] = flatten_translations(data)
        duplicate_map[locale] = duplicates

        if duplicates:
            for duplicate in duplicates:
                errors.append(
                    f'Duplicate translation key in {path.relative_to(ROOT)}: {duplicate}'
                )

    if 'en' not in locale_data:
        for error in errors:
            print(f'ERROR: {error}')
        return 1

    english_keys = set(locale_data['en'].keys())
    frontend_keys = scan_frontend_keys()

    missing_global = sorted(key for key in frontend_keys if key not in english_keys)
    for key in missing_global:
        locations = ', '.join(sorted(frontend_keys[key]))
        errors.append(f'Frontend uses unknown translation key "{key}" in {locations}')

    for locale, translations in sorted(locale_data.items()):
        locale_keys = set(translations.keys())
        missing_keys = sorted(english_keys - locale_keys)
        extra_keys = sorted(locale_keys - english_keys)

        for key in missing_keys:
            errors.append(f'Locale "{locale}" is missing key "{key}"')

        for key in extra_keys:
            warnings.append(f'Locale "{locale}" has extra key "{key}"')

        missing_used = sorted(key for key in frontend_keys if key not in locale_keys)
        for key in missing_used:
            locations = ', '.join(sorted(frontend_keys[key]))
            errors.append(
                f'Locale "{locale}" is missing frontend-used key "{key}" (used in {locations})'
            )

    english_translations = locale_data['en']
    for locale, translations in sorted(locale_data.items()):
        if locale == 'en':
            continue

        for key, value in translations.items():
            if contains_suspicious_replacement(value):
                errors.append(
                    f'Locale "{locale}" contains suspicious replacement characters for "{key}": {value}'
                )

            english_value = english_translations.get(key)
            if (
                looks_like_translatable_text(value)
                and value == english_value
                and not is_allowed_english_value(key)
            ):
                warnings.append(
                    f'Locale "{locale}" still matches English for "{key}": {value}'
                )

    if errors:
        for error in errors:
            print(f'ERROR: {error}')

    if warnings:
        for warning in warnings:
            print(f'WARNING: {warning}')

    if not errors and not warnings:
        print('Translations look good: no structural issues, no missing frontend keys, no warnings.')
    elif not errors:
        print('Translations passed structural checks with warnings.')

    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
