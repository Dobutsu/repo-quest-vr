<?php
declare(strict_types=1);
require dirname(__DIR__) . '/app/bootstrap.php';
require_once dirname(__DIR__) . '/app/I18n.php';
RepoQuest\Security::headers();
try {
    $config = rq_config();
    RepoQuest\Security::transport($config);
} catch (RepoQuest\HttpError $error) {
    http_response_code($error->status);
    header('Content-Type: text/plain; charset=utf-8');
    exit(rq_message(RQ_ERRORS[$error->errorCode] ?? 'error.request_failed'));
} catch (Throwable) {
    http_response_code(503);
    header('Content-Type: text/plain; charset=utf-8');
    exit(rq_message('error.site_unconfigured'));
}
header('Content-Type: text/html; charset=utf-8');
$siteCssVersion = substr(hash_file('sha256', __DIR__ . '/assets/site.css'), 0, 12);
$siteJsVersion = substr(hash_file('sha256', __DIR__ . '/assets/site.js'), 0, 12);
?>
<!doctype html>
<html lang="<?= rq_locale() ?>">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="robots" content="noindex,nofollow">
    <meta name="theme-color" content="#151615">
    <meta name="rq-origin" content="<?= htmlspecialchars($config['origin'], ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8') ?>">
    <title data-i18n="page.title"><?= rq_t('page.title') ?></title>
    <meta name="description" content="<?= rq_t('page.description') ?>" data-i18n-content="page.description">
    <link rel="icon" href="/assets/favicon.svg" type="image/svg+xml">
    <link rel="stylesheet" href="/assets/site.css?v=<?= $siteCssVersion ?>">
    <script src="/i18n.php"></script>
    <script src="/assets/i18n.js" defer></script>
    <script src="/assets/site.js?v=<?= $siteJsVersion ?>" defer></script>
</head>
<body>
<a class="skip" href="#rooms"><span data-i18n="navigation.skip_rooms"><?= rq_t('navigation.skip_rooms') ?></span></a>
<div class="shell">
    <header class="masthead">
        <a class="brand" href="/" aria-label="<?= rq_t('brand.home_label') ?>" data-i18n-aria-label="brand.home_label"><span class="brand-symbol" aria-hidden="true">R<span>↗</span></span><span>REPO<span class="brand-light">LINK</span><small><span data-i18n="brand.tagline"><?= rq_t('brand.tagline') ?></span></small></span></a>
        <span class="masthead-note"><span data-i18n="brand.unofficial"><?= rq_t('brand.unofficial') ?></span></span>
        <nav class="language-switch" aria-label="<?= rq_t('language.label') ?>" data-i18n-aria-label="language.label"><button type="button" data-language="ru" lang="ru" aria-pressed="<?= rq_locale() === 'ru' ? 'true' : 'false' ?>">RU</button><button type="button" data-language="en" lang="en" aria-pressed="<?= rq_locale() === 'en' ? 'true' : 'false' ?>">EN</button></nav>
        <span class="protocol">RQ1 <span aria-hidden="true">●</span></span>
    </header>

    <section class="hero" aria-labelledby="hero-title">
        <div class="hero-copy">
            <p class="eyebrow"><span class="small-square" aria-hidden="true"></span> R.E.P.O. / CLOUD CO-OP</p>
            <h1 id="hero-title"><span data-i18n="hero.title"><?= rq_t('hero.title') ?></span><br><span><span data-i18n="hero.subtitle"><?= rq_t('hero.subtitle') ?></span></span></h1>
            <p class="hero-description"><span data-i18n="hero.create_room"><?= rq_t('hero.create_room') ?></span><br class="desktop-break"> <span data-i18n="hero.connect"><?= rq_t('hero.connect') ?></span></p>
        </div>
        <div class="transit" aria-hidden="true">
            <div class="transit-top"><span><span data-i18n="transfer.title"><?= rq_t('transfer.title') ?></span></span><span>01 — 02</span></div>
            <div class="transit-diagram"><span class="device pc"><i></i><span data-i18n="transfer.pc"><?= rq_t('transfer.pc') ?></span></span><span class="signal"><b></b><b></b><b></b><span>RQ1</span></span><span class="device headset"><i><em></em><em></em></i>QUEST</span></div>
            <div class="transit-bottom"><span><span data-i18n="transfer.no_signup"><?= rq_t('transfer.no_signup') ?></span></span><span><span data-i18n="transfer.expiry"><?= rq_t('transfer.expiry') ?></span></span></div>
        </div>
    </section>

    <div class="instruction-strip" aria-label="<?= rq_t('guide.connect_label') ?>" data-i18n-aria-label="guide.connect_label">
        <span><b>01</b> <span data-i18n="guide.create_game"><?= rq_t('guide.create_game') ?></span></span><i aria-hidden="true">→</i><span><b>02</b> <span data-i18n="guide.post_code"><?= rq_t('guide.post_code') ?></span></span><i aria-hidden="true">→</i><span><b>03</b> <span data-i18n="guide.connect_code"><?= rq_t('guide.connect_code') ?></span></span>
    </div>

    <main class="workspace">
        <section class="board" id="rooms" aria-labelledby="rooms-heading">
            <div class="section-heading"><div><p class="eyebrow"><span data-i18n="rooms.eyebrow"><?= rq_t('rooms.eyebrow') ?></span></p><h2 id="rooms-heading"><span data-i18n="rooms.title"><?= rq_t('rooms.title') ?></span></h2></div><button class="button button-quiet" id="refresh" type="button"><span aria-hidden="true">↻</span> <span data-i18n="rooms.refresh"><?= rq_t('rooms.refresh') ?></span></button></div>
            <p class="board-caption"><span data-i18n="rooms.caption"><?= rq_t('rooms.caption') ?></span></p>
            <div id="list-status" class="notice" role="status" hidden></div>
            <div id="room-list" class="room-list" aria-busy="true"><p class="loading"><span data-i18n="rooms.loading"><?= rq_t('rooms.loading') ?></span></p></div>
            <div id="empty-state" class="empty-state" hidden><span class="empty-symbol" aria-hidden="true">[ &nbsp;_&nbsp; ]</span><h3><span data-i18n="rooms.empty_title"><?= rq_t('rooms.empty_title') ?></span></h3><p><span data-i18n="guide.create_game"><?= rq_t('guide.create_game') ?></span><br><span data-i18n="rooms.empty_hint"><?= rq_t('rooms.empty_hint') ?></span></p><a class="text-link" href="#publish-title"><span data-i18n="rooms.add"><?= rq_t('rooms.add') ?></span> <span aria-hidden="true">↗</span></a></div>
            <nav class="pagination" aria-label="<?= rq_t('rooms.pagination_label') ?>" data-i18n-aria-label="rooms.pagination_label"><button id="previous" class="button button-quiet" type="button" disabled><span data-i18n="rooms.previous"><?= rq_t('rooms.previous') ?></span></button><span id="page-number"><span data-i18n="rooms.first_page"><?= rq_t('rooms.first_page') ?></span></span><button id="next" class="button button-quiet" type="button" disabled><span data-i18n="rooms.next"><?= rq_t('rooms.next') ?></span></button></nav>
            <p class="board-footnote"><span aria-hidden="true">ⓘ</span> <span data-i18n="rooms.compatibility"><?= rq_t('rooms.compatibility') ?></span></p>
        </section>

        <aside class="publish-panel" aria-labelledby="publish-title">
            <div class="panel-topline"><span><span data-i18n="publish.eyebrow"><?= rq_t('publish.eyebrow') ?></span></span><span aria-hidden="true">↗</span></div>
            <div class="panel-content">
                <h2 id="publish-title"><span data-i18n="publish.title"><?= rq_t('publish.title') ?></span><br><span data-i18n="publish.subtitle"><?= rq_t('publish.subtitle') ?></span></h2>
                <p class="panel-intro"><span data-i18n="publish.intro_start"><?= rq_t('publish.intro_start') ?></span><br class="desktop-break"> <span data-i18n="publish.intro_end"><?= rq_t('publish.intro_end') ?></span></p>
                <form id="publish-form">
                    <label for="room-title"><span data-i18n="publish.name_label"><?= rq_t('publish.name_label') ?></span> <span>01</span></label>
                    <input id="room-title" name="title" maxlength="60" required autocomplete="off" placeholder="<?= rq_t('publish.name_placeholder') ?>" data-i18n-placeholder="publish.name_placeholder">
                    <div class="field-label"><label for="room-code"><span data-i18n="publish.code_label"><?= rq_t('publish.code_label') ?></span> <span>02</span></label><button id="paste-code" class="small-button" type="button"><span data-i18n="actions.paste"><?= rq_t('actions.paste') ?></span></button></div>
                    <textarea id="room-code" name="code" maxlength="1024" rows="5" required spellcheck="false" autocapitalize="off" autocomplete="off" placeholder="RQ1.…"></textarea>
                    <div class="field-help"><span><span data-i18n="publish.code_hint"><?= rq_t('publish.code_hint') ?></span></span><span id="code-length">0 / 1024</span></div>
                    <div id="publish-status" class="notice" role="status" hidden></div>
                    <button class="button button-primary" id="publish-button" type="submit"><span data-i18n="publish.submit"><?= rq_t('publish.submit') ?></span> <span aria-hidden="true">↗</span></button>
                </form>
                <div id="owner-result" class="owner-result" role="status" hidden>
                    <h3><span data-i18n="publish.success"><?= rq_t('publish.success') ?></span></h3><p><span data-i18n="publish.save_link_hint"><?= rq_t('publish.save_link_hint') ?></span></p>
                    <label for="delete-link"><span data-i18n="publish.delete_link_label"><?= rq_t('publish.delete_link_label') ?></span></label><input id="delete-link" readonly autocomplete="off"><button class="button button-dark" id="copy-delete" type="button"><span data-i18n="actions.copy_delete_link"><?= rq_t('actions.copy_delete_link') ?></span></button>
                </div>
                <p class="publication-note"><span aria-hidden="true">◷</span> <span data-i18n="publish.expiry"><?= rq_t('publish.expiry') ?></span><br><span data-i18n="publish.public_code"><?= rq_t('publish.public_code') ?></span></p>
            </div>
            <div class="hazard" aria-hidden="true"></div>
        </aside>
    </main>

    <section class="howto" aria-labelledby="howto-title"><span class="howto-index" aria-hidden="true">?</span><div><h2 id="howto-title"><span data-i18n="guide.paste_title"><?= rq_t('guide.paste_title') ?></span></h2><p><span data-i18n="guide.cloud_open"><?= rq_t('guide.cloud_open') ?></span> <strong><span data-i18n="game.connect_path"><?= rq_t('game.connect_path') ?></span></strong>.<br><span data-i18n="guide.select"><?= rq_t('guide.select') ?></span> <strong><span data-i18n="actions.paste"><?= rq_t('actions.paste') ?></span></strong><span data-i18n="guide.then"><?= rq_t('guide.then') ?></span> <strong><span data-i18n="game.connect"><?= rq_t('game.connect') ?></span></strong>.</p><p><span data-i18n="guide.website_button_prefix"><?= rq_t('guide.website_button_prefix') ?></span> <strong><span data-i18n="game.open_website"><?= rq_t('game.open_website') ?></span></strong> <span data-i18n="guide.website_return"><?= rq_t('guide.website_return') ?></span></p></div><span class="howto-tag"><span data-i18n="guide.flow_tag"><?= rq_t('guide.flow_tag') ?></span></span></section>
    <section class="faq" id="faq" aria-labelledby="faq-title">
        <div class="section-heading"><div><p class="eyebrow"><span data-i18n="faq.eyebrow"><?= rq_t('faq.eyebrow') ?></span></p><h2 id="faq-title"><span data-i18n="faq.title"><?= rq_t('faq.title') ?></span></h2></div><span class="faq-tag">PHOTON / REALTIME + VOICE</span></div>
        <p class="faq-intro"><span data-i18n="faq.intro"><?= rq_t('faq.intro') ?></span></p>
        <details><summary><span data-i18n="faq.custom_ids.title"><?= rq_t('faq.custom_ids.title') ?></span></summary><div class="faq-answer"><p><span data-i18n="faq.custom_ids.defaults"><?= rq_t('faq.custom_ids.defaults') ?></span></p><p><span data-i18n="faq.custom_ids.invitation"><?= rq_t('faq.custom_ids.invitation') ?></span></p></div></details>
        <details><summary><span data-i18n="faq.photon_setup.title"><?= rq_t('faq.photon_setup.title') ?></span></summary><div class="faq-answer">
            <ol>
                <li><span data-i18n="faq.photon_setup.open"><?= rq_t('faq.photon_setup.open') ?></span> <a href="https://www.photonengine.com/">Photon Engine</a> <span data-i18n="faq.photon_setup.register"><?= rq_t('faq.photon_setup.register') ?></span></li>
                <li><span data-i18n="faq.photon_setup.email"><?= rq_t('faq.photon_setup.email') ?></span> <strong>SET PASSWORD AND START</strong><span data-i18n="faq.photon_setup.password"><?= rq_t('faq.photon_setup.password') ?></span></li>
                <li><span data-i18n="faq.photon_setup.dashboard"><?= rq_t('faq.photon_setup.dashboard') ?></span> <a href="https://dashboard.photonengine.com/">Dashboard</a>.</li>
                <li><span data-i18n="guide.select"><?= rq_t('guide.select') ?></span> <strong>Create New App</strong><span data-i18n="faq.photon_setup.choose_type"><?= rq_t('faq.photon_setup.choose_type') ?></span> <strong>Multiplayer Game</strong>.</li>
                <li><span data-i18n="faq.photon_setup.field_prefix"><?= rq_t('faq.photon_setup.field_prefix') ?></span> <strong>Select Photon SDK</strong> <span data-i18n="faq.photon_setup.select_sdk"><?= rq_t('faq.photon_setup.select_sdk') ?></span> <strong>Realtime</strong>.</li>
                <li><span data-i18n="faq.photon_setup.name"><?= rq_t('faq.photon_setup.name') ?></span> <strong>Application Name</strong> <span data-i18n="faq.photon_setup.create"><?= rq_t('faq.photon_setup.create') ?></span> <strong>Create</strong>.</li>
                <li><span data-i18n="faq.photon_setup.voice"><?= rq_t('faq.photon_setup.voice') ?></span> <strong>Voice</strong>.</li>
                <li><span data-i18n="faq.photon_setup.copy"><?= rq_t('faq.photon_setup.copy') ?></span> <strong>App ID</strong> <span data-i18n="faq.photon_setup.label_ids"><?= rq_t('faq.photon_setup.label_ids') ?></span></li>
            </ol>
            <p><span data-i18n="faq.photon_setup.id_roles"><?= rq_t('faq.photon_setup.id_roles') ?></span> <a href="https://doc.photonengine.com/video/v2/getting-started/obtain-your-app-id"><span data-i18n="faq.photon_setup.documentation"><?= rq_t('faq.photon_setup.documentation') ?></span></a>.</p>
        </div></details>
        <details><summary><span data-i18n="faq.quest.title"><?= rq_t('faq.quest.title') ?></span></summary><div class="faq-answer">
            <ol>
                <li><span data-i18n="guide.cloud_open"><?= rq_t('guide.cloud_open') ?></span> <strong><span data-i18n="game.settings_path"><?= rq_t('game.settings_path') ?></span></strong>.</li>
                <li><span data-i18n="faq.quest.step_prefix"><?= rq_t('faq.quest.step_prefix') ?></span> <strong><span data-i18n="game.realtime_step"><?= rq_t('game.realtime_step') ?></span></strong> <span data-i18n="faq.quest.realtime"><?= rq_t('faq.quest.realtime') ?></span> <strong><span data-i18n="game.next"><?= rq_t('game.next') ?></span></strong>.</li>
                <li><span data-i18n="faq.quest.step_prefix"><?= rq_t('faq.quest.step_prefix') ?></span> <strong><span data-i18n="game.voice_step"><?= rq_t('game.voice_step') ?></span></strong> <span data-i18n="faq.quest.voice"><?= rq_t('faq.quest.voice') ?></span> <strong><span data-i18n="game.next"><?= rq_t('game.next') ?></span></strong>.</li>
                <li><span data-i18n="faq.quest.nickname_website"><?= rq_t('faq.quest.nickname_website') ?></span> <span data-i18n="faq.quest.cloud_identity"><?= rq_t('faq.quest.cloud_identity') ?></span></li>
                <li><span data-i18n="faq.quest.review"><?= rq_t('faq.quest.review') ?></span> <strong><span data-i18n="game.save"><?= rq_t('game.save') ?></span></strong><span data-i18n="faq.quest.restart"><?= rq_t('faq.quest.restart') ?></span></li>
            </ol>
            <p><span data-i18n="faq.quest.steam_prefix"><?= rq_t('faq.quest.steam_prefix') ?></span> <strong><span data-i18n="game.change_cloud_path"><?= rq_t('game.change_cloud_path') ?></span></strong><span data-i18n="faq.quest.complete_setup"><?= rq_t('faq.quest.complete_setup') ?></span></p>
        </div></details>
        <details><summary><span data-i18n="faq.pc.title"><?= rq_t('faq.pc.title') ?></span></summary><div class="faq-answer">
            <p><span data-i18n="faq.pc.plugins"><?= rq_t('faq.pc.plugins') ?></span></p>
            <p><span data-i18n="faq.pc.identity_intro"><?= rq_t('faq.pc.identity_intro') ?></span> <code>REPO_Data/Plugins/x86_64/steam_settings/configs.user.ini</code>.</p>
            <ol>
                <li data-i18n="faq.pc.identity_nickname"><?= rq_t('faq.pc.identity_nickname') ?></li>
                <li data-i18n="faq.pc.identity_id"><?= rq_t('faq.pc.identity_id') ?></li>
                <li data-i18n="faq.pc.identity_restart"><?= rq_t('faq.pc.identity_restart') ?></li>
            </ol>
            <pre><code>account_name=YourNickname
account_steamid=YOUR_STEAM_ID64</code></pre>
            <p data-i18n="faq.pc.identity_warning"><?= rq_t('faq.pc.identity_warning') ?></p>
            <div class="identity-generator">
                <label for="steam-id-output" data-i18n="faq.pc.id_generator_label"><?= rq_t('faq.pc.id_generator_label') ?></label>
                <div class="identity-generator-controls">
                    <input id="steam-id-output" type="text" inputmode="numeric" readonly aria-describedby="steam-id-status" data-i18n-placeholder="faq.pc.id_placeholder" data-i18n-aria-label="faq.pc.id_generator_label" placeholder="<?= rq_t('faq.pc.id_placeholder') ?>" aria-label="<?= rq_t('faq.pc.id_generator_label') ?>">
                    <button id="generate-steam-id" class="button button-primary" type="button"><span data-i18n="faq.pc.id_generate"><?= rq_t('faq.pc.id_generate') ?></span></button>
                </div>
                <p id="steam-id-status" class="identity-generator-status" role="status" aria-live="polite" hidden></p>
            </div>
            <ol>
                <li><span data-i18n="faq.pc.open_config"><?= rq_t('faq.pc.open_config') ?></span> <code>BepInEx/config/io.repoquest.cloudhost.cfg</code> <span data-i18n="faq.pc.editor"><?= rq_t('faq.pc.editor') ?></span></li>
                <li><span data-i18n="faq.pc.section_prefix"><?= rq_t('faq.pc.section_prefix') ?></span> <code>[Photon]</code> <span data-i18n="faq.pc.set_values"><?= rq_t('faq.pc.set_values') ?></span> <code>AppIdRealtime</code> <span data-i18n="faq.pc.and"><?= rq_t('faq.pc.and') ?></span> <code>AppIdVoice</code>:</li>
            </ol>
            <pre><code>[Photon]
AppIdRealtime = REALTIME_APP_ID
AppIdVoice = VOICE_APP_ID</code></pre>
            <p><span data-i18n="faq.pc.restart"><?= rq_t('faq.pc.restart') ?></span></p>
        </div></details>
        <details><summary><span data-i18n="faq.host.title"><?= rq_t('faq.host.title') ?></span></summary><div class="faq-answer">
            <p><span data-i18n="faq.host.quest_prefix"><?= rq_t('faq.host.quest_prefix') ?></span> <strong><span data-i18n="game.create_path"><?= rq_t('game.create_path') ?></span></strong><span data-i18n="faq.host.quest_complete"><?= rq_t('faq.host.quest_complete') ?></span></p>
            <p><span data-i18n="faq.host.created_prefix"><?= rq_t('faq.host.created_prefix') ?></span> <strong><span data-i18n="game.copy_code"><?= rq_t('game.copy_code') ?></span></strong> <span data-i18n="faq.host.window_prefix"><?= rq_t('faq.host.window_prefix') ?></span> <strong><span data-i18n="game.created_title"><?= rq_t('game.created_title') ?></span></strong>.</p>
            <p><span data-i18n="faq.host.waiting_lobby"><?= rq_t('faq.host.waiting_lobby') ?></span></p>
            <p><span data-i18n="faq.host.pc"><?= rq_t('faq.host.pc') ?></span> <strong>Copy room code</strong><span data-i18n="faq.host.code_file_prefix"><?= rq_t('faq.host.code_file_prefix') ?></span> <code>BepInEx/config/quest-room.txt</code>.</p>
            <p><span data-i18n="faq.host.share"><?= rq_t('faq.host.share') ?></span></p>
        </div></details>
    </section>
    <footer><span>REPO LINK</span><p><span data-i18n="footer.disclaimer"><?= rq_t('footer.disclaimer') ?></span></p><span class="footer-stamp"><span data-i18n="footer.signoff"><?= rq_t('footer.signoff') ?></span></span></footer>
</div>

<template id="room-template"><article class="room-card"><div class="room-card-main"><span class="room-marker" aria-hidden="true">↳</span><div class="room-content"><h3 class="room-name"></h3><div class="room-meta"><span class="room-region"></span><span class="room-version"></span><span class="room-expiry"></span></div></div><button class="button button-copy" type="button"><span data-i18n="actions.copy_code"><?= rq_t('actions.copy_code') ?></span> <span aria-hidden="true">↗</span></button></div><div class="copy-fallback" hidden><label><span data-i18n="clipboard.manual_hint"><?= rq_t('clipboard.manual_hint') ?></span><textarea readonly rows="3" spellcheck="false" aria-label="<?= rq_t('clipboard.code_label') ?>" data-i18n-aria-label="clipboard.code_label"></textarea></label></div></article></template>

<dialog id="delete-dialog" aria-labelledby="delete-heading"><p class="eyebrow"><span data-i18n="delete.eyebrow"><?= rq_t('delete.eyebrow') ?></span></p><h2 id="delete-heading"><span data-i18n="delete.title"><?= rq_t('delete.title') ?></span></h2><p><span data-i18n="delete.board_effect"><?= rq_t('delete.board_effect') ?></span><br><span data-i18n="delete.game_effect"><?= rq_t('delete.game_effect') ?></span></p><div id="delete-status" class="notice" role="status" hidden></div><div class="dialog-actions"><button id="confirm-delete" class="button button-primary" type="button"><span data-i18n="delete.confirm"><?= rq_t('delete.confirm') ?></span></button><button id="cancel-delete" class="button button-quiet" type="button"><span data-i18n="actions.cancel"><?= rq_t('actions.cancel') ?></span></button></div></dialog>
<noscript><p class="noscript"><span data-i18n="page.javascript_required"><?= rq_t('page.javascript_required') ?></span></p></noscript>
</body>
</html>
