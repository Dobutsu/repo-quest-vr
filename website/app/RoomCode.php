<?php
declare(strict_types=1);

namespace RepoQuest;

final class RoomCode
{
    /** @return array{code:string, region:string, build:string, version:string} */
    public static function parse(mixed $input): array
    {
        if (!is_string($input)) self::invalid();
        $code = trim($input);
        if (strlen($code) > 1024 || !preg_match('/\ARQ1\.([A-Za-z0-9_-]+)\z/D', $code, $match)) self::invalid();
        $encoded = $match[1];
        if (strlen($encoded) % 4 === 1) self::invalid();
        $payload = base64_decode(strtr($encoded, '-_', '+/') . str_repeat('=', (4 - strlen($encoded) % 4) % 4), true);
        if ($payload === false || !mb_check_encoding($payload, 'UTF-8')) self::invalid();
        // Reject ambiguous/noncanonical encodings; the game's exporter always uses this representation.
        if (rtrim(strtr(base64_encode($payload), '+/', '-_'), '=') !== $encoded) self::invalid();
        $parts = explode("\n", $payload);
        if (count($parts) !== 9) self::invalid();
        foreach ([0 => 16, 1 => 128, 2 => 64, 3 => 80] as $index => $maximum) {
            $value = $parts[$index];
            // C# string.Length counts UTF-16 code units, not UTF-8 bytes or Unicode code points.
            if (preg_match('/\A\s*\z/u', $value) || preg_match('/\p{Cc}/u', $value) ||
                strlen(mb_convert_encoding($value, 'UTF-16LE', 'UTF-8')) / 2 > $maximum) self::invalid();
        }
        if (!preg_match('/\A[a-z]{1,16}\z/D', $parts[0])) self::invalid();
        foreach ([4, 5] as $index) {
            if (!preg_match('/\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z/Di', $parts[$index]) ||
                str_replace(['0', '-'], '', $parts[$index]) === '') self::invalid();
        }
        // Lobby IDs may exceed PHP_INT_MAX. Keep both platform IDs as strings throughout.
        foreach ([6, 7] as $index) {
            if (!preg_match('/\A[0-9]{1,20}\z/D', $parts[$index])) self::invalid();
            $id = ltrim($parts[$index], '0');
            if ($id === '' || strlen($id) > 20 || (strlen($id) === 20 && strcmp($id, '18446744073709551615') > 0)) self::invalid();
        }
        if (!preg_match('/\A[0-9]{1,3}\z/D', $parts[8]) || (int)$parts[8] < 2 || (int)$parts[8] > 255) self::invalid();
        return ['code' => $code, 'region' => $parts[0], 'build' => $parts[2], 'version' => $parts[3]];
    }

    public static function title(mixed $input): string
    {
        if (!is_string($input) || !mb_check_encoding($input, 'UTF-8')) throw new HttpError(422, 'Введите название комнаты.', errorCode: 'title_required');
        $title = trim($input);
        if ($title === '' || mb_strlen($title, 'UTF-8') > 60 || preg_match('/[\p{Cc}\p{Cf}]/u', $title))
            throw new HttpError(422, 'Название: от 1 до 60 символов, без переносов и скрытых символов.', errorCode: 'invalid_title');
        // Markup is never interpreted: templates escape it; the browser renders it using textContent.
        return $title;
    }

    private static function invalid(): never
    {
        throw new HttpError(422, 'Нужен полный корректный RQ1-код из игры, длиной до 1024 символов.', errorCode: 'invalid_room_code');
    }
}
