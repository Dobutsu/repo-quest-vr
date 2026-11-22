'use strict';

const catalog = window.RQ_I18N;
let locale = catalog.locale;
function t(key, values = {}) {
    let result = catalog.translations[key]?.[locale];
    if (typeof result !== 'string') throw new Error(`Unknown translation key: ${key}`);
    for (const [key, value] of Object.entries(values)) result = result.replaceAll(`{${key}}`, String(value));
    return result;
}

// Explicit bindings keep UI translation independent of visible text and user-supplied content.
function registerTranslations(root) {
    const selector = '[data-i18n],[data-i18n-aria-label],[data-i18n-placeholder],[data-i18n-title],[data-i18n-content]';
    const nodes = [...root.querySelectorAll(selector)];
    if (root.matches?.(selector)) nodes.unshift(root);
    for (const node of nodes) {
        if (node.dataset.i18n) node.textContent = t(node.dataset.i18n);
        for (const attribute of ['aria-label', 'placeholder', 'title', 'content']) {
            const key = node.getAttribute(`data-i18n-${attribute}`);
            if (key) node.setAttribute(attribute, t(key));
        }
    }
}

function setLanguage(language, save = true) {
    if (!['ru', 'en'].includes(language)) return;
    locale = language;
    document.documentElement.lang = locale;
    if (save) document.cookie = `rq-language=${locale}; Path=/; Max-Age=31536000; SameSite=Lax${location.protocol === 'https:' ? '; Secure' : ''}`;
    registerTranslations(document);
    for (const button of document.querySelectorAll('[data-language]')) button.setAttribute('aria-pressed', String(button.dataset.language === locale));
    document.dispatchEvent(new CustomEvent('languagechange'));
}

registerTranslations(document);
for (const button of document.querySelectorAll('[data-language]')) button.addEventListener('click', () => setLanguage(button.dataset.language));
const savedLanguage = document.cookie.match(/(?:^|;\s*)rq-language=(ru|en)(?:;|$)/)?.[1];
setLanguage(savedLanguage || (/^ru(?:-|$)/i.test(navigator.language || '') ? 'ru' : 'en'), false);
