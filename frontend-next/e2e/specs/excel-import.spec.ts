import { test, expect, post, submit } from '../fixtures';
import { workbookFile, openImport, mapping, previewSection, confirmation, downloadWorkbook, matrix, previewPath, commitPath } from '../excel';
import type { ImportPreview } from '../../src/lib/excel-import';

test('Excel import maps reordered columns on a selected sheet and previews normalized values without writing', async ({ page, qa }) => {
  await qa.addColumn({ columnCode: 'date', columnName: 'Ngày', dataType: 'DATE' });
  const file = workbookFile({
    'Hướng dẫn': [['Không nhập sheet này'], ['NOT-IMPORTED']],
    'Dữ liệu': [
      ['Danh sách kiểm thử'],
      ['Trạng thái nguồn', 'Mã ngoài', 'Tiền nguồn', 'Ngày nguồn', 'Cờ nguồn', 'Không dùng'],
      ['CÔNG TY', '001', '1.234,50', '17/11/2026', 'có', 'DO-NOT-IMPORT'],
      [],
      ['CÁ NHÂN', '002', '2.000,00', '18/11/2026', 'không', 'DO-NOT-IMPORT'],
    ],
  });
  const dialog = await openImport(page, qa);
  await dialog.getByLabel('Tệp Excel/CSV').setInputFiles(file);
  await dialog.getByRole('combobox', { name: 'Sheet', exact: true }).selectOption('Dữ liệu');
  await dialog.getByRole('spinbutton', { name: 'Dòng tiêu đề', exact: true }).fill('2');
  for (const [source, target] of [['A', 'mode'], ['B', 'code'], ['C', 'amount'], ['D', 'date'], ['E', 'approved']]) {
    await mapping(dialog, source).selectOption(target);
  }
  await expect(mapping(dialog, 'F')).toHaveValue('');
  await dialog.getByRole('combobox', { name: 'Định dạng số văn bản', exact: true }).selectOption('vi-VN');
  await dialog.getByRole('combobox', { name: 'Định dạng ngày văn bản', exact: true }).selectOption('dd/MM/yyyy');
  const response = await submit(page, 'Kiểm tra và xem trước', previewPath);
  const preview: ImportPreview = (await response.json()).data;
  expect(response.request().postDataJSON()).toMatchObject({ rowNumbers: [3, 5], rows: [
    { mode: 'CÔNG TY', code: '001', amount: '1.234,50', date: '17/11/2026', approved: 'có' },
    { mode: 'CÁ NHÂN', code: '002', amount: '2.000,00', date: '18/11/2026', approved: 'không' },
  ] });
  expect(preview).toMatchObject({ totalRows: 2, errorRows: 0, validRows: 2, inserted: 2, updated: 0, canImport: true });
  expect(preview.rows.map(row => row.data)).toMatchObject([
    { code: '001', mode: 'CÔNG TY', amount: 1234.5, date: '2026-11-17', approved: true },
    { code: '002', mode: 'CÁ NHÂN', amount: 2000, date: '2026-11-18', approved: false },
  ]);
  const section = previewSection(dialog);
  await expect(section.getByRole('row').filter({ hasText: '001' }).getByRole('cell').first()).toHaveText('3');
  await expect(section.getByRole('row').filter({ hasText: '002' }).getByRole('cell').first()).toHaveText('5');
  await expect(section.getByRole('cell', { name: '1234.5', exact: true })).toBeVisible();
  expect(await qa.records()).toHaveLength(0);
  await expect(confirmation(dialog)).toBeEnabled();
  const committed = await submit(page, 'Xác nhận nhập 2 dòng', commitPath);
  expect(committed.request().postDataJSON().previewFingerprint).toBe(preview.fingerprint);
  expect((await committed.json()).data).toMatchObject({ inserted: 2, updated: 0, errorRows: 0 });
  await expect(dialog).toBeHidden();
  expect((await qa.records()).map(record => record.data)).toEqual(expect.arrayContaining([
    expect.objectContaining({ code: '001', amount: 1234.5, date: '2026-11-17', approved: true }),
    expect.objectContaining({ code: '002', amount: 2000, date: '2026-11-18', approved: false }),
  ]));
  await expect(page.getByRole('row').filter({ hasText: '001' })).toBeVisible();
});

test('Excel import reports original row numbers, filters and pages errors, and rejects the entire invalid batch', async ({ page, qa }, info) => {
  await qa.addColumn({ columnCode: 'date', columnName: 'Ngày', dataType: 'DATE' });
  const rows: unknown[][] = [
    ['Báo cáo có dòng trống'], [], ['code', 'amount', 'date'],
    ['VALID-00', 1000.5, '2026-11-17'],
    ['BAD-NUMBER', 'not-a-number', '2026-11-17'], [],
    ['BAD-DATE', 1.2, '2026-02-31'], ['', 12.5, '2026-11-17'],
    ['DUPLICATE', 1, '2026-11-17'], ['DUPLICATE', 2, '2026-11-17'],
    ...Array.from({ length: 25 }, (_, i) => [`VALID-${i + 1}`, i, '2026-11-17']),
  ];
  const dialog = await openImport(page, qa);
  await dialog.getByLabel('Tệp Excel/CSV').setInputFiles(workbookFile({ Data: rows }));
  await dialog.getByRole('spinbutton', { name: 'Dòng tiêu đề', exact: true }).fill('3');
  const response = await submit(page, 'Kiểm tra và xem trước', previewPath);
  const preview: ImportPreview = (await response.json()).data;
  expect(preview).toMatchObject({ totalRows: 31, validRows: 26, errorRows: 5, canImport: false });
  expect(preview.rows.filter(row => row.errors.some(error => error.severity === 'ERROR')).map(row => row.sourceRow)).toEqual([5, 7, 8, 9, 10]);
  await expect(confirmation(dialog)).toBeDisabled();
  const section = previewSection(dialog);
  await expect(section.getByRole('checkbox', { name: 'Chỉ dòng lỗi', exact: true })).toBeChecked();
  await expect(section.getByRole('row')).toHaveCount(6);
  const sourceNumbers = await section.getByRole('row').all();
  expect(await Promise.all(sourceNumbers.slice(1).map(row => row.getByRole('cell').first().innerText()))).toEqual(['5', '7', '8', '9', '10']);
  await expect(section.getByRole('row').filter({ hasText: 'BAD-NUMBER' })).toContainText('amount:');
  await expect(section.getByRole('row').filter({ hasText: 'BAD-DATE' })).toContainText('date:');
  await section.getByRole('checkbox', { name: 'Chỉ dòng lỗi', exact: true }).uncheck();
  await expect(section.getByRole('row')).toHaveCount(26);
  await section.getByRole('button', { name: 'Trang sau', exact: true }).click();
  await expect(section.getByRole('row')).toHaveCount(7);
  await expect(section.getByRole('row').last().getByRole('cell').first()).toHaveText('35');
  await section.getByRole('checkbox', { name: 'Chỉ dòng lỗi', exact: true }).check();
  await expect(section.getByText('Trang 1/1 · 5 dòng', { exact: true })).toBeVisible();
  const { book } = await downloadWorkbook(page, section.getByRole('button', { name: 'Tải báo cáo lỗi', exact: true }), info);
  expect(book.SheetNames).toEqual(['Errors']);
  const report = matrix(book);
  expect(report[0]).toEqual(['Dòng Excel', 'Trường', 'Mức độ', 'Nội dung']);
  for (const [source, field] of [[5, 'amount'], [7, 'date'], [8, 'code'], [9, 'code'], [10, 'code']]) {
    expect(report.slice(1)).toEqual(expect.arrayContaining([expect.arrayContaining([source, field, 'ERROR'])]));
  }
  expect([...new Set(report.slice(1).map(row => row[0]))]).toEqual([5, 7, 8, 9, 10]);
  // A client cannot bypass the disabled confirmation button and partially import valid rows.
  const invalid = await qa.api.post(commitPath, { data: { ...response.request().postDataJSON(), previewFingerprint: preview.fingerprint } });
  expect(invalid.status()).toBe(400);
  expect(await qa.records()).toHaveLength(0);
});

test('Excel mapping distinguishes identical headers and invalidates preview on mapping, format and upsert changes', async ({ page, qa }) => {
  await qa.save({ code: 'EXIST', mode: 'ORIGINAL', amount: 10, details: 'KEEP-UNMAPPED', reason: 'CLEAR-ME' });
  const dialog = await openImport(page, qa);
  await dialog.getByLabel('Tệp Excel/CSV').setInputFiles(workbookFile({ Data: [
    ['Giá trị', 'Giá trị', 'Số tiền', 'Lý do'], ['EXIST', 'NEW-MODE', '12.5', null],
  ] }));
  await mapping(dialog, 'A').selectOption('code');
  await mapping(dialog, 'B').selectOption('code');
  await expect(dialog.getByRole('alert').filter({ hasText: 'Mỗi trường đích chỉ được ánh xạ một lần.' })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Kiểm tra và xem trước', exact: true })).toBeDisabled();
  await mapping(dialog, 'B').selectOption('mode');
  const response = await submit(page, 'Kiểm tra và xem trước', previewPath);
  expect((await response.json()).data).toMatchObject({ inserted: 0, updated: 1, canImport: true, rows: [
    { operation: 'UPDATE', data: { code: 'EXIST', mode: 'NEW-MODE', amount: 12.5, details: 'KEEP-UNMAPPED', reason: null } },
  ] });
  expect((await qa.record('EXIST')).data).toMatchObject({ mode: 'ORIGINAL', amount: 10, reason: 'CLEAR-ME' });
  await mapping(dialog, 'B').selectOption('');
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  const skipped = await submit(page, 'Kiểm tra và xem trước', previewPath);
  expect((await skipped.json()).data.rows[0].data.mode).toBe('ORIGINAL');
  await mapping(dialog, 'B').selectOption('mode');
  await expect(confirmation(dialog)).toBeDisabled();
  await submit(page, 'Kiểm tra và xem trước', previewPath);
  await dialog.getByRole('combobox', { name: 'Định dạng số văn bản', exact: true }).selectOption('vi-VN');
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  await dialog.getByRole('combobox', { name: 'Định dạng số văn bản', exact: true }).selectOption('invariant');
  await submit(page, 'Kiểm tra và xem trước', previewPath);
  const upsert = dialog.getByRole('checkbox', { name: 'Cập nhật khi trùng mã định danh', exact: true });
  await upsert.uncheck();
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  const duplicate = await submit(page, 'Kiểm tra và xem trước', previewPath);
  expect((await duplicate.json()).data).toMatchObject({ errorRows: 1, canImport: false });
  await expect(confirmation(dialog)).toBeDisabled();
  await upsert.check();
  await submit(page, 'Kiểm tra và xem trước', previewPath);
  await dialog.getByRole('combobox', { name: 'Định dạng ngày văn bản', exact: true }).selectOption('dd/MM/yyyy');
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  await submit(page, 'Kiểm tra và xem trước', previewPath);
  const committed = await submit(page, 'Xác nhận nhập 1 dòng', commitPath);
  expect((await committed.json()).data).toMatchObject({ inserted: 0, updated: 1, errorRows: 0 });
  await expect(dialog).toBeHidden();
  expect((await qa.record('EXIST')).data).toMatchObject({ mode: 'NEW-MODE', amount: 12.5, details: 'KEEP-UNMAPPED', reason: null });
  expect(await qa.records()).toHaveLength(1);
});

test('Excel import requires a fresh preview after changing the file, sheet or header row', async ({ page, qa }) => {
  const dialog = await openImport(page, qa);
  await dialog.getByLabel('Tệp Excel/CSV').setInputFiles(workbookFile({
    First: [['code', 'amount'], ['FIRST', 1]], Second: [['code', 'amount'], ['SECOND', 2]],
  }));
  await submit(page, 'Kiểm tra và xem trước', previewPath);
  await expect(confirmation(dialog)).toBeEnabled();
  await dialog.getByRole('combobox', { name: 'Sheet', exact: true }).selectOption('Second');
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  const second = await submit(page, 'Kiểm tra và xem trước', previewPath);
  expect((await second.json()).data.rows[0].data.code).toBe('SECOND');
  await dialog.getByRole('spinbutton', { name: 'Dòng tiêu đề', exact: true }).fill('2');
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  await dialog.getByRole('spinbutton', { name: 'Dòng tiêu đề', exact: true }).fill('1');
  await submit(page, 'Kiểm tra và xem trước', previewPath);
  await dialog.getByLabel('Tệp Excel/CSV').setInputFiles(workbookFile({ Final: [['code', 'amount'], ['FINAL', 3]] }, 'replacement.xlsx'));
  await expect(previewSection(dialog)).toHaveCount(0);
  await expect(confirmation(dialog)).toBeDisabled();
  const final = await submit(page, 'Kiểm tra và xem trước', previewPath);
  expect((await final.json()).data.rows[0].data.code).toBe('FINAL');
  expect(await qa.records()).toHaveLength(0);
  await submit(page, 'Xác nhận nhập 1 dòng', commitPath);
  await expect(dialog).toBeHidden();
  expect((await qa.records()).map(record => record.recordKey)).toEqual(['FINAL']);
});

test('Excel confirmation rejects a stale preview after another write and succeeds only after rechecking', async ({ page, qa }) => {
  const original = await qa.save({ code: 'STALE', amount: 1 });
  const dialog = await openImport(page, qa);
  await dialog.getByLabel('Tệp Excel/CSV').setInputFiles(workbookFile({ Data: [['code', 'amount'], ['STALE', 20]] }));
  const first = await submit(page, 'Kiểm tra và xem trước', previewPath);
  const fingerprint = (await first.json()).data.fingerprint;
  await post(qa.api, '/api/records', { id: original.id, appId: qa.app.id, moduleId: qa.module.id, tableId: qa.table.id, data: { amount: 99 } });
  await submit(page, 'Xác nhận nhập 1 dòng', commitPath, 409);
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('alert')).not.toBeEmpty();
  await expect(confirmation(dialog)).toBeDisabled();
  expect((await qa.record('STALE')).data.amount).toBe(99);
  const rechecked = await submit(page, 'Kiểm tra và xem trước', previewPath);
  expect((await rechecked.json()).data.fingerprint).not.toBe(fingerprint);
  await submit(page, 'Xác nhận nhập 1 dòng', commitPath);
  await expect(dialog).toBeHidden();
  expect((await qa.record('STALE')).data.amount).toBe(20);
  expect(await qa.records()).toHaveLength(1);
});
