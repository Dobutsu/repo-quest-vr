<?php
declare(strict_types=1);
require dirname(__DIR__) . '/app/bootstrap.php';

function test_config(): array
{
    $config = rq_config();
    if ($config['origin'] !== 'http://127.0.0.1:8089' || $config['db']['dsn'] !== 'mysql:host=db;dbname=repoquest;charset=utf8mb4')
        throw new RuntimeException('Tests only run against the isolated Docker database.');
    return $config;
}

function test_code(array $overrides = []): string
{
    $parts = ['jp', 'fixture-room', 'v0.5.0.net3', 'v0.5.0.net3',
        '00000000-0000-4000-8000-000000000001', '00000000-0000-4000-8000-000000000002',
        '12345', '1', '6'];
    return 'RQ1.' . rtrim(strtr(base64_encode(implode("\n", array_replace($parts, $overrides))), '+/', '-_'), '=');
}

function test_reset(RepoQuest\Store $store): void
{
    $store->db->exec('DELETE FROM rq_rooms');
    $store->db->exec('DELETE FROM rq_limits');
    $store->db->exec('INSERT IGNORE INTO rq_guard (id) VALUES (1)');
}
