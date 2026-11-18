import { test, expect, input, submit } from '../fixtures';

test('runtime updates visibility, resets protected drafts and enforces conditional required before saving', async ({ page, qa }) => {
  await qa.setRules();
  await qa.openRuntime(page);
  await page.getByRole('button', { name: 'Thêm Phiếu E2E điều kiện', exact: true }).click();
  await input(page, 'mã phiếu').fill('RUNTIME-NEW');
  await expect(input(page, 'chi tiết')).toHaveCount(0);
  await page.getByRole('button', { name: 'Lưu bản ghi', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: "Trường 'Lý do'" })).toBeVisible();
  expect(await qa.records()).toHaveLength(0);
  await input(page, 'chế độ').fill('show');
  await input(page, 'chi tiết').fill('UNSAVED-HIDDEN');
  await input(page, 'chế độ').fill('normal');
  await expect(input(page, 'chi tiết')).toHaveCount(0);
  await input(page, 'chế độ').fill('show');
  await expect(input(page, 'chi tiết')).toHaveValue('DEFAULT');
  await input(page, 'chi tiết').fill('UNSAVED-LOCKED');
  await page.getByRole('switch', { name: 'Không / Sai', exact: true }).click();
  await expect(input(page, 'chi tiết')).toBeDisabled();
  await expect(input(page, 'chi tiết')).toHaveValue('DEFAULT');
  // Both OR branches are now false: the empty reason must be allowed.
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('button', { name: 'Lưu bản ghi', exact: true })).toBeHidden();
  expect((await qa.record('RUNTIME-NEW')).data).toMatchObject({ mode: 'show', amount: 0, details: 'DEFAULT', approved: true });
});

test('runtime unlock requires a separate save and later hidden fields retain stored values', async ({ page, qa }) => {
  await qa.setRules();
  await qa.save({ code: 'RUNTIME-LOCKED', mode: 'show', amount: 0, approved: true });
  await qa.openRuntime(page);
  const row = page.getByRole('row').filter({ hasText: 'RUNTIME-LOCKED' });
  await row.getByRole('button', { name: 'Mở form chi tiết', exact: true }).click();
  await expect(input(page, 'chi tiết')).toBeDisabled();
  await page.getByRole('switch', { name: 'Có / Đúng', exact: true }).click();
  await expect(input(page, 'chi tiết')).toBeDisabled();
  await input(page, 'lý do').fill('Mở khóa để cập nhật');
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('button', { name: 'Lưu bản ghi', exact: true })).toBeHidden();
  await row.getByRole('button', { name: 'Mở form chi tiết', exact: true }).click();
  await expect(input(page, 'chi tiết')).toBeEnabled();
  await input(page, 'chi tiết').fill('STORED-DETAILS');
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('button', { name: 'Lưu bản ghi', exact: true })).toBeHidden();
  await row.getByRole('button', { name: 'Mở form chi tiết', exact: true }).click();
  await input(page, 'chi tiết').fill('DO-NOT-SAVE');
  await input(page, 'chế độ').fill('normal');
  await expect(input(page, 'chi tiết')).toHaveCount(0);
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('button', { name: 'Lưu bản ghi', exact: true })).toBeHidden();
  expect((await qa.record('RUNTIME-LOCKED')).data).toMatchObject({ mode: 'normal', details: 'STORED-DETAILS', approved: false });
});

test('runtime honors numeric boolean defaults and fractional numeric conditions', async ({ page, qa }) => {
  await qa.setRules();
  await qa.setApprovedDefault('1');
  await qa.openRuntime(page);
  await page.getByRole('button', { name: 'Thêm Phiếu E2E điều kiện', exact: true }).click();
  await input(page, 'mã phiếu').fill('RUNTIME-DEFAULT');
  await expect(page.getByRole('switch', { name: 'Có / Đúng', exact: true })).toBeChecked();
  await input(page, 'chế độ').fill('show');
  await expect(input(page, 'chi tiết')).toBeDisabled();
  await expect(input(page, 'chi tiết')).toHaveValue('DEFAULT');
  await page.getByRole('spinbutton').fill('100.5');
  await page.getByRole('button', { name: 'Lưu bản ghi', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: "Trường 'Lý do'" })).toBeVisible();
  expect(await qa.records()).toHaveLength(0);
  await input(page, 'lý do').fill('Đạt ngưỡng số tiền thập phân');
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('button', { name: 'Lưu bản ghi', exact: true })).toBeHidden();
  expect((await qa.record('RUNTIME-DEFAULT')).data).toMatchObject({ amount: 100.5, approved: true, details: 'DEFAULT' });
});
