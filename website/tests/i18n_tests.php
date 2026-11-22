<?php
declare(strict_types=1);
require dirname(__DIR__) . '/app/I18n.php';

foreach (['ru-RU,ru;q=0.9' => 'ru', 'ru' => 'ru', 'en-US,ru;q=0.8' => 'en', 'de-DE' => 'en', '' => 'en'] as $header => $expected) {
    $_COOKIE = [];
    $_SERVER['HTTP_ACCEPT_LANGUAGE'] = $header;
    if (rq_locale() !== $expected) throw new RuntimeException('Incorrect language default');
    if (rq_message('page.title') !== rq_translations()['page.title'][$expected]) throw new RuntimeException('Incorrect translation');
}
$_SERVER['HTTP_ACCEPT_LANGUAGE'] = 'en-US';
$_COOKIE['rq-language'] = 'ru';
if (rq_message('rooms.title') !== 'Комнаты') throw new RuntimeException('Manual selection ignored');
$_COOKIE['rq-language'] = ['ru'];
if (rq_locale() !== 'en') throw new RuntimeException('Invalid cookie accepted');
try {
    rq_message('missing.translation');
    throw new RuntimeException('Missing translation silently accepted');
} catch (LogicException) { }
foreach (RQ_ERRORS as $code => $key) {
    if ($key !== 'error.' . $code || !isset(rq_translations()[$key])) throw new RuntimeException('Unknown error translation');
}
echo "PASS: server language defaults, saved selection, neutral key lookup, missing-key rejection and API error mapping\n";
