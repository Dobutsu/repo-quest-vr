-- Import into a NEW, dedicated MySQL 8 database using phpMyAdmin.
CREATE TABLE IF NOT EXISTS rq_rooms (
    id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    title VARCHAR(60) NOT NULL,
    code VARCHAR(1024) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    region VARCHAR(16) CHARACTER SET ascii NOT NULL,
    game_build VARCHAR(64) NOT NULL,
    app_version VARCHAR(80) NOT NULL,
    delete_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ip_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at BIGINT UNSIGNED NOT NULL,
    expires_at BIGINT UNSIGNED NOT NULL,
    INDEX rq_rooms_expiry (expires_at),
    INDEX rq_rooms_recent (created_at, id),
    INDEX rq_rooms_ip (ip_hash, expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS rq_limits (
    bucket CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    hits INT UNSIGNED NOT NULL,
    expires_at BIGINT UNSIGNED NOT NULL,
    INDEX rq_limits_expiry (expires_at)
) ENGINE=InnoDB;

-- Serializes capacity checks + inserts, including requests from different PHP workers.
CREATE TABLE IF NOT EXISTS rq_guard (id TINYINT UNSIGNED PRIMARY KEY) ENGINE=InnoDB;
INSERT IGNORE INTO rq_guard (id) VALUES (1);
