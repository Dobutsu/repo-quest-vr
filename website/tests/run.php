<?php
declare(strict_types=1);
require __DIR__ . '/support.php';
use RepoQuest\{HttpError, RoomCode, Security, Store};

$count = 0;
function check(bool $value, string $name): void {
    global $count;
    if (!$value) throw new RuntimeException('FAIL: ' . $name);
    $count++;
}
function rejects(callable $action, int $status = 422): void {
    try { $action(); } catch (HttpError $error) { check($error->status === $status, 'rejection status'); return; }
    throw new RuntimeException('Expected rejection');
}

$config = test_config();
$store = new Store($config);
test_reset($store);
$_SERVER['HTTP_HOST'] = 'rooms.example.org';
$_SERVER['HTTPS'] = 'off';
$_SERVER['SERVER_PORT'] = '80';
rejects(fn() => Security::transport(['origin' => 'https://rooms.example.org']), 426);
$_SERVER['HTTPS'] = 'on';
Security::transport(['origin' => 'https://rooms.example.org']);
$_SERVER['HTTP_HOST'] = 'attacker.invalid';
rejects(fn() => Security::transport(['origin' => 'https://rooms.example.org']), 400);
$csrf = Security::sessionToken(['origin' => 'https://rooms.example.org']);
$cookies = session_get_cookie_params();
check(strlen($csrf) === 64 && session_name() === '__Host-rq-session', 'production session token and host-prefixed cookie');
check($cookies['secure'] && $cookies['httponly'] && $cookies['samesite'] === 'Lax' && $cookies['path'] === '/' && $cookies['domain'] === '', 'production secure cookie flags');

$sample = test_code([1 => 'fixture-room', 6 => '12345', 7 => '1']);
check(RoomCode::parse($sample)['region'] === 'jp', 'synthetic RQ1 invitation');
check(RoomCode::parse(test_code())['build'] === 'v0.5.0.net3', 'game-exporter format');
check(RoomCode::parse(" \n" . test_code() . "\r\n")['code'] === test_code(), 'trim outer whitespace');
check(RoomCode::parse(test_code([6 => '18446744073709551615']))['region'] === 'jp', 'uint64 maximum beyond signed PHP int');
check(RoomCode::parse(test_code([1 => str_repeat('я', 128)]))['region'] === 'jp', 'UTF-8 room');
check(RoomCode::parse(test_code([1 => "room'; DROP TABLE rq_rooms; --"]))['region'] === 'jp', 'room data is not executable');
foreach ([null, [], '', 'RQ2.a', 'RQ1.a', 'RQ1.???', test_code() . '=', 'RQ1.' . str_repeat('a', 1025),
    'RQ1.' . rtrim(strtr(base64_encode("\xff"), '+/', '-_'), '='), test_code([0 => 'eu1']), test_code([0 => str_repeat('a', 17)]),
    test_code([1 => '']), test_code([1 => "room\0"]), test_code([1 => str_repeat('я', 129)]), test_code([1 => str_repeat('😀', 65)]),
    test_code([2 => str_repeat('x', 65)]), test_code([3 => str_repeat('x', 81)]), test_code([4 => '00000000-0000-0000-0000-000000000000']),
    test_code([5 => 'not-guid']), test_code([6 => '0']), test_code([7 => '18446744073709551616']), test_code([6 => '-1']),
    test_code([7 => '1e10']), test_code([8 => '256']), test_code([8 => '1']), test_code([8 => '6x']), test_code([9 => 'extra'])] as $bad)
    rejects(fn() => RoomCode::parse($bad));
foreach (['', [], str_repeat('а', 61), "name\nnew", "hidden\u{202e}", "bad\xff"] as $bad) rejects(fn() => RoomCode::title($bad));
check(RoomCode::title("  Комната  ") === 'Комната', 'trim title');
$attack = "<img src=x onerror=alert(1)> '; --";
check(RoomCode::title($attack) === $attack, 'markup remains plain data');
$ip = $store->ipHash('192.0.2.1');
check(strlen($ip) === 64 && $ip !== $store->ipHash('192.0.2.2'), 'HMAC network key');
$created = $store->create($attack, RoomCode::parse(test_code()), $ip);
$listed = $store->listing(1)['rooms'];
check(count($listed) === 1 && $listed[0]['title'] === $attack, 'prepared query round trip');
check(count(array_intersect(['delete_hash', 'ip_hash', 'delete_token'], array_keys($listed[0]))) === 0, 'no private metadata in listing');
check($created['expires_at'] - (int)$listed[0]['created_at'] === 86400, '24-hour lifetime');
$storedHash = $store->db->query('SELECT delete_hash FROM rq_rooms')->fetchColumn();
check($storedHash === hash('sha256', $created['delete_token']) && $storedHash !== $created['delete_token'], 'hashed deletion capability');
rejects(fn() => $store->delete($created['id'], str_repeat('0', 64)), 404);
rejects(fn() => $store->delete("' OR 1=1 --", $created['delete_token']), 404);
$store->delete($created['id'], $created['delete_token']);
check(count($store->listing(1)['rooms']) === 0, 'owner deletion');
rejects(fn() => $store->delete($created['id'], $created['delete_token']), 404);
$created = $store->create('expired', RoomCode::parse(test_code()), $ip);
$store->db->exec('UPDATE rq_rooms SET expires_at=1');
check(count($store->listing(1)['rooms']) === 0, 'expired hidden without cron');
rejects(fn() => $store->delete($created['id'], $created['delete_token']), 404);
$store->cleanup();
check((int)$store->db->query('SELECT COUNT(*) FROM rq_rooms')->fetchColumn() === 0, 'expired records purged');
$store->rate('test', $ip, 2, 600); $store->rate('test', $ip, 2, 600);
rejects(fn() => $store->rate('test', $ip, 2, 600), 429);
$store->db->exec('UPDATE rq_limits SET expires_at=1'); $store->cleanup();
check((int)$store->db->query('SELECT COUNT(*) FROM rq_limits')->fetchColumn() === 0, 'rate counters purged');
$limitedConfig = $config; $limitedConfig['limits']['active_total'] = 2; $limitedConfig['limits']['active_per_ip'] = 1;
$limited = new Store($limitedConfig);
$limited->create('first', RoomCode::parse(test_code()), $ip);
rejects(fn() => $limited->create('second', RoomCode::parse(test_code()), $ip), 429);
$limited->create('other IP', RoomCode::parse(test_code()), $store->ipHash('192.0.2.2'));
rejects(fn() => $limited->create('global cap', RoomCode::parse(test_code()), $store->ipHash('192.0.2.3')), 503);
test_reset($store);
for ($i = 0; $i < 21; $i++) $store->create("room $i", RoomCode::parse(test_code()), $store->ipHash((string)$i));
check(count($store->listing(1)['rooms']) === 20 && $store->listing(1)['has_more'], 'pagination first page');
check(count($store->listing(2)['rooms']) === 1 && !$store->listing(2)['has_more'], 'pagination last page');
$store->db->exec('DELETE FROM rq_guard');
try { $store->create('fail closed', RoomCode::parse(test_code()), $ip); throw new LogicException('Guard not enforced'); }
catch (RuntimeException $error) { check($error->getMessage() === 'Capacity guard missing', 'missing schema guard fails closed'); }
test_reset($store);
echo "PASS: $count parser and MySQL checks\n";
