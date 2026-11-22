"""Browser-language, live-switch, API error and VR instruction checks. Uses the isolated Docker fixture."""
from pathlib import Path
import json
import re
import subprocess
import urllib.request
from playwright.sync_api import sync_playwright, expect
from http_tests import BASE, COMPOSE, code

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'work/website-preview'
OUT.mkdir(parents=True, exist_ok=True)
catalog = json.loads((ROOT / 'website/app/translations.json').read_text(encoding='utf-8'))
strings = (ROOT / 'tools/Shared/QuestStrings.cs').read_text(encoding='utf-8')
game = dict((key, (en, ru)) for key, en, ru in re.findall(r'\["([^"]+)"\] = new\[\] \{ "([^"\\]*)", "([^"\\]*)" \}', strings))
template = (ROOT / 'website/public_html/index.php').read_text(encoding='utf-8')
routes = {
    'game.connect_path': ['net.title', 'net.connect'],
    'game.settings_path': ['net.title', 'net.cloudSettings'],
    'game.change_cloud_path': ['net.title', 'net.change', None],
    'game.create_path': ['net.title', 'net.create', 'ui.continue'],
    'actions.paste': ['ui.paste'], 'game.connect': ['net.connect'],
    'game.open_website': ['net.openWebsite'], 'game.copy_code': ['net.copyCode'],
    'game.created_title': ['net.hostTitle'], 'game.realtime_step': ['net.stepRealtime'],
    'game.voice_step': ['net.stepVoice'], 'game.next': ['ui.next'], 'game.save': ['ui.save'],
}
for key, route in routes.items():
    en = ' → '.join(game[key][0] if key else 'Cloud' for key in route)
    ru = ' → '.join(game[key][1] if key else 'Cloud' for key in route)
    assert f"rq_t('{key}')" in template, key
    assert catalog[key] == {'ru': ru, 'en': en}, (key, catalog.get(key))
assert 'JOIN PC' not in template
assert 'в меню паузы' not in template
assert 'Players cannot join with a code after the game starts.' in catalog['faq.host.waiting_lobby']['en']

# Server rendering also works without JavaScript and ignores secondary Russian preferences.
for preferred, expected in [('ru-RU,ru;q=0.9', 'ru'), ('en-US,ru;q=0.8', 'en'), ('fr-FR', 'en')]:
    request = urllib.request.Request(BASE, headers={'Accept-Language': preferred})
    with urllib.request.urlopen(request) as response:
        source = response.read().decode('utf-8')
    assert f'<html lang="{expected}">' in source
    assert ('Enable JavaScript in your browser' in source) == (expected == 'en')

subprocess.run(COMPOSE + ['exec', '-T', 'web', 'php', 'tests/db.php', 'fixture'], check=True)
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    for language, expected in [('ru-RU', 'ru'), ('ru', 'ru'), ('en-US', 'en'), ('de-DE', 'en')]:
        subprocess.run(COMPOSE + ['exec', '-T', 'web', 'php', 'tests/db.php', 'fixture'], check=True)
        context = browser.new_context(locale=language, viewport={'width': 390, 'height': 844})
        page = context.new_page()
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(BASE)
        page.wait_for_load_state('networkidle')
        expect(page.locator('html')).to_have_attribute('lang', expected)
        expect(page.locator(f'[data-language={expected}]')).to_have_attribute('aria-pressed', 'true')
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        page.locator('.faq summary').nth(1).click()
        expect(page.locator('.faq details').nth(1)).to_have_attribute('open', '')
        page.locator('#room-title').fill('Комнаты')  # User content matching a UI translation must stay untouched.
        page.locator('#room-code').fill('RQ1.invalid')
        page.locator('#publish-button').click()
        expect(page.locator('#publish-status')).to_be_visible()
        expect(page.locator('#publish-status')).to_have_text(catalog['error.invalid_room_code'][expected])
        other = 'en' if expected == 'ru' else 'ru'
        page.locator(f'[data-language={other}]').click()
        expect(page.locator('html')).to_have_attribute('lang', other)
        expect(page.locator('#room-title')).to_have_value('Комнаты')
        expect(page.locator('#room-code')).to_have_value('RQ1.invalid')
        expect(page.locator('#publish-status')).to_have_text(catalog['error.invalid_room_code'][other])
        page.reload()
        expect(page.locator('html')).to_have_attribute('lang', other)
        # Return to English and audit the entire static page, including expanded FAQ sections.
        page.locator('[data-language=en]').click()
        text = page.evaluate("""() => {
            const body = document.body.cloneNode(true);
            body.querySelectorAll('script,style,template,noscript,.room-name,.room-version,.room-region').forEach(e => e.remove());
            return body.textContent;
        }""")
        assert not re.search('[А-Яа-яЁё]', text), text
        for details in page.locator('.faq details').all(): details.evaluate('(e) => e.open = true')
        expect(page).to_have_title(catalog['page.title']['en'])
        expect(page.locator('#room-title')).to_have_attribute('placeholder', catalog['publish.name_placeholder']['en'])
        expect(page.locator('.language-switch')).to_have_attribute('aria-label', catalog['language.label']['en'])
        assert page.locator('#refresh [aria-hidden=true]').inner_text() == '↻', 'refresh icon lost'
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'expanded FAQ overflow'
        page.screenshot(path=str(OUT / f'faq-{language}.png'), full_page=True)
        assert not errors, errors
        context.close()

    # Live switching retains the deletion capability, published link, names and clipboard feedback.
    subprocess.run(COMPOSE + ['exec', '-T', 'web', 'php', 'tests/db.php', 'fixture'], check=True)
    context = browser.new_context(locale='en-US', permissions=['clipboard-read', 'clipboard-write'])
    page = context.new_page()
    page.goto(BASE)
    page.wait_for_load_state('networkidle')
    page.locator('#room-title').fill('Комнаты')
    page.locator('#room-code').fill(code())
    page.locator('#publish-button').click()
    expect(page.locator('#owner-result')).to_be_visible()
    link = page.locator('#delete-link').input_value()
    page.locator('.button-copy').first.click()
    expect(page.locator('.button-copy').first).to_have_text('Copied ✓', use_inner_text=True)
    page.locator('[data-language=ru]').click()
    expect(page.locator('.button-copy').first).to_have_text('Скопировано ✓', use_inner_text=True)
    expect(page.locator('#delete-link')).to_have_value(link)
    expect(page.locator('.room-name').filter(has_text='Комнаты')).to_have_count(1)
    page.evaluate("catalog.translations['rooms.title'].ru = 'Комнаты (проверка ключа)'; setLanguage('ru')")
    expect(page.locator('#rooms-heading')).to_have_text('Комнаты (проверка ключа)')
    expect(page.locator('.room-name').filter(has_text=re.compile('^Комнаты$'))).to_have_count(1)
    page.wait_for_timeout(2300)
    expect(page.locator('.button-copy').first).to_have_text('Копировать код ↗')
    page.goto(link)
    expect(page.locator('#delete-dialog')).to_be_visible()
    # A modal prevents pointer access to the header, so exercise the same switch function while it is open.
    page.evaluate("setLanguage('en')")
    expect(page.locator('#delete-heading')).to_have_text('Delete room?')
    page.locator('#confirm-delete').click()
    expect(page.locator('#delete-dialog')).not_to_be_visible()
    expect(page.locator('#list-status')).to_have_text('Invitation deleted.')
    context.close()
    browser.close()
print('PASS: VR labels in RU/EN, browser defaults, persistence, live switch, form/link preservation, localized API errors, copy timeout, deletion, FAQ and overflow')
