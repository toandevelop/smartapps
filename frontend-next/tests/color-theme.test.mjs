import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildBrandPalette,
  computeContrastRatio,
  normalizeDensityMode,
  normalizeHexColor,
  normalizePinnedApps,
  pickContrastText,
  resolveEffectiveBrandColor,
} from '../src/lib/color-theme.ts';

test('normalizeHexColor validates 6-digit hex and falls back safely', () => {
  assert.equal(normalizeHexColor('#00897B'), '#00897b');
  assert.equal(normalizeHexColor('invalid', '#1a73e8'), '#1a73e8');
  assert.equal(normalizeHexColor(''), '#1a73e8');
});

test('pickContrastText selects readable foreground color for light and dark brand colors', () => {
  assert.equal(pickContrastText('#1a73e8'), '#ffffff');
  assert.equal(pickContrastText('#00897b'), '#ffffff');
  assert.equal(pickContrastText('#facc15'), '#111827');
  assert.equal(pickContrastText('#f59e0b'), '#111827');
});

test('buildBrandPalette ensures >= 3.4 contrast in dark mode and >= 4.5 text contrast', () => {
  const darkNavy = buildBrandPalette('#1e3a8a', true);
  assert.ok(computeContrastRatio(darkNavy.primary, '#162032') >= 3.4);
  assert.ok(computeContrastRatio(darkNavy.primary, darkNavy.onPrimary) >= 4.5);

  const lightAmber = buildBrandPalette('#f59e0b', false);
  assert.equal(lightAmber.onPrimary, '#111827');
  assert.ok(computeContrastRatio(lightAmber.primary, lightAmber.onPrimary) >= 4.5);
});

test('resolveEffectiveBrandColor honors brand keyword inheritance', () => {
  assert.equal(resolveEffectiveBrandColor('brand', '#00897b'), '#00897b');
  assert.equal(resolveEffectiveBrandColor('', '#7b1fa2'), '#7b1fa2');
  assert.equal(resolveEffectiveBrandColor('#d93025', '#00897b'), '#d93025');
});

test('normalizeDensityMode and normalizePinnedApps enforce constraints', () => {
  assert.equal(normalizeDensityMode('SPACIOUS', false), 'spacious');
  assert.equal(normalizeDensityMode(undefined, true), 'compact');
  assert.equal(normalizeDensityMode(undefined, false), 'comfortable');

  assert.deepEqual(
    normalizePinnedApps(['hrm', 'OPS', 'HRM', '  asset  ', '']),
    ['HRM', 'OPS', 'ASSET'],
  );
  const many = Array.from({ length: 20 }, (_, i) => `APP_${i}`);
  assert.equal(normalizePinnedApps(many).length, 12);
});
