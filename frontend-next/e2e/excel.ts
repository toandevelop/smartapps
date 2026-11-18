import { readFile } from 'node:fs/promises';
import * as XLSX from 'xlsx';
import { expect, type FormApp, post } from './fixtures';
import type { APIRequestContext, Locator, Page, TestInfo } from '@playwright/test';

export const previewPath = '/api/records/bulk-import/preview';
export const commitPath = '/api/records/bulk-import';

export function workbookFile(sheets: Record<string, unknown[][]>, name = 'e2e-import.xlsx') {
  const book = XLSX.utils.book_new();
  for (const [sheetName, rows] of Object.entries(sheets)) XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet(rows), sheetName);
  return { name, mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: XLSX.write(book, { type: 'buffer', bookType: 'xlsx' }) as Buffer };
}

export async function openImport(page: Page, qa: FormApp) {
  await qa.openRuntime(page);
  await page.getByRole('button', { name: 'Tiện ích', exact: true }).click();
  await page.getByRole('button', { name: /^Import dữ liệu Excel/ }).click();
  const dialog = page.getByRole('dialog', { name: `Nhập Excel · ${qa.table.displayName}`, exact: true });
  await expect(dialog).toBeVisible();
  return dialog;
}

export const mapping = (dialog: Locator, column: string) => dialog.getByRole('combobox', { name: `Trường đích cho cột ${column}`, exact: true });
export const previewSection = (dialog: Locator) => dialog.getByRole('region', { name: 'Xem trước kết quả nhập', exact: true });
export const confirmation = (dialog: Locator) => dialog.getByRole('button', { name: /^Xác nhận nhập/ });

export async function downloadWorkbook(page: Page, button: Locator, info: TestInfo) {
  const pending = page.waitForEvent('download');
  await button.click();
  const download = await pending;
  expect(await download.failure()).toBeNull();
  expect(download.suggestedFilename()).toMatch(/\.xlsx$/);
  const file = info.outputPath(download.suggestedFilename());
  await download.saveAs(file);
  const book = XLSX.read(await readFile(file), { type: 'buffer', cellFormula: true });
  expect(book.SheetNames.length).toBeGreaterThan(0);
  return { book, file, name: download.suggestedFilename() };
}

export function matrix(book: XLSX.WorkBook, sheet = book.SheetNames[0]): unknown[][] {
  return XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheet], { header: 1, defval: null, raw: true });
}

export async function seedByImport(qa: FormApp, rows: Record<string, unknown>[], api: APIRequestContext = qa.api) {
  const request = { appId: qa.app.id, moduleId: qa.module.id, tableId: qa.table.id, rows,
    upsertByIdentityKey: true, numberFormat: 'invariant', dateFormat: 'yyyy-MM-dd' };
  const preview = await post<{ canImport: boolean; fingerprint: string }>(api, previewPath, request);
  expect(preview.canImport).toBe(true);
  const result = await post<{ inserted: number; updated: number }>(api, commitPath, { ...request, previewFingerprint: preview.fingerprint });
  expect(result).toMatchObject({ inserted: rows.length, updated: 0 });
}

export async function exportWorkbook(page: Page, info: TestInfo) {
  await page.getByRole('button', { name: 'Tiện ích', exact: true }).click();
  const pending = page.waitForResponse(response => new URL(response.url()).pathname === '/api/records/export');
  const result = await downloadWorkbook(page, page.getByRole('button', { name: 'Xuất toàn bộ kết quả lọc (.xlsx)', exact: true }), info);
  const response = await pending;
  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
  return { ...result, response };
}
