<?php
declare(strict_types=1);
require __DIR__ . '/support.php';
$config = test_config();
$store = new RepoQuest\Store($config);
switch ($argv[1] ?? '') {
    case 'reset': test_reset($store); break;
    case 'clear-limits': $store->db->exec('DELETE FROM rq_limits'); break;
    case 'count': echo $store->db->query('SELECT COUNT(*) FROM rq_rooms')->fetchColumn(); break;
    case 'expire': $store->db->exec('UPDATE rq_rooms SET expires_at=1'); break;
    case 'fixture':
        test_reset($store);
        foreach (['Вечерняя смена', 'Осторожно, хрупкое!', '<script>alert(1)</script>'] as $i => $name)
            $store->create($name, RepoQuest\RoomCode::parse(test_code()), $store->ipHash('fixture-' . $i));
        break;
    case 'race':
        $config['limits']['active_total'] = 2;
        $config['limits']['active_per_ip'] = 2;
        try { (new RepoQuest\Store($config))->create('concurrent', RepoQuest\RoomCode::parse(test_code()), $store->ipHash('race')); echo 'created'; }
        catch (RepoQuest\HttpError $error) { echo (string)$error->status; }
        break;
    default: throw new RuntimeException('Unknown test command');
}
