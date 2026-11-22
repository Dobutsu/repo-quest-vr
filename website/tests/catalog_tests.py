"""Check stable translation IDs, language coverage, placeholders and template bindings without a running server."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def unique_entries(pairs):
    result = {}
    for key, value in pairs:
        assert key not in result, f'Duplicate catalog key: {key}'
        result[key] = value
    return result

catalog = json.loads((ROOT / 'app/translations.json').read_text(encoding='utf-8'), object_pairs_hook=unique_entries)
for key, entry in catalog.items():
    assert re.fullmatch(r'[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)*', key), key
    assert set(entry) == {'ru', 'en'}, key
    assert all(isinstance(value, str) and value.strip() for value in entry.values()), key
    assert set(re.findall(r'\{(\w+)\}', entry['ru'])) == set(re.findall(r'\{(\w+)\}', entry['en'])), key

template = (ROOT / 'public_html/index.php').read_text(encoding='utf-8')
bound = set(re.findall(r'data-i18n(?:-[a-z-]+)?="([^"]+)"', template))
called = set(re.findall(r"rq_(?:t|message)\('([^']+)'\)", template))
assert bound <= catalog.keys(), bound - catalog.keys()
assert called <= catalog.keys(), called - catalog.keys()
assert set(re.findall(r"rq_t\('([^']+)'\)", template)) <= bound, 'unbound server-rendered translation'
for relative in ['public_html/assets/site.js', 'public_html/assets/i18n.js', 'app/I18n.php']:
    source = (ROOT / relative).read_text(encoding='utf-8')
    assert not re.search('[А-Яа-яЁё]', source), relative
    keys = re.findall(r"(?:\bt|rq_message|rq_t|Error)\('([a-z][a-z0-9_.]+)'", source)
    keys += re.findall(r"=> '([a-z][a-z0-9_.]+)'", source)
    assert set(keys) <= catalog.keys(), (relative, set(keys) - catalog.keys())
client = (ROOT / 'public_html/assets/i18n.js').read_text(encoding='utf-8')
assert 'sourceMessages' not in client and 'createTreeWalker' not in client
assert 'catalog.translations[key]?.[locale]' in client
print(f'PASS: {len(catalog)} neutral IDs, RU/EN values, matching placeholders, PHP/DOM/JS/error references and explicit bindings')
