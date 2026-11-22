<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
require dirname(__DIR__) . '/app/bootstrap.php';
(new RepoQuest\Store(rq_config()))->cleanup();
echo "Expired rooms and rate counters removed.\n";
