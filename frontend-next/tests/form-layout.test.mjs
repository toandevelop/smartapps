import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildDefaultFormLayout,
  normalizeFormLayout,
  validateFormLayout,
} from '../src/lib/form-layout.ts';

const cols = [
  { id: '1', tableId: 't1', columnCode: 'ma', columnName: 'Mã', excelHeader: 'Mã', dataType: 'STRING', isRequired: true, isIdentityKey: true, showInTable: true, showInForm: true, width: 140, orderIndex: 1 },
  { id: '2', tableId: 't1', columnCode: 'ten', columnName: 'Tên', excelHeader: 'Tên', dataType: 'STRING', isRequired: true, isIdentityKey: false, showInTable: true, showInForm: true, width: 180, orderIndex: 2 },
  { id: '3', tableId: 't1', columnCode: 'ghi_chu', columnName: 'Ghi chú', excelHeader: 'Ghi chú', dataType: 'TEXTAREA', isRequired: false, isIdentityKey: false, showInTable: false, showInForm: true, width: 220, orderIndex: 3 },
  { id: '4', tableId: 't1', columnCode: 'an_form', columnName: 'Ẩn form', excelHeader: 'Ẩn', dataType: 'STRING', isRequired: false, isIdentityKey: false, showInTable: true, showInForm: false, width: 120, orderIndex: 4 },
];

test('buildDefaultFormLayout builds 2-col layout excluding showInForm=false and spans TEXTAREA', () => {
  const layout = buildDefaultFormLayout(cols);
  assert.equal(layout.mode, 'sections');
  assert.equal(layout.sections.length, 1);
  assert.deepEqual(
    layout.sections[0].fields.map((f) => [f.columnCode, f.colSpan]),
    [['ma', 1], ['ten', 1], ['ghi_chu', 2]],
  );
});

test('normalizeFormLayout appends newly added columns and removes deleted/duplicate fields', () => {
  const saved = {
    mode: 'wizard',
    sections: [
      { id: 's1', title: 'Bước 1', columns: 2, fields: [{ columnCode: 'ma', colSpan: 1 }, { columnCode: 'deleted_col', colSpan: 1 }] },
      { id: 's2', title: 'Bước 2', columns: 1, fields: [{ columnCode: 'ma', colSpan: 2 }, { columnCode: 'ten', colSpan: 3 }] },
    ],
  };
  const norm = normalizeFormLayout(saved, cols);
  assert.equal(norm.mode, 'wizard');
  assert.deepEqual(norm.sections[0].fields, [{ columnCode: 'ma', colSpan: 1 }]);
  // ten clamped to colSpan=1 (columns=1), ghi_chu auto-appended to last section
  assert.deepEqual(norm.sections[1].fields, [
    { columnCode: 'ten', colSpan: 1 },
    { columnCode: 'ghi_chu', colSpan: 1 },
  ]);
});

test('validateFormLayout flags duplicate fields and missing section titles', () => {
  const bad = {
    mode: 'tabs',
    sections: [
      { id: 's1', title: '', columns: 2, fields: [{ columnCode: 'ma', colSpan: 1 }] },
      { id: 's2', title: 'Tab 2', columns: 2, fields: [{ columnCode: 'ma', colSpan: 1 }] },
    ],
  };
  const errs = validateFormLayout(bad, cols);
  assert.ok(errs.some((e) => e.includes('chưa có tiêu đề')));
  assert.ok(errs.some((e) => e.includes('bị lặp')));
});
