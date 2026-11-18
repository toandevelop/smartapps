import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';
import * as XLSX from 'xlsx/xlsx.mjs';

const source = await readFile(new URL('../src/lib/excel-import.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { suggestImportMapping, getImportSourceRows, mapImportRows, MAX_IMPORT_ROWS } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const columns = [
  { columnCode: 'code', columnName: 'Mã nhân viên', excelHeader: 'Mã NV', dataType: 'STRING' },
  { columnCode: 'amount', columnName: 'Số tiền', excelHeader: 'Amount', dataType: 'NUMBER' },
  { columnCode: 'date', columnName: 'Ngày', excelHeader: 'Date', dataType: 'DATE' },
];

test('suggests mappings by code, label, or Excel header without reusing duplicate targets', () => {
  assert.deepEqual(suggestImportMapping(['Ma NV', 'Số tiền', 'Mã nhân viên', '', 'unknown'], columns), ['code', 'amount', '', '', '']);
});

test('keeps worksheet row numbers with title/header and blank rows, and preserves empty mapped cells', () => {
  const source = getImportSourceRows([['Title'], ['ID', 'Amount'], [], ['001', 0], ['', '']], 2);
  assert.deepEqual(source.map(row => row.sourceRow), [4]);
  const mapped = mapImportRows(source, ['code', 'amount'], columns);
  assert.equal(mapped.rows[0].code, '001');
  assert.equal(mapped.rows[0].amount, 0);
  const blank = mapImportRows([{ sourceRow: 9, values: ['002'] }], ['code', 'amount'], columns);
  assert.equal(blank.rows[0].amount, null);
});

test('uses source indexes for identical headers and rejects repeated destination fields', () => {
  const rows = [{ sourceRow: 2, values: ['ignored', 'chosen', false] }];
  assert.equal(mapImportRows(rows, ['', 'code', ''], columns).rows[0].code, 'chosen');
  assert.throws(() => mapImportRows(rows, ['code', 'code'], columns), /một lần/);
  assert.throws(() => mapImportRows(rows, ['unknown'], columns), /không hợp lệ/);
  assert.throws(() => mapImportRows(rows, ['', ''], columns), /ít nhất/);
});

test('enforces the import size without truncating rows', () => {
  assert.throws(() => mapImportRows([], ['code'], columns), /dòng dữ liệu/);
  assert.throws(() => mapImportRows(Array.from({ length: MAX_IMPORT_ROWS + 1 }, (_, i) => ({ sourceRow: i + 2, values: [i] })), ['code'], columns), /dòng dữ liệu/);
});

test('installed SheetJS reads multiple sheets and blank rows using the wizard options', () => {
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet([['Title'], ['ID', 'Amount'], [], ['001', 0]]), 'People');
  XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet([['Other'], ['x']]), 'Other');
  const parsed = XLSX.read(XLSX.write(workbook, { type: 'buffer', bookType: 'xlsx' }), { type: 'buffer', cellDates: false });
  assert.equal(parsed.SheetNames.length, 2);
  const grid = XLSX.utils.sheet_to_json(parsed.Sheets.People, { header: 1, range: 0, defval: null, blankrows: true, raw: true });
  assert.equal(getImportSourceRows(grid, 2)[0].sourceRow, 4);
  assert.equal(getImportSourceRows(grid, 2)[0].values[0], '001');
  assert.deepEqual([XLSX.SSF.parse_date_code(46343).y, XLSX.SSF.parse_date_code(46343).m, XLSX.SSF.parse_date_code(46343).d], [2026, 11, 17]);
});

test('CSV import preserves UTF-8, leading zeros, and ambiguous date/number text until backend normalization', () => {
  const bytes = Buffer.from('Tên;Mã;Ngày;Số tiền\nNguyễn;001;01/02/2026;1.234,50', 'utf8');
  const book = XLSX.read(bytes, { type: 'array', raw: true, cellDates: false, codepage: 65001 });
  const grid = XLSX.utils.sheet_to_json(book.Sheets[book.SheetNames[0]], { header: 1, range: 0, defval: null, blankrows: true, raw: true });
  assert.deepEqual(grid[1], ['Nguyễn', '001', '01/02/2026', '1.234,50']);
});
