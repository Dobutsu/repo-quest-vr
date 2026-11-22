'use strict';

const byId = id => document.getElementById(id);
const origin = document.querySelector('meta[name="rq-origin"]').content;
let csrf = '';
let currentPage = 1;
let loadSequence = 0;
let deletion = null;
const codeField = byId('room-code');

function steamIdStatus(key) {
    const status = byId('steam-id-status');
    status.dataset.messageKey = key;
    status.textContent = t(key);
}

function notice(id, key) {
    const element = byId(id);
    element.dataset.messageKey = key;
    element.textContent = key ? t(key) : '';
    element.hidden = !key;
}

async function api(action, data = null, page = 1) {
    const options = { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } };
    if (data !== null) {
        options.method = 'POST';
        options.headers['Content-Type'] = 'application/json';
        options.headers['X-CSRF-Token'] = csrf;
        options.body = JSON.stringify(data);
    }
    let response, result;
    try {
        response = await fetch(`/api.php?action=${action}${action === 'list' ? `&page=${page}` : ''}`, options);
        result = await response.json();
    } catch { throw new Error('error.unavailable'); }
    if (!response.ok) throw new Error(catalog.errors[result.error_code] || 'error.request_failed');
    return result;
}

async function session() {
    const result = await api('session');
    csrf = result.csrf;
}

function remaining(expires, now) {
    const minutes = Math.max(1, Math.ceil((expires - now) / 60));
    return minutes >= 60 ? t('rooms.hours_remaining', { count: Math.ceil(minutes / 60) }) : t('rooms.minutes_remaining', { count: minutes });
}

function renderCopyButton(button) {
    const copying = button.dataset.copying === 'true';
    const label = button.querySelector('[data-i18n]');
    label.dataset.i18n = copying ? 'clipboard.copied' : button.dataset.copyLabel;
    label.textContent = t(label.dataset.i18n);
    const icon = button.querySelector('[aria-hidden]');
    if (icon) icon.hidden = copying;
}

async function copyText(value, button, fallback) {
    try {
        if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
        await navigator.clipboard.writeText(value);
        button.dataset.copying = 'true';
        renderCopyButton(button);
        clearTimeout(button.copyTimeout);
        button.copyTimeout = setTimeout(() => {
            button.dataset.copying = 'false';
            renderCopyButton(button);
        }, 2200);
    } catch { fallback(); }
}

function renderRoomMeta(card) {
    const data = card.roomData;
    card.querySelector('.room-version').title = t('rooms.version_hint', { build: data.room.game_build, version: data.room.app_version });
    card.querySelector('.room-expiry').textContent = remaining(Number(data.room.expires_at), data.now);
}

function roomCard(room, now) {
    const card = byId('room-template').content.firstElementChild.cloneNode(true);
    registerTranslations(card);
    card.roomData = { room, now };
    card.querySelector('.room-name').textContent = room.title;
    card.querySelector('.room-region').textContent = room.region;
    card.querySelector('.room-version').textContent = room.game_build;
    renderRoomMeta(card);
    const copy = card.querySelector('.button-copy');
    copy.dataset.copyLabel = 'actions.copy_code';
    copy.addEventListener('click', () => copyText(room.code, copy, () => {
        const wrapper = card.querySelector('.copy-fallback');
        const field = wrapper.querySelector('textarea');
        field.value = room.code;
        wrapper.hidden = false;
        field.focus();
        field.select();
    }));
    return card;
}

async function loadRooms() {
    const sequence = ++loadSequence;
    byId('room-list').setAttribute('aria-busy', 'true');
    byId('refresh').disabled = true;
    try {
        const result = await api('list', null, currentPage);
        if (sequence !== loadSequence) return;
        if (result.rooms.length === 0 && currentPage > 1) {
            currentPage--;
            await loadRooms();
            return;
        }
        byId('room-list').replaceChildren(...result.rooms.map(room => roomCard(room, result.server_time)));
        byId('empty-state').hidden = result.rooms.length !== 0;
        byId('previous').disabled = currentPage === 1;
        byId('next').disabled = !result.has_more;
        byId('page-number').textContent = t('rooms.page_number', { page: currentPage });
        notice('list-status', '');
    } catch (error) {
        if (sequence !== loadSequence) return;
        byId('room-list').querySelector('.loading')?.remove();
        notice('list-status', error.message);
        byId('empty-state').hidden = true;
    } finally {
        if (sequence === loadSequence) {
            byId('refresh').disabled = false;
            byId('room-list').setAttribute('aria-busy', 'false');
        }
    }
}

byId('refresh').addEventListener('click', loadRooms);
byId('previous').addEventListener('click', () => { if (currentPage > 1) { currentPage--; loadRooms(); } });
byId('next').addEventListener('click', () => { if (currentPage < 50) { currentPage++; loadRooms(); } });
setInterval(() => { if (!document.hidden && !byId('refresh').disabled) loadRooms(); }, 30000);

byId('generate-steam-id').addEventListener('click', async () => {
    const field = byId('steam-id-output');
    const status = byId('steam-id-status');
    const random = new Uint32Array(1);
    do { crypto.getRandomValues(random); } while (random[0] === 0);
    const steamId = String(76561197960265728n + BigInt(random[0]));
    field.value = steamId;
    status.hidden = false;
    try {
        if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
        await navigator.clipboard.writeText(steamId);
        steamIdStatus('faq.pc.id_copied');
    } catch {
        steamIdStatus('faq.pc.id_copy_fallback');
        field.focus();
        field.select();
    }
});

function updateLength() { byId('code-length').textContent = `${codeField.value.length} / 1024`; }
codeField.addEventListener('input', updateLength);
byId('paste-code').addEventListener('click', async () => {
    try {
        if (!navigator.clipboard?.readText) throw new Error('Unavailable');
        const text = (await navigator.clipboard.readText()).trim();
        if (!text || text.length > 1024) {
            notice('publish-status', 'error.clipboard_code');
            return;
        }
        codeField.value = text;
        updateLength();
        notice('publish-status', '');
    } catch {
        notice('publish-status', 'error.clipboard_denied');
    }
    codeField.focus();
});

byId('publish-form').addEventListener('submit', async event => {
    event.preventDefault();
    const button = byId('publish-button');
    button.disabled = true;
    notice('publish-status', '');
    try {
        if (!csrf) await session();
        const result = await api('create', { title: byId('room-title').value.trim(), code: codeField.value.trim() });
        byId('delete-link').value = `${origin}/?manage=${result.id}#delete=${result.delete_token}`;
        byId('owner-result').hidden = false;
        byId('publish-form').reset();
        updateLength();
        currentPage = 1;
        await loadRooms();
        byId('owner-result').scrollIntoView({ block: 'nearest', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    } catch (error) { notice('publish-status', error.message); }
    finally { button.disabled = false; }
});

byId('copy-delete').dataset.copyLabel = 'actions.copy_delete_link';
byId('copy-delete').addEventListener('click', () => copyText(byId('delete-link').value, byId('copy-delete'), () => {
    byId('delete-link').focus();
    byId('delete-link').select();
}));

function closeDelete() {
    const dialog = byId('delete-dialog');
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute('open');
    deletion = null;
}
byId('cancel-delete').addEventListener('click', closeDelete);
byId('delete-dialog').addEventListener('cancel', () => { deletion = null; });
byId('confirm-delete').addEventListener('click', async () => {
    if (!deletion) return;
    const button = byId('confirm-delete');
    button.disabled = true;
    try {
        if (!csrf) await session();
        await api('delete', deletion);
        closeDelete();
        currentPage = 1;
        await loadRooms();
        notice('list-status', 'delete.success');
    } catch (error) { notice('delete-status', error.message); }
    finally { button.disabled = false; }
});

function readDeletionLink() {
    const url = new URL(window.location.href);
    const id = url.searchParams.get('manage');
    const token = new URLSearchParams(url.hash.slice(1)).get('delete');
    if (!id && !token) return;
    window.history.replaceState(null, '', '/');
    if (!/^[a-f0-9]{32}$/.test(id || '') || !/^[a-f0-9]{64}$/.test(token || '')) {
        notice('publish-status', 'error.invalid_delete_link');
        return;
    }
    deletion = { id, token };
    const dialog = byId('delete-dialog');
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
}

document.addEventListener('languagechange', () => {
    for (const element of document.querySelectorAll('.notice')) element.textContent = element.dataset.messageKey ? t(element.dataset.messageKey) : '';
    const steamStatus = byId('steam-id-status');
    if (!steamStatus.hidden && steamStatus.dataset.messageKey) steamStatus.textContent = t(steamStatus.dataset.messageKey);
    for (const card of document.querySelectorAll('.room-card')) renderRoomMeta(card);
    for (const button of document.querySelectorAll('[data-copy-label]')) renderCopyButton(button);
    byId('page-number').textContent = t('rooms.page_number', { page: currentPage });
});

readDeletionLink();
loadRooms();
