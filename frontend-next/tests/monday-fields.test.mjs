import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MONDAY_COLUMN_TYPES,
  clampProgressValue,
  clampRatingValue,
  computeMondayColumnSummary,
  formatDateTimeDisplay,
  getColumnTypeMeta,
  getMondayProgressColor,
  normalizeSafeLinkUrl,
  parseTagsValue,
  resolveMondayPriorityStyle,
  resolveMondayStatusStyle,
} from '../src/lib/monday-fields.ts';

test('MONDAY_COLUMN_TYPES exposes all 27 data types across 4 categories', () => {
  assert.equal(MONDAY_COLUMN_TYPES.length, 27);
  const statusMeta = getColumnTypeMeta('STATUS');
  assert.equal(statusMeta.category, 'WORK_OS');
  assert.equal(statusMeta.shortBadge, '◉');
});

test('resolveMondayStatusStyle and resolveMondayPriorityStyle map codes to vibrant Work OS colors', () => {
  assert.equal(resolveMondayStatusStyle('DONE').bg, '#00c875');
  assert.equal(resolveMondayStatusStyle('HOAN_THANH').bg, '#00c875');
  assert.equal(resolveMondayStatusStyle('WORKING').bg, '#fdab3d');
  assert.equal(resolveMondayStatusStyle('STUCK').bg, '#e2445c');
  assert.equal(resolveMondayPriorityStyle('CRITICAL').bg, '#e2445c');
  assert.equal(resolveMondayPriorityStyle('HIGH').bg, '#401694');
});

test('clampRatingValue and clampProgressValue bound numeric ranges and colors', () => {
  assert.equal(clampRatingValue(7), 5);
  assert.equal(clampRatingValue(-2), 0);
  assert.equal(clampRatingValue('4'), 4);
  assert.equal(clampProgressValue(135), 100);
  assert.equal(clampProgressValue(-10), 0);
  assert.equal(getMondayProgressColor(100), '#00c875');
  assert.equal(getMondayProgressColor(20), '#e2445c');
});

test('parseTagsValue deduplicates and strips leading # characters', () => {
  assert.deepEqual(parseTagsValue('#urgent, #Frontend, urgent, #AI'), ['urgent', 'Frontend', 'AI']);
  assert.deepEqual(parseTagsValue('["#erp","crm"]'), ['erp', 'crm']);
});

test('normalizeSafeLinkUrl blocks javascript XSS and auto-prefixes https', () => {
  assert.equal(normalizeSafeLinkUrl('javascript:alert(1)'), null);
  const safe = normalizeSafeLinkUrl('monday.com/work-os');
  assert.deepEqual(safe, { href: 'https://monday.com/work-os', label: 'monday.com/work-os' });
});

test('formatDateTimeDisplay formats DATETIME and TIME cleanly', () => {
  assert.equal(formatDateTimeDisplay('2026-11-23T14:30', 'DATETIME'), '23/11/2026 14:30');
  assert.equal(formatDateTimeDisplay('08:45:00', 'TIME'), '08:45');
});

test('computeMondayColumnSummary aggregates SUM, AVG_PROGRESS, AVG_RATING, and STATUS_BAR', () => {
  const records = [
    { id: '1', appId: 'a', moduleId: 'm', tableId: 't', status: 'ACTIVE', createdAt: '', updatedAt: '', data: { amt: 100, prog: 50, rate: 4, st: 'DONE' } },
    { id: '2', appId: 'a', moduleId: 'm', tableId: 't', status: 'ACTIVE', createdAt: '', updatedAt: '', data: { amt: 250, prog: 100, rate: 5, st: 'WORKING' } },
  ];
  const sumRes = computeMondayColumnSummary(records, { id: 'c1', tableId: 't', columnCode: 'amt', columnName: 'Amount', excelHeader: '', dataType: 'NUMBER', isRequired: false, isIdentityKey: false, showInTable: true, showInForm: true, width: 120, orderIndex: 1 });
  assert.equal(sumRes.kind, 'SUM');
  assert.equal(sumRes.numericValue, 350);

  const progRes = computeMondayColumnSummary(records, { id: 'c2', tableId: 't', columnCode: 'prog', columnName: 'Progress', excelHeader: '', dataType: 'PROGRESS', isRequired: false, isIdentityKey: false, showInTable: true, showInForm: true, width: 120, orderIndex: 2 });
  assert.equal(progRes.kind, 'AVG_PROGRESS');
  assert.equal(progRes.numericValue, 75);

  const rateRes = computeMondayColumnSummary(records, { id: 'c3', tableId: 't', columnCode: 'rate', columnName: 'Rating', excelHeader: '', dataType: 'RATING', isRequired: false, isIdentityKey: false, showInTable: true, showInForm: true, width: 120, orderIndex: 3 });
  assert.equal(rateRes.kind, 'AVG_RATING');
  assert.equal(rateRes.numericValue, 4.5);

  const stRes = computeMondayColumnSummary(records, { id: 'c4', tableId: 't', columnCode: 'st', columnName: 'Status', excelHeader: '', dataType: 'STATUS', isRequired: false, isIdentityKey: false, showInTable: true, showInForm: true, width: 120, orderIndex: 4 });
  assert.equal(stRes.kind, 'STATUS_BAR');
  assert.equal(stRes.segments?.length, 2);
});
