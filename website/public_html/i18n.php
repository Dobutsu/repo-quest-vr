<?php
declare(strict_types=1);
require dirname(__DIR__) . '/app/bootstrap.php';
RepoQuest\Security::headers();
header('Content-Type: application/javascript; charset=utf-8');
echo 'window.RQ_I18N = ' . json_encode([
    'locale' => rq_locale(),
    'translations' => rq_translations(),
    'errors' => RQ_ERRORS,
], JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT) . ';';
