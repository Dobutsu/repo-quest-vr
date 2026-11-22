<?php
// Only for the isolated local Docker environment. Not included in the release ZIP.
return [
    'origin' => 'http://127.0.0.1:8089',
    'allow_http_local' => true,
    'db' => ['dsn' => 'mysql:host=db;dbname=repoquest;charset=utf8mb4', 'user' => 'repoquest', 'password' => 'local-test-only'],
    'ip_secret' => str_repeat('a', 64),
    'limits' => ['api_per_minute' => 120, 'publish_per_ten_minutes' => 3, 'active_per_ip' => 10, 'active_total' => 1000],
];
