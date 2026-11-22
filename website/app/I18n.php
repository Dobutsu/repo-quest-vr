<?php
declare(strict_types=1);

/** The first browser language decides the default; a valid manual choice takes priority. */
function rq_locale(): string
{
    $saved = $_COOKIE['rq-language'] ?? null;
    if (is_string($saved) && in_array($saved, ['ru', 'en'], true)) return $saved;
    $preferred = trim(explode(',', $_SERVER['HTTP_ACCEPT_LANGUAGE'] ?? '')[0]);
    return preg_match('/\Aru(?:-|;|\z)/i', $preferred) ? 'ru' : 'en';
}

function rq_translations(): array
{
    static $catalog;
    return $catalog ??= json_decode(file_get_contents(__DIR__ . '/translations.json'), true, 512, JSON_THROW_ON_ERROR);
}

function rq_message(string $key): string
{
    return rq_translations()[$key][rq_locale()] ?? throw new LogicException('Unknown translation key: ' . $key);
}

/** Escape all translated output, including attribute values. Markup remains in the template. */
function rq_t(string $key): string
{
    return htmlspecialchars(rq_message($key), ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

const RQ_ERRORS = [
    'action_not_found' => 'error.action_not_found',
    'method_not_allowed' => 'error.method_not_allowed',
    'invalid_page' => 'error.invalid_page',
    'title_required' => 'error.title_required',
    'invalid_title' => 'error.invalid_title',
    'invalid_room_code' => 'error.invalid_room_code',
    'invalid_host' => 'error.invalid_host',
    'https_required' => 'error.https_required',
    'refresh_required' => 'error.refresh_required',
    'json_required' => 'error.json_required',
    'request_too_large' => 'error.request_too_large',
    'invalid_request' => 'error.invalid_request',
    'rate_limited' => 'error.rate_limited',
    'board_full' => 'error.board_full',
    'active_room_limit' => 'error.active_room_limit',
    'room_not_found' => 'error.room_not_found',
    'unavailable' => 'error.unavailable',
];
