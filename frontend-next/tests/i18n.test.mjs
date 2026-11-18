import assert from 'node:assert/strict';
import test from 'node:test';
import { TRANSLATIONS, normalizeLanguage, translate } from '../src/lib/i18n.ts';

test('normalizeLanguage defaults to vi and recognizes en', () => {
  assert.equal(normalizeLanguage(undefined), 'vi');
  assert.equal(normalizeLanguage('vi'), 'vi');
  assert.equal(normalizeLanguage('en'), 'en');
  assert.equal(normalizeLanguage('en-US'), 'en');
});

test('vi and en translation dictionaries have identical keys', () => {
  const viKeys = Object.keys(TRANSLATIONS.vi).sort();
  const enKeys = Object.keys(TRANSLATIONS.en).sort();
  assert.deepEqual(enKeys, viKeys);
});

test('translate interpolates variables and switches between vi and en', () => {
  assert.equal(translate('vi', 'navbar.syncOffline', { count: 3 }), 'Đồng bộ Offline (3)');
  assert.equal(translate('en', 'navbar.syncOffline', { count: 3 }), 'Sync Offline (3)');
  assert.equal(translate('en', 'nav.portal'), 'App Portal');
  assert.equal(translate('vi', 'nav.portal'), 'Cổng Ứng dụng');
});
