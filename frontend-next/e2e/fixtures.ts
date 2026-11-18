import { randomUUID } from 'node:crypto';
import { test as base, expect, type APIRequestContext, type Page, type Response } from '@playwright/test';
import type { AppRecord, FormRule, SysApp, SysColumn, SysModule, SysTable } from '../src/lib/types';

export const defaultRules: FormRule[] = [
  { id: 'details-visible', targetField: 'details', property: 'visible', value: true, elseValue: false, match: 'ALL',
    conditions: [{ field: 'mode', operator: 'EQUALS', value: 'show' }] },
  { id: 'reason-required', targetField: 'reason', property: 'required', value: true, elseValue: false, match: 'ANY',
    conditions: [{ field: 'amount', operator: 'GREATER_OR_EQUAL', value: '100' }, { field: 'approved', operator: 'EQUALS', value: 'false' }] },
  { id: 'details-readonly', targetField: 'details', property: 'readOnly', value: true, elseValue: false, match: 'ALL',
    conditions: [{ field: 'approved', operator: 'EQUALS', value: 'true' }] },
];

export async function post<T>(api: APIRequestContext, path: string, body: unknown): Promise<T> {
  const response = await api.post(path, { data: body });
  expect(response.status(), `${path}: ${await response.text()}`).toBe(200);
  const result = await response.json();
  expect(result.success).toBe(true);
  return result.data as T;
}

export class FormApp {
  constructor(readonly api: APIRequestContext, readonly app: SysApp, readonly module: SysModule,
    readonly table: SysTable, readonly columns: SysColumn[]) {}

  async setRules(rules: FormRule[] = defaultRules) {
    return post<SysTable>(this.api, '/api/tables', { ...this.table, formRules: rules });
  }
  async currentTable(): Promise<SysTable> {
    const response = await this.api.get(`/api/tables?appId=${this.app.id}`);
    expect(response.ok()).toBe(true);
    return (await response.json()).data.find((item: SysTable) => item.id === this.table.id);
  }
  async setApprovedDefault(value: string) {
    const column = this.columns.find(c => c.columnCode === 'approved')!;
    await post(this.api, '/api/columns', { ...column, defaultValue: value });
  }
  async addColumn(column: Pick<SysColumn, 'columnCode' | 'columnName'> & Partial<SysColumn>): Promise<SysColumn> {
    const saved = await post<SysColumn>(this.api, '/api/columns', { tableId: this.table.id, orderIndex: this.columns.length + 1, ...column });
    this.columns.push(saved);
    return saved;
  }
  async save(data: Record<string, unknown>): Promise<AppRecord> {
    return post<AppRecord>(this.api, '/api/records', { appId: this.app.id, moduleId: this.module.id, tableId: this.table.id, data });
  }
  async records(): Promise<AppRecord[]> {
    const records: AppRecord[] = [];
    for (let page = 1; ; page++) {
      const response = await this.api.get(`/api/records?tableId=${this.table.id}&limit=1000&page=${page}`);
      expect(response.ok()).toBe(true);
      const data = (await response.json()).data;
      records.push(...data.items);
      if (page >= data.totalPages) return records;
    }
  }
  async record(code: string): Promise<AppRecord> {
    const record = (await this.records()).find(r => r.recordKey === code);
    expect(record, `Saved record ${code}`).toBeDefined();
    return record!;
  }
  async signIn(page: Page) {
    // The real login endpoint supplies the HttpOnly cookie; no auth or metadata mocks.
    await page.context().addCookies((await this.api.storageState()).cookies);
  }
  async openStudio(page: Page) {
    await this.signIn(page);
    await page.goto(`/studio?appId=${encodeURIComponent(this.app.id)}`);
    await expect(page.getByRole('link', { name: `Chạy App ${this.app.code}`, exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sửa Bảng', exact: true })).toBeVisible();
    await expect(page.getByText(this.table.tableName, { exact: true }).first()).toBeVisible();
  }
  async openRuntime(page: Page) {
    await this.signIn(page);
    await page.goto(`/apps/${this.app.code}`);
    await expect(page.getByRole('heading', { name: this.app.name, exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: this.table.displayName, exact: true })).toBeVisible();
  }
  async openPublic(page: Page) {
    expect((await page.context().cookies()).some(c => c.name === 'smartapps_token')).toBe(false);
    await page.goto(`/forms/${this.table.id}`);
    await expect(page.getByRole('heading', { name: this.table.displayName, exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Gửi Biểu mẫu', exact: true })).toBeVisible();
  }
}

export const test = base.extend<{ adminApi: APIRequestContext; qa: FormApp }>({
  adminApi: async ({ playwright, baseURL }, use) => {
    const api = await playwright.request.newContext({ baseURL });
    try {
      await post(api, '/api/auth/login', { username: 'admin', password: '123456' });
      await use(api);
    } finally { await api.dispose(); }
  },
  qa: async ({ adminApi }, use) => {
    const suffix = randomUUID().replaceAll('-', '').slice(0, 12);
    const app = await post<SysApp>(adminApi, '/api/apps', { code: `E2E_${suffix.toUpperCase()}`, name: `E2E Forms ${suffix}`, orderIndex: 0 });
    try {
      const module = await post<SysModule>(adminApi, '/api/modules', { appId: app.id, code: 'FORMS', name: 'Kiểm thử biểu mẫu' });
      const table = await post<SysTable>(adminApi, '/api/tables', { appId: app.id, moduleId: module.id,
        tableName: `e2e_form_${suffix}`, displayName: 'Phiếu E2E điều kiện', publicFormEnabled: true, rowScope: 'ALL' });
      const fields = [
        { columnCode: 'code', columnName: 'Mã phiếu', dataType: 'STRING', isRequired: true, isIdentityKey: true },
        { columnCode: 'mode', columnName: 'Chế độ', dataType: 'STRING', defaultValue: 'normal' },
        { columnCode: 'amount', columnName: 'Số tiền', dataType: 'NUMBER', defaultValue: '0' },
        { columnCode: 'details', columnName: 'Chi tiết', dataType: 'STRING', defaultValue: 'DEFAULT' },
        { columnCode: 'reason', columnName: 'Lý do', dataType: 'TEXTAREA' },
        { columnCode: 'approved', columnName: 'Xác nhận', dataType: 'BOOLEAN', defaultValue: 'false' },
      ];
      const columns: SysColumn[] = [];
      for (const [index, field] of fields.entries()) {
        columns.push(await post<SysColumn>(adminApi, '/api/columns', { tableId: table.id, ...field, orderIndex: index + 1 }));
      }
      await use(new FormApp(adminApi, app, module, table, columns));
    } finally {
      const deleted = await adminApi.delete(`/api/apps/${app.id}`);
      expect(deleted.ok(), 'Remove the app created for this test').toBe(true);
    }
  },
  page: async ({ page }, use) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await use(page);
    expect(errors, 'Unexpected browser JavaScript errors').toEqual([]);
  },
});

export { expect };

export async function submit(page: Page, button: string, path: string, status = 200): Promise<Response> {
  const pending = page.waitForResponse(response => new URL(response.url()).pathname === path && response.request().method() === 'POST');
  await page.getByRole('button', { name: button, exact: true }).click();
  const response = await pending;
  expect(response.status(), await response.text()).toBe(status);
  return response;
}

export const input = (page: Page, name: string) => page.getByPlaceholder(`Nhập ${name}...`, { exact: true });
