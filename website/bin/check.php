<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
require dirname(__DIR__) . '/app/bootstrap.php';
try {
    if (PHP_VERSION_ID < 80400) throw new RuntimeException('Use PHP 8.4 or newer.');
    foreach (['pdo_mysql', 'mbstring'] as $extension)
        if (!extension_loaded($extension)) throw new RuntimeException('Missing extension: ' . $extension);
    $store = new RepoQuest\Store(rq_config());
    $store->listing(1);
    $store->db->query('SELECT bucket,hits,expires_at FROM rq_limits LIMIT 0');
    if ($store->db->query('SELECT id FROM rq_guard WHERE id=1')->fetchColumn() === false)
        throw new RuntimeException('Import the complete schema.sql, including rq_guard.');
    echo 'OK: PHP ' . PHP_VERSION . ", required extensions, configuration and database schema.\n";
} catch (Throwable $error) {
    fwrite(STDERR, 'Check failed: ' . $error::class . '. Verify PHP, config.php, database credentials and schema.sql.' . PHP_EOL);
    exit(1);
}
