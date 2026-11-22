<?php
declare(strict_types=1);
namespace RepoQuest;

final class HttpError extends \RuntimeException
{
    public function __construct(public readonly int $status, string $message, public readonly int $retryAfter = 0,
        public readonly string $errorCode = 'request_failed')
    {
        parent::__construct($message);
    }
}
