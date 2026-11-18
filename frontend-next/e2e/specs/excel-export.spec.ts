import { test, expect, post } from '../fixtures';
import { exportWorkbook, matrix, seedByImport } from '../excel';
import type { Page } from '@playwright/test';

const selectWithOption = (page: Page, name: string) => page.getByRole('combobox').filter({
  has: page.getByRole('option', { name, exact: true }),
});

test('Excel export includes every filtered row beyond page and 1000-row limits in the selected numeric order', async ({ page, qa }, info) => {
  const count = 1057;
  const codes = Array.from({ length: count }, (_, i) => `TARGET-${String(i + 1).padStart(4, '0')}`);
  await seedByImport(qa, codes.map((code, i) => ({ code, mode: 'KEEP', amount: i + 1.5,
    details: i === 0 ? '=SUM(1,2)' : `Details ${i + 1}` })));
  await qa.save({ code: 'OTHER-SEARCH', mode: 'KEEP', amount: -10 });
  await qa.save({ code: 'TARGET-OTHER-COLUMN', mode: 'OTHER', amount: -20 });
  await post(qa.api, '/api/records', { appId: qa.app.id, moduleId: qa.module.id, tableId: qa.table.id,
    status: 'ERROR', data: { code: 'TARGET-ERROR', mode: 'KEEP', amount: -30 } });
  const deleted = await qa.save({ code: 'TARGET-DELETED', mode: 'KEEP', amount: -40 });
  expect((await qa.api.delete(`/api/records/${deleted.id}`)).ok()).toBe(true);

  await qa.openRuntime(page);
  await page.getByPlaceholder('Tìm nhanh trên bảng...', { exact: true }).fill('TARGET');
  await selectWithOption(page, 'Tất cả trạng thái').selectOption('ACTIVE');
  await page.getByRole('button', { name: 'Lọc & Xem', exact: true }).click();
  await selectWithOption(page, '-- Chọn cột cần lọc --').selectOption('mode');
  await page.getByPlaceholder('Nhập giá trị cần lọc...', { exact: true }).fill('KEEP');
  await page.getByRole('button', { name: 'Áp dụng', exact: true }).click();
  await page.getByRole('columnheader', { name: /Cột Số tiền/ }).click();
  await selectWithOption(page, '10 dòng / trang').selectOption('10');
  const loaded = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === '/api/records' && url.searchParams.get('tableId') === qa.table.id
      && url.searchParams.get('page') === '2' && url.searchParams.get('sortDir') === 'ASC';
  });
  await page.getByRole('button', { name: 'Trang kế tiếp', exact: true }).click();
  const result = (await (await loaded).json()).data;
  expect(result).toMatchObject({ page: 2, limit: 10, total: count });
  expect(result.items.map((item: { recordKey: string }) => item.recordKey)).toEqual(codes.slice(10, 20));
  await expect(page.getByRole('row')).toHaveCount(11);
  await expect(page.getByRole('row').filter({ hasText: 'TARGET-0011' })).toBeVisible();
  const { book, response, name } = await exportWorkbook(page, info);
  expect(name).toBe(`${qa.table.tableName}.xlsx`);
  const params = new URL(response.url()).searchParams;
  expect(Object.fromEntries(params)).toMatchObject({ tableId: qa.table.id, search: 'TARGET', status: 'ACTIVE', sortField: 'amount', sortDir: 'ASC' });
  expect(JSON.parse(params.get('filters')!)).toEqual({ mode: 'KEEP' });
  expect(params.has('page')).toBe(false);
  expect(params.has('limit')).toBe(false);
  const rows = matrix(book);
  expect(rows[0]).toEqual(['code', 'mode', 'amount', 'details', 'reason', 'approved']);
  expect(rows).toHaveLength(count + 1);
  expect(rows.slice(1).map(row => row[0])).toEqual(codes);
  expect(rows.slice(1).map(row => row[2])).toEqual(Array.from({ length: count }, (_, i) => i + 1.5));
  const sheet = book.Sheets[book.SheetNames[0]];
  expect(sheet.C2).toMatchObject({ t: 'n', v: 1.5 });
  expect(sheet.D2).toMatchObject({ t: 's', v: '=SUM(1,2)' });
  expect(sheet.D2.f).toBeUndefined();
});

test('Excel export with no matching records still downloads a readable header-only workbook', async ({ page, qa }, info) => {
  await qa.save({ code: 'EXISTING', amount: 10 });
  await qa.openRuntime(page);
  const filtered = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === '/api/records' && url.searchParams.get('search') === 'NO-MATCH-FOR-EXPORT';
  });
  await page.getByPlaceholder('Tìm nhanh trên bảng...', { exact: true }).fill('NO-MATCH-FOR-EXPORT');
  expect((await (await filtered).json()).data.total).toBe(0);
  const { book } = await exportWorkbook(page, info);
  expect(matrix(book)).toEqual([['code', 'mode', 'amount', 'details', 'reason', 'approved']]);
});

test('Excel export respects ownership and hidden columns and denies export after permission revocation', async ({ page, playwright, baseURL, qa }, info) => {
  const roleCode = `EXCEL_${qa.app.code}`;
  const username = `excel_${qa.app.code.toLowerCase()}`;
  const permissions = ['record.view', 'record.create', 'record.export'];
  const definition = { code: roleCode, name: `Excel role ${qa.app.code}`, defaultAppPermissions: permissions,
    appPermissions: [{ appId: qa.app.id, permissions, rowScope: 'OWNER_ONLY' }] };
  const role = await post<{ id: string }>(qa.api, '/api/roles', definition);
  const user = await post<{ id: string }>(qa.api, '/api/auth/users', { username, fullName: 'Excel E2E user',
    password: 'Excel-E2E-123456', roleCode, roleCodes: [roleCode], allowedApps: [qa.app.code], permissions });
  const userApi = await playwright.request.newContext({ baseURL });
  try {
    await qa.addColumn({ columnCode: 'secret', columnName: 'Bí mật', dataType: 'STRING', hiddenForRoles: [roleCode] });
    await post(userApi, '/api/auth/login', { username, password: 'Excel-E2E-123456' });
    const codes = Array.from({ length: 25 }, (_, i) => `OWN-${String(i).padStart(3, '0')}`);
    await seedByImport(qa, codes.map((code, amount) => ({ code, amount })), userApi);
    const own = await qa.record('OWN-000');
    await post(qa.api, '/api/records', { id: own.id, appId: qa.app.id, moduleId: qa.module.id, tableId: qa.table.id,
      data: { secret: 'PRIVATE-OWN-SECRET' } });
    await qa.save({ code: 'OWN-FOREIGN', amount: -1, secret: 'PRIVATE-FOREIGN-SECRET' });
    await page.context().addCookies((await userApi.storageState()).cookies);
    await page.goto(`/apps/${qa.app.code}`);
    await expect(page.getByRole('heading', { name: qa.table.displayName, exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Bí mật', exact: true })).toHaveCount(0);
    const { book } = await exportWorkbook(page, info);
    const rows = matrix(book);
    expect(rows[0]).toEqual(['code', 'mode', 'amount', 'details', 'reason', 'approved']);
    expect(rows).toHaveLength(26);
    expect(rows.slice(1).map(row => row[0]).sort()).toEqual(codes);
    expect(JSON.stringify(rows)).not.toContain('PRIVATE-');
    expect(JSON.stringify(rows)).not.toContain('OWN-FOREIGN');

    const reduced = ['record.view', 'record.create'];
    await post(qa.api, '/api/roles', { ...definition, id: role.id, defaultAppPermissions: reduced,
      appPermissions: [{ appId: qa.app.id, permissions: reduced, rowScope: 'OWNER_ONLY' }] });
    await post(qa.api, '/api/auth/users', { id: user.id, username, fullName: 'Excel E2E user', roleCode,
      roleCodes: [roleCode], allowedApps: [qa.app.code], permissions: reduced });
    await page.reload();
    await expect(page.getByRole('heading', { name: qa.table.displayName, exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Tiện ích', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Xuất toàn bộ kết quả lọc (.xlsx)', exact: true })).toHaveCount(0);
    expect((await userApi.get(`/api/records/export?tableId=${qa.table.id}`)).status()).toBe(403);
  } finally {
    await userApi.dispose();
    expect((await qa.api.delete(`/api/auth/users/${user.id}`)).ok()).toBe(true);
    expect((await qa.api.delete(`/api/roles/${role.id}`)).ok()).toBe(true);
  }
});
