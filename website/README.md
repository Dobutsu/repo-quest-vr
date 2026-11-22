# Room-code website

Players can publish and copy RQ1 invitations for Cloud co-op. Posts expire after 24 hours. The publisher can also
remove a post using its private deletion link. Published codes are visible to visitors, and the site validates
the code format without querying Photon to verify the lobby.

## Deployment

Use PHP 8.4 or later with `pdo_mysql`, `mbstring`, JSON, sessions and hash support, plus MySQL 8.0 with InnoDB.

1. Run `python website/bin/package.py` and extract `dist/repo-rooms.zip` on the server. Set the HTTPS virtual host's
   document root to `public_html`. Keep `app`, `bin`, the config and the schema outside the document root.
2. Import `schema.sql` into a dedicated database and configure a database user for the application.
3. Copy `config.example.php` to `config.php`. Set the database credentials and `origin` to the site's public HTTPS
   origin, such as `https://rooms.example.org`. Leave `allow_http_local` disabled in production.
4. Generate `ip_secret` with `php -r 'echo bin2hex(random_bytes(32)), PHP_EOL;'`. Limit access to `config.php` to
   the deployment user and PHP process. Keep the completed file outside source control.
5. Verify the configuration with `php bin/check.php` and schedule `php bin/cleanup.php` hourly to purge expired data.

Configure the web server to redirect HTTP to the site's fixed HTTPS origin. The application also checks the
request host and transport. It uses `REMOTE_ADDR` for rate limits, so configure trusted proxies at the web-server
layer if needed. Preserve the database, config and `ip_secret` when updating the site.

In the headset, enter the public URL under **Network play → Cloud settings**. After saving and restarting, use
**Open website** to access the room board. Publishing an invitation is a manual step.

## Localization

The browser's primary language selects Russian or English. An explicit choice takes priority and is stored in
the `rq-language` cookie. Use stable keys in `app/translations.json` when editing either translation. The API provides
stable `error_code` values for localized display.

## Local tests

From the repository root, start the Docker test stack. It serves the site at `http://127.0.0.1:8089` with an
isolated MySQL database.

```sh
docker compose -f website/tests/compose.yaml up -d --build --wait
docker compose -f website/tests/compose.yaml exec -T web php bin/check.php
docker compose -f website/tests/compose.yaml exec -T web php tests/run.php
docker compose -f website/tests/compose.yaml exec -T web php tests/i18n_tests.php
python website/tests/catalog_tests.py
python website/tests/http_tests.py
```

With Playwright and Chromium installed, run `python website/tests/browser_tests.py` and
`python website/tests/localization_tests.py`. Stop the stack with `docker compose -f website/tests/compose.yaml down`.

The font licenses for Golos Text and Russo One are in `public_html/assets`.
