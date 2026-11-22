<?php
declare(strict_types=1);
namespace RepoQuest;

use PDO;

final class Store
{
    public readonly PDO $db;

    public function __construct(private readonly array $config)
    {
        $this->db = new PDO($config['db']['dsn'], $config['db']['user'], $config['db']['password'], [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_EMULATE_PREPARES => false,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_STRINGIFY_FETCHES => false,
        ]);
    }

    public function ipHash(string $address): string
    {
        return hash_hmac('sha256', $address, $this->config['ip_secret']);
    }

    public function rate(string $category, string $ipHash, int $limit, int $seconds): void
    {
        $now = time();
        $end = (intdiv($now, $seconds) + 1) * $seconds;
        $bucket = hash('sha256', $category . ':' . $ipHash . ':' . intdiv($now, $seconds));
        $query = $this->db->prepare('INSERT INTO rq_limits (bucket,hits,expires_at) VALUES (?,1,?) ON DUPLICATE KEY UPDATE hits=LEAST(hits+1,1000000)');
        $query->execute([$bucket, $end]);
        $query = $this->db->prepare('SELECT hits FROM rq_limits WHERE bucket=?');
        $query->execute([$bucket]);
        if ((int)$query->fetchColumn() > $limit) throw new HttpError(429, 'Слишком много запросов. Подождите и попробуйте снова.', $end - $now, errorCode: 'rate_limited');
    }

    public function listing(int $page): array
    {
        $now = time();
        $query = $this->db->prepare('SELECT id,title,code,region,game_build,app_version,created_at,expires_at FROM rq_rooms WHERE expires_at>? ORDER BY created_at DESC,id DESC LIMIT 21 OFFSET ?');
        $query->bindValue(1, $now, PDO::PARAM_INT);
        $query->bindValue(2, ($page - 1) * 20, PDO::PARAM_INT);
        $query->execute();
        $rows = $query->fetchAll();
        return ['rooms' => array_slice($rows, 0, 20), 'has_more' => count($rows) > 20, 'server_time' => $now];
    }

    public function create(string $title, array $code, string $ipHash): array
    {
        $now = time();
        $id = bin2hex(random_bytes(16));
        $token = bin2hex(random_bytes(32));
        $this->db->beginTransaction();
        try {
            if ($this->db->query('SELECT id FROM rq_guard WHERE id=1 FOR UPDATE')->fetchColumn() === false)
                throw new \RuntimeException('Capacity guard missing');
            $query = $this->db->prepare('SELECT COUNT(*) FROM rq_rooms WHERE expires_at>?');
            $query->execute([$now]);
            if ((int)$query->fetchColumn() >= $this->config['limits']['active_total'])
                throw new HttpError(503, 'Список временно заполнен. Попробуйте позже.', errorCode: 'board_full');
            $query = $this->db->prepare('SELECT COUNT(*) FROM rq_rooms WHERE ip_hash=? AND expires_at>?');
            $query->execute([$ipHash, $now]);
            if ((int)$query->fetchColumn() >= $this->config['limits']['active_per_ip'])
                throw new HttpError(429, 'С вашего подключения уже опубликовано достаточно активных комнат.', errorCode: 'active_room_limit');
            $query = $this->db->prepare('INSERT INTO rq_rooms (id,title,code,region,game_build,app_version,delete_hash,ip_hash,created_at,expires_at) VALUES (?,?,?,?,?,?,?,?,?,?)');
            $query->execute([$id, $title, $code['code'], $code['region'], $code['build'], $code['version'], hash('sha256', $token), $ipHash, $now, $now + 86400]);
            $this->db->commit();
            return ['id' => $id, 'delete_token' => $token, 'expires_at' => $now + 86400];
        } catch (\Throwable $error) {
            if ($this->db->inTransaction()) $this->db->rollBack();
            throw $error;
        }
    }

    public function delete(mixed $id, mixed $token): void
    {
        if (!is_string($id) || !is_string($token) || !preg_match('/\A[a-f0-9]{32}\z/D', $id) || !preg_match('/\A[a-f0-9]{64}\z/D', $token))
            throw new HttpError(404, 'Комната не найдена, уже удалена или ссылка неверна.', errorCode: 'room_not_found');
        $query = $this->db->prepare('DELETE FROM rq_rooms WHERE id=? AND delete_hash=? AND expires_at>?');
        $query->execute([$id, hash('sha256', $token), time()]);
        if ($query->rowCount() !== 1) throw new HttpError(404, 'Комната не найдена, уже удалена или ссылка неверна.', errorCode: 'room_not_found');
    }

    public function cleanup(): void
    {
        foreach (['rq_rooms', 'rq_limits'] as $table) {
            $query = $this->db->prepare("DELETE FROM $table WHERE expires_at<=?"); // fixed allowlist, never request input
            $query->execute([time()]);
        }
    }
}
