<?php
declare(strict_types=1);

ini_set('display_errors', '0');
ini_set('log_errors', '1');
require_once __DIR__ . '/HttpError.php';
require_once __DIR__ . '/RoomCode.php';
require_once __DIR__ . '/Store.php';
require_once __DIR__ . '/Security.php';
require_once __DIR__ . '/I18n.php';

function rq_config(): array
{
    $path = getenv('REPOQUEST_CONFIG') ?: dirname(__DIR__) . '/config.php';
    if (!is_file($path)) throw new RuntimeException('Configuration missing');
    $config = require $path;
    if (!is_array($config) || !preg_match('/\A[0-9a-f]{64}\z/Di', $config['ip_secret'] ?? '') ||
        ($config['ip_secret'] ?? '') === str_repeat('0', 64)) throw new RuntimeException('Configuration invalid');
    $origin = parse_url($config['origin'] ?? '');
    $local = ($config['allow_http_local'] ?? false) && in_array($origin['host'] ?? '', ['localhost', '127.0.0.1', '[::1]'], true);
    if (!$origin || !in_array($origin['scheme'] ?? '', $local ? ['http', 'https'] : ['https'], true) ||
        !isset($origin['host']) || isset($origin['user']) || isset($origin['pass']) || isset($origin['query']) || isset($origin['fragment']) ||
        !empty($origin['path'])) throw new RuntimeException('Origin invalid');
    foreach (['api_per_minute', 'publish_per_ten_minutes', 'active_per_ip', 'active_total'] as $key) {
        if (!is_int($config['limits'][$key] ?? null) || $config['limits'][$key] < 1) throw new RuntimeException('Limits invalid');
    }
    return $config;
}
