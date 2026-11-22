"""HTTP + concurrency checks against the isolated Docker service, using only Python's standard library."""
import base64
import concurrent.futures
import http.cookiejar
import json
import subprocess
import time
import urllib.error
import urllib.request
import urllib.parse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = 'http://127.0.0.1:8089'
COMPOSE = ['docker', 'compose', '-f', str(ROOT / 'website/tests/compose.yaml')]
count = 0


def db(action):
    return subprocess.check_output(COMPOSE + ['exec', '-T', 'web', 'php', 'tests/db.php', action], text=True).strip()


def check(condition, name):
    global count
    assert condition, name
    count += 1


def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def request(browser, action, body=None, headers=None, method=None, page=1):
    metadata = {'Accept': 'application/json'}
    if body is not None:
        body = json.dumps(body).encode() if not isinstance(body, bytes) else body
        metadata['Content-Type'] = 'application/json'
    metadata.update(headers or {})
    query = urllib.parse.urlencode({'action': action, 'page': page})
    req = urllib.request.Request(f'{BASE}/api.php?{query}', data=body, headers=metadata, method=method)
    try:
        response = browser.open(req, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, json.loads(response.read()), response.headers


def token(browser):
    status, data, _ = request(browser, 'session')
    assert status == 200, data
    return {'Origin': BASE, 'X-CSRF-Token': data['csrf']}


def code():
    fields = ['eu', "room'; DROP TABLE rq_rooms; --", 'v0.5.0.net3', 'v0.5.0.net3',
              '00000000-0000-4000-8000-000000000001', '00000000-0000-4000-8000-000000000002',
              '18446744073709551615', '1', '6']
    return 'RQ1.' + base64.urlsafe_b64encode('\n'.join(fields).encode()).decode().rstrip('=')



def main():
    db('reset')
    a, b = client(), client()
    _, _, cookie_headers = request(client(), 'session')
    check('HttpOnly' in cookie_headers['Set-Cookie'] and 'SameSite=Lax' in cookie_headers['Set-Cookie'], 'protected session cookie')
    ha, hb = token(a), token(b)
    data = {'title': "<img src=x onerror=alert(1)> '; DROP TABLE x;--", 'code': code()}
    status, created, headers = request(a, 'create', data, ha)
    check(status == 201, 'create via HTTP')
    check('no-store' in headers['Cache-Control'], 'private mutation response not cached')
    status, listed, headers = request(b, 'list')
    check(status == 200 and listed['rooms'][0]['title'] == data['title'], 'separate browser sees the same record')
    check(listed['rooms'][0]['code'] == code(), 'complete invitation preserved')
    check(not {'delete_hash', 'delete_token', 'ip_hash'} & set(listed['rooms'][0]), 'private fields not exposed')
    check(headers.get('X-Frame-Options') == 'DENY' and "frame-ancestors 'none'" in headers['Content-Security-Policy'], 'anti-framing CSP')
    check(headers.get('Referrer-Policy') == 'no-referrer', 'deletion link cannot leak as referrer')
    check('Access-Control-Allow-Origin' not in headers, 'no cross-origin API access')
    delete = {'id': created['id'], 'token': created['delete_token']}
    check(request(b, 'delete', delete, {'Origin': BASE})[0] == 403, 'missing CSRF')
    check(request(b, 'delete', delete, ha)[0] == 403, 'CSRF token bound to browser session')
    check(request(a, 'delete', delete, {**ha, 'Origin': 'https://attacker.invalid'})[0] == 403, 'foreign origin')
    check(request(a, 'delete', delete, {'X-CSRF-Token': ha['X-CSRF-Token']})[0] == 403, 'missing origin')
    check(request(b, 'delete', {**delete, 'token': '0' * 64}, hb)[0] == 404, 'wrong deletion token')
    check(request(b, 'list')[1]['rooms'], 'rejected mutations preserved record')
    check(request(a, 'delete', delete, ha, method='GET')[0] == 405, 'GET cannot delete')
    check(request(a, 'delete', delete, ha)[0] == 200, 'owner can delete')
    check(request(b, 'list')[1]['rooms'] == [], 'deletion visible in another browser')
    check(request(a, 'delete', delete, ha)[0] == 404, 'deleted capability cannot replay')
    check(request(a, 'list', page="1' OR 1=1")[0] == 422, 'pagination injection rejected')
    check(request(a, 'list', page=51)[0] == 422, 'pagination bounded')
    status, problem, _ = request(a, 'unknown')
    check(status == 404, 'unknown action')
    check(problem['error_code'] == 'action_not_found' and 'error' in problem, 'stable error code preserves legacy error text')
    check(request(a, 'list', headers={'Host': 'attacker.invalid'})[0] == 400, 'untrusted Host rejected')
    check(request(a, 'delete', b'{', ha)[0] == 400, 'malformed JSON')
    check(request(a, 'delete', b'[]', ha)[0] == 400, 'list is not JSON object')
    check(request(a, 'delete', b'x' * 8193, ha)[0] == 413, 'request size enforced')
    check(request(a, 'delete', delete, {**ha, 'Content-Type': 'text/plain'})[0] == 415, 'JSON content type required')
    db('clear-limits')
    if 600 - int(time.time()) % 600 < 3:
        time.sleep(600 - int(time.time()) % 600 + .1)
    check(request(a, 'create', {'title': [], 'code': code()}, ha)[0] == 422, 'array cannot become title')
    check(request(a, 'create', {'title': 'Room', 'code': 'RQ1.???'}, ha)[0] == 422, 'invalid code')
    check(request(a, 'create', data, ha)[0] == 201, 'third publication attempt allowed')
    status, _, headers = request(a, 'create', data, ha)
    check(status == 429 and int(headers['Retry-After']) > 0, 'invalid attempts count towards publish limit')
    db('expire')
    check(request(b, 'list')[1]['rooms'] == [], 'expiry hidden without cleanup')
    db('reset')

    # Competing workers use different connections and browser sessions; capacity cannot be exceeded.
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        races = list(pool.map(lambda _: db('race'), range(8)))
    check(races.count('created') == 2 and db('count') == '2', 'concurrent global + per-IP capacity')
    db('reset')
    browser = client()
    h = token(browser)
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        # Same valid session, concurrent requests: session is unlocked before Store writes.
        statuses = list(pool.map(lambda _: request(browser, 'create', data, h)[0], range(8)))
    check(statuses.count(201) <= 3 and statuses.count(429) >= 5, 'publication rate limit is atomic under concurrency')
    check(int(db('count')) <= 3, 'concurrent publication bounded in MySQL')
    db('clear-limits')
    last = None
    if 60 - int(time.time()) % 60 < 3:
        time.sleep(60 - int(time.time()) % 60 + .1)
    for _ in range(121):
        last = request(b, 'list')
    check(last[0] == 429, 'general API rate limit')
    db('clear-limits')

    # Restart PHP only, not MySQL. Existing rooms must survive the server lifecycle.
    before = db('count')
    subprocess.run(COMPOSE + ['restart', 'web'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(30):
        try:
            status, records, _ = request(client(), 'list')
            if status == 200:
                break
        except (OSError, urllib.error.URLError):
            time.sleep(.5)
    check(db('count') == before and len(records['rooms']) == int(before), 'data survives PHP restart')
    db('reset')
    print(f'PASS: {count} HTTP and concurrency checks')


if __name__ == '__main__':
    main()
