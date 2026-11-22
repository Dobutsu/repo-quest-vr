<?php
declare(strict_types=1);

require dirname(__DIR__) . '/app/bootstrap.php';

use RepoQuest\{HttpError, RoomCode, Security, Store};

Security::headers();
header('Content-Type: application/json; charset=utf-8');

try {
    $config = rq_config();
    Security::transport($config);
    $method = $_SERVER['REQUEST_METHOD'] ?? '';
    $action = $_GET['action'] ?? '';
    if (!is_string($action) || !in_array($action, ['session', 'list', 'create', 'delete'], true)) throw new HttpError(404, 'Действие не найдено.', errorCode: 'action_not_found');
    if ($method !== (in_array($action, ['session', 'list'], true) ? 'GET' : 'POST')) {
        header('Allow: ' . (in_array($action, ['session', 'list'], true) ? 'GET' : 'POST'));
        throw new HttpError(405, 'Недопустимый метод запроса.', errorCode: 'method_not_allowed');
    }
    $store = new Store($config);
    $ip = $store->ipHash($_SERVER['REMOTE_ADDR'] ?? 'unknown');
    $store->rate('api', $ip, $config['limits']['api_per_minute'], 60);
    $csrf = Security::sessionToken($config);
    if ($action === 'session') {
        $result = ['csrf' => $csrf];
    } elseif ($action === 'list') {
        $page = $_GET['page'] ?? '1';
        if (!is_string($page) || !preg_match('/\A[1-9][0-9]?\z/D', $page) || (int)$page > 50) throw new HttpError(422, 'Неверная страница.', errorCode: 'invalid_page');
        $result = $store->listing((int)$page);
    } else {
        if ($action === 'create') $store->rate('publish', $ip, $config['limits']['publish_per_ten_minutes'], 600);
        $data = Security::mutation($config, $csrf);
        if ($action === 'create') {
            $result = $store->create(RoomCode::title($data['title'] ?? null), RoomCode::parse($data['code'] ?? null), $ip);
            http_response_code(201);
        } else {
            $store->delete($data['id'] ?? null, $data['token'] ?? null);
            $result = ['deleted' => true];
        }
    }
    echo json_encode($result, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_HEX_TAG | JSON_HEX_AMP);
} catch (HttpError $error) {
    http_response_code($error->status);
    if ($error->retryAfter > 0) header('Retry-After: ' . $error->retryAfter);
    echo json_encode(['error' => $error->getMessage(), 'error_code' => $error->errorCode], JSON_UNESCAPED_UNICODE | JSON_HEX_TAG | JSON_HEX_AMP);
} catch (Throwable $error) {
    $reference = bin2hex(random_bytes(4));
    error_log('[RepoQuest] ' . $error::class . ' reference=' . $reference);
    http_response_code(503);
    echo json_encode(['error' => 'Сервис временно недоступен. Попробуйте позже.', 'error_code' => 'unavailable', 'reference' => $reference], JSON_UNESCAPED_UNICODE);
}
