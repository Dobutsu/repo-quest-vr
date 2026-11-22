"""Run with Python + Playwright after compose up. Screenshots are written outside the source tree."""
import subprocess
from pathlib import Path
from playwright.sync_api import sync_playwright, expect
from http_tests import BASE, COMPOSE, code

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'work/website-preview'
OUT.mkdir(parents=True, exist_ok=True)


def db(action):
    subprocess.run(COMPOSE + ['exec', '-T', 'web', 'php', 'tests/db.php', action], check=True)


db('fixture')
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True)
    first = browser.new_context(locale='ru-RU', viewport={'width': 1440, 'height': 1050}, permissions=['clipboard-read', 'clipboard-write'])
    second = browser.new_context(locale='ru-RU', viewport={'width': 1440, 'height': 1050})
    page = first.new_page()
    errors, dialogs, external = [], [], []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('dialog', lambda dialog: (dialogs.append(dialog.message), dialog.dismiss()))
    page.on('request', lambda request: external.append(request.url) if not request.url.startswith(BASE) else None)
    page.goto(BASE)
    page.wait_for_load_state('networkidle')
    expect(page.locator('.room-card')).to_have_count(3)
    expect(page.locator('.room-name').filter(has_text='<script>alert(1)</script>')).to_be_visible()
    assert page.locator('.room-card script, .room-card img').count() == 0
    page.screenshot(path=str(OUT / 'desktop.png'), full_page=True)

    page.locator('.button-copy').first.click()
    expect(page.locator('.button-copy').first).to_have_text('Скопировано ✓', use_inner_text=True)
    expect(page.locator('.button-copy').first.locator('[aria-hidden]')).to_be_hidden()
    clipboard = page.evaluate('navigator.clipboard.readText()')
    assert clipboard.startswith('RQ1.')

    other = second.new_page()
    other.goto(BASE)
    other.wait_for_load_state('networkidle')
    expect(other.locator('.room-card')).to_have_count(3)
    page.locator('#room-title').fill('Проверка двух браузеров')
    page.locator('#room-code').fill(code())
    page.locator('#publish-button').click()
    expect(page.locator('#owner-result')).to_be_visible()
    deletion_link = page.locator('#delete-link').input_value()
    assert '#delete=' in deletion_link
    expect(page.locator('.room-card')).to_have_count(4)
    other.locator('#refresh').click()
    expect(other.locator('.room-name').filter(has_text='Проверка двух браузеров')).to_be_visible()

    # A browser which cannot use the Clipboard API can still select the complete code manually.
    page.evaluate("Object.defineProperty(navigator, 'clipboard', {value: {writeText: async () => {throw new Error('denied')}}})")
    page.locator('.button-copy').last.click()
    expect(page.locator('.copy-fallback').last).to_be_visible()
    assert page.locator('.copy-fallback textarea').last.input_value().startswith('RQ1.')

    # A new, independent browser can delete only with the saved capability + its own CSRF session.
    owner = second.new_page()
    owner.goto(deletion_link)
    owner.wait_for_load_state('networkidle')
    expect(owner.locator('#delete-dialog')).to_be_visible()
    assert owner.url == BASE + '/'
    expect(owner.locator('.room-card')).to_have_count(4)
    owner.locator('#cancel-delete').click()
    expect(owner.locator('#delete-dialog')).not_to_be_visible()
    owner.goto(deletion_link)
    owner.wait_for_load_state('networkidle')
    owner.locator('#confirm-delete').click()
    expect(owner.locator('#delete-dialog')).not_to_be_visible()
    expect(owner.locator('.room-card')).to_have_count(3)
    other.locator('#refresh').click()
    expect(other.locator('.room-name').filter(has_text='Проверка двух браузеров')).to_have_count(0)

    mobile = browser.new_context(locale='ru-RU', viewport={'width': 390, 'height': 844}, is_mobile=True, device_scale_factor=1, has_touch=True)
    small = mobile.new_page()
    small.goto(BASE)
    small.wait_for_load_state('networkidle')
    expect(small.locator('.room-card')).to_have_count(3)
    assert small.evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'horizontal overflow'
    small.screenshot(path=str(OUT / 'mobile.png'), full_page=True)
    small.locator('#paste-code').click()
    expect(small.locator('#room-code')).to_be_focused()

    db('reset')
    page.locator('#refresh').click()
    expect(page.locator('#empty-state')).to_be_visible()
    page.screenshot(path=str(OUT / 'empty.png'), full_page=True)
    assert not errors, errors
    assert not dialogs, dialogs
    assert not external, external
    browser.close()
print('PASS: desktop/mobile UI, two browser contexts, XSS rendering, clipboard + fallback, confirmation + deletion, empty state; no page errors or external requests')
