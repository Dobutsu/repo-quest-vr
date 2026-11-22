<?php
declare(strict_types=1);
namespace RepoQuest;

final class Security
{
    public static function headers(): void
    {
        header_remove('X-Powered-By');
        header("Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'");
        header('X-Content-Type-Options: nosniff');
        header('X-Frame-Options: DENY');
        header('Referrer-Policy: no-referrer');
        header('Permissions-Policy: camera=(), microphone=(), geolocation=()');
        header('Cache-Control: no-store');
        header('X-Robots-Tag: noindex, nofollow');
    }

    public static function transport(array $config): void
    {
        $origin = parse_url($config['origin']);
        $host = $origin['host'] . (isset($origin['port']) ? ':' . $origin['port'] : '');
        if (strcasecmp($_SERVER['HTTP_HOST'] ?? '', $host) !== 0) throw new HttpError(400, 'Неверный адрес сайта.', errorCode: 'invalid_host');
        if ($origin['scheme'] === 'https') {
            if (($_SERVER['HTTPS'] ?? '') !== 'on' && (string)($_SERVER['SERVER_PORT'] ?? '') !== '443')
                throw new HttpError(426, 'Откройте сайт по HTTPS.', errorCode: 'https_required');
            header('Strict-Transport-Security: max-age=31536000');
        }
    }

    public static function sessionToken(array $config): string
    {
        $secure = str_starts_with($config['origin'], 'https://');
        ini_set('session.use_strict_mode', '1');
        ini_set('session.use_only_cookies', '1');
        ini_set('session.use_trans_sid', '0');
        session_name($secure ? '__Host-rq-session' : 'rq_session');
        session_set_cookie_params(['lifetime' => 0, 'path' => '/', 'secure' => $secure, 'httponly' => true, 'samesite' => 'Lax']);
        if (!session_start()) throw new \RuntimeException('Session unavailable');
        $_SESSION['csrf'] ??= bin2hex(random_bytes(32));
        $token = $_SESSION['csrf'];
        session_write_close(); // no session lock while waiting for MySQL
        return $token;
    }

    public static function mutation(array $config, string $csrf): array
    {
        if (($_SERVER['HTTP_ORIGIN'] ?? '') !== $config['origin']) throw new HttpError(403, 'Обновите страницу и повторите действие.', errorCode: 'refresh_required');
        if (!hash_equals($csrf, $_SERVER['HTTP_X_CSRF_TOKEN'] ?? '')) throw new HttpError(403, 'Обновите страницу и повторите действие.', errorCode: 'refresh_required');
        if (strtolower(trim(explode(';', $_SERVER['CONTENT_TYPE'] ?? '')[0])) !== 'application/json')
            throw new HttpError(415, 'Нужен JSON-запрос.', errorCode: 'json_required');
        if (isset($_SERVER['CONTENT_LENGTH']) && (int)$_SERVER['CONTENT_LENGTH'] > 8192)
            throw new HttpError(413, 'Запрос слишком большой.', errorCode: 'request_too_large');
        $body = file_get_contents('php://input', false, null, 0, 8193);
        if ($body === false || strlen($body) > 8192) throw new HttpError(413, 'Запрос слишком большой.', errorCode: 'request_too_large');
        try { $data = json_decode($body, true, 16, JSON_THROW_ON_ERROR); }
        catch (\JsonException) { throw new HttpError(400, 'Некорректный запрос.', errorCode: 'invalid_request'); }
        if (!is_array($data) || array_is_list($data)) throw new HttpError(400, 'Некорректный запрос.', errorCode: 'invalid_request');
        return $data;
    }
}
