<?php
declare(strict_types=1);

// Copy to config.php OUTSIDE public_html. Never commit the completed file.
return [
    'origin' => 'https://rooms.example.org',
    'allow_http_local' => false,
    'db' => [
        'dsn' => 'mysql:host=localhost;dbname=YOUR_DATABASE;charset=utf8mb4',
        'user' => 'YOUR_DATABASE_USER',
        'password' => 'YOUR_DATABASE_PASSWORD',
    ],
    // Generate with: php -r "echo bin2hex(random_bytes(32)), PHP_EOL;"
    'ip_secret' => 'REPLACE_WITH_64_RANDOM_HEX_CHARACTERS',
    'limits' => [
        'api_per_minute' => 120,
        'publish_per_ten_minutes' => 3,
        'active_per_ip' => 10,
        'active_total' => 1000,
    ],
];
