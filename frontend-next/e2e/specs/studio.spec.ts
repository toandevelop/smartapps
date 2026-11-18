import { test, expect, defaultRules, input, submit } from '../fixtures';
import type { Page } from '@playwright/test';

const group = (page: Page, index: number) => page.getByRole('group', { name: `Quy tắc ${index}`, exact: true });
const combo = (page: Page, index: number, name: string) => group(page, index).getByRole('combobox', { name, exact: true });

test('Studio creates, persists and reloads all three rules, and preview enforces them', async ({ page, qa }) => {
  await qa.openStudio(page);
  await page.getByRole('button', { name: 'Sửa Bảng', exact: true }).click();
  const add = page.getByRole('button', { name: 'Thêm quy tắc form', exact: true });
  await expect(add).toBeEnabled();
  await add.click();
  await combo(page, 1, 'Trường đích').selectOption('details');
  await combo(page, 1, 'Trường điều kiện 1').selectOption('mode');
  await group(page, 1).getByRole('textbox', { name: 'Giá trị', exact: true }).fill('show');
  await add.click();
  await combo(page, 2, 'Trường đích').selectOption('reason');
  await combo(page, 2, 'Thuộc tính').selectOption('required');
  await combo(page, 2, 'Trường điều kiện 1').selectOption('amount');
  await combo(page, 2, 'So sánh').selectOption('GREATER_OR_EQUAL');
  await group(page, 2).getByRole('spinbutton', { name: 'Giá trị', exact: true }).fill('100');
  await combo(page, 2, 'Kết hợp điều kiện').selectOption('ANY');
  await group(page, 2).getByRole('button', { name: 'Thêm điều kiện', exact: true }).click();
  await combo(page, 2, 'Trường điều kiện 2').selectOption('approved');
  await combo(page, 2, 'Giá trị').selectOption('false');
  await add.click();
  await combo(page, 3, 'Trường đích').selectOption('details');
  await combo(page, 3, 'Thuộc tính').selectOption('readOnly');
  await combo(page, 3, 'Trường điều kiện 1').selectOption('approved');
  await combo(page, 3, 'Giá trị').selectOption('true');
  await submit(page, 'Lưu Bảng Động', '/api/tables');
  await expect(page.getByRole('heading', { name: 'Sửa Bảng Động', exact: true })).toBeHidden();
  const saved = (await qa.currentTable()).formRules!;
  expect(saved.map(({ id, ...rule }) => rule)).toEqual(defaultRules.map(({ id, ...rule }) => rule));
  await page.reload();
  await page.getByRole('button', { name: 'Sửa Bảng', exact: true }).click();
  await expect(combo(page, 1, 'Trường đích')).toHaveValue('details');
  await expect(combo(page, 2, 'Thuộc tính')).toHaveValue('required');
  await expect(combo(page, 2, 'Kết hợp điều kiện')).toHaveValue('ANY');
  await expect(combo(page, 2, 'Trường điều kiện 2')).toHaveValue('approved');
  await expect(combo(page, 3, 'Thuộc tính')).toHaveValue('readOnly');
  await page.getByRole('button', { name: 'Hủy', exact: true }).click();
  await page.getByRole('button', { name: 'Xem thử Dữ liệu (0)', exact: true }).click();
  await page.getByRole('button', { name: 'Thêm bản ghi thử', exact: true }).click();
  await input(page, 'mã phiếu').fill('STUDIO-PREVIEW');
  await expect(input(page, 'chi tiết')).toHaveCount(0);
  await page.getByRole('button', { name: 'Lưu bản ghi', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: "Trường 'Lý do'" })).toBeVisible();
  expect(await qa.records()).toHaveLength(0);
  await input(page, 'chế độ').fill('show');
  await expect(input(page, 'chi tiết')).toHaveValue('DEFAULT');
  await input(page, 'lý do').fill('Tạo từ Studio preview');
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('row').filter({ hasText: 'STUDIO-PREVIEW' })).toBeVisible();
  expect((await qa.record('STUDIO-PREVIEW')).data).toMatchObject({ mode: 'show', details: 'DEFAULT', approved: false });
});

test('Studio reports conflicting rules without losing the draft or persisting bad metadata', async ({ page, qa }) => {
  await qa.setRules();
  await qa.openStudio(page);
  await page.getByRole('button', { name: 'Sửa Bảng', exact: true }).click();
  await page.getByRole('button', { name: 'Thêm quy tắc form', exact: true }).click();
  await combo(page, 4, 'Trường đích').selectOption('details');
  await combo(page, 4, 'Trường điều kiện 1').selectOption('mode');
  await group(page, 4).getByRole('textbox', { name: 'Giá trị', exact: true }).fill('show');
  await submit(page, 'Lưu Bảng Động', '/api/tables', 400);
  await expect(page.getByRole('alert').filter({ hasText: 'Mỗi trường chỉ có một quy tắc' })).toBeVisible();
  await expect(group(page, 4)).toBeVisible();
  await expect(combo(page, 4, 'Trường đích')).toHaveValue('details');
  expect((await qa.currentTable()).formRules).toEqual(defaultRules);
  await group(page, 4).getByRole('button', { name: 'Xóa quy tắc', exact: true }).click();
  await submit(page, 'Lưu Bảng Động', '/api/tables');
  await expect(page.getByRole('heading', { name: 'Sửa Bảng Động', exact: true })).toBeHidden();
  expect((await qa.currentTable()).formRules).toEqual(defaultRules);
});

test('Studio waits for real column metadata before exposing the rule editor', async ({ page, qa }) => {
  await qa.setRules();
  await qa.openStudio(page);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  // Hold the real request, then continue it; no fabricated metadata response.
  const route = `**/api/columns?tableId=${qa.table.id}`;
  await page.route(route, async request => { await gate; await request.continue(); });
  try {
    await page.getByRole('button', { name: 'Sửa Bảng', exact: true }).click();
    await expect(page.getByRole('status')).toHaveText('Đang tải các trường để cấu hình quy tắc...');
    await expect(page.getByRole('region', { name: 'Quy tắc biểu mẫu có điều kiện' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Lưu Bảng Động', exact: true })).toBeDisabled();
    release();
    await expect(combo(page, 1, 'Trường đích')).toHaveValue('details');
    await expect(page.getByRole('button', { name: 'Lưu Bảng Động', exact: true })).toBeEnabled();
    await expect(page.getByText('Tạo bảng và các cột trước, sau đó mở sửa bảng để cấu hình quy tắc.')).toHaveCount(0);
  } finally { release(); await page.unroute(route); }
});
