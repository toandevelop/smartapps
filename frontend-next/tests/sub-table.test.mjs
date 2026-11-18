import test from 'node:test';
import assert from 'node:assert/strict';
import {
  parseSubTableSchema,
  serializeSubTableSchema,
  evaluateSubTableRowFormulas,
  validateSubTableRows,
} from '../src/lib/sub-table.ts';

test('parseSubTableSchema supports legacy comma-separated and typed JSON schemas', () => {
  const legacy = parseSubTableSchema('hang_muc, so_luong, ghi_chu');
  assert.equal(legacy.length, 3);
  assert.deepEqual(
    legacy.map((c) => [c.code, c.dataType]),
    [
      ['hang_muc', 'STRING'],
      ['so_luong', 'STRING'],
      ['ghi_chu', 'STRING'],
    ]
  );

  const json = serializeSubTableSchema([
    { code: 'san_pham', name: 'Sản phẩm', dataType: 'STRING', required: true },
    { code: 'so_luong', name: 'Số lượng', dataType: 'NUMBER', required: true },
    { code: 'don_gia', name: 'Đơn giá', dataType: 'CURRENCY', required: true },
    { code: 'thanh_tien', name: 'Thành tiền', dataType: 'CURRENCY', formula: 'ROUND(so_luong * don_gia, 2)' },
  ]);
  const parsed = parseSubTableSchema(json);
  assert.equal(parsed.length, 4);
  assert.equal(parsed[0].required, true);
  assert.equal(parsed[3].formula, 'ROUND(so_luong * don_gia, 2)');
});

test('evaluateSubTableRowFormulas computes row arithmetic and ROUND', () => {
  const schema = parseSubTableSchema(
    JSON.stringify([
      { code: 'so_luong', name: 'Số lượng', dataType: 'NUMBER' },
      { code: 'don_gia', name: 'Đơn giá', dataType: 'CURRENCY' },
      { code: 'thanh_tien', name: 'Thành tiền', dataType: 'CURRENCY', formula: 'ROUND(so_luong * don_gia, 2)' },
    ])
  );
  const row = evaluateSubTableRowFormulas(schema, { so_luong: '3', don_gia: '12.345' });
  assert.equal(row.thanh_tien, 37.04);
});

test('validateSubTableRows reports cell-level paths for required and typed errors', () => {
  const schema = parseSubTableSchema(
    JSON.stringify([
      { code: 'san_pham', name: 'Sản phẩm', dataType: 'STRING', required: true },
      { code: 'so_luong', name: 'Số lượng', dataType: 'NUMBER', required: true },
      { code: 'email', name: 'Email', dataType: 'EMAIL' },
    ])
  );
  const errors = validateSubTableRows('chi_tiet', schema, [
    { san_pham: '', so_luong: 'abc', email: 'not-an-email' },
    { san_pham: 'Laptop', so_luong: 2, email: 'ok@example.com' },
  ]);
  assert.ok(errors['chi_tiet[0].san_pham']);
  assert.ok(errors['chi_tiet[0].so_luong']);
  assert.ok(errors['chi_tiet[0].email']);
  assert.equal(errors['chi_tiet[1].san_pham'], undefined);
  assert.equal(errors['chi_tiet[1].so_luong'], undefined);
});
