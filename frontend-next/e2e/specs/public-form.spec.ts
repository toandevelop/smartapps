import { test, expect, submit } from '../fixtures';

test('anonymous public form enforces rules and resets defaults for a second submission', async ({ page, qa }) => {
  await qa.setRules();
  await qa.openPublic(page);
  const code = page.getByRole('textbox', { name: /^Mã phiếu/ });
  const mode = page.getByRole('textbox', { name: 'Chế độ', exact: true });
  const reason = page.getByRole('textbox', { name: /^Lý do/ });
  const approved = page.getByRole('checkbox', { name: 'Xác nhận', exact: true });
  await expect(mode).toHaveValue('normal');
  await expect(approved).not.toBeChecked();
  await expect(page.getByRole('textbox', { name: 'Chi tiết', exact: true })).toHaveCount(0);
  await code.fill('PUBLIC-FIRST');
  await mode.fill('show');
  const details = page.getByRole('textbox', { name: 'Chi tiết', exact: true });
  await details.fill('UNSAVED');
  await approved.check();
  await expect(details).toBeDisabled();
  await expect(details).toHaveValue('DEFAULT');
  await expect(reason).not.toHaveAttribute('required');
  await page.getByRole('spinbutton', { name: 'Số tiền', exact: true }).fill('100.5');
  await expect(reason).toHaveAttribute('required');
  await page.getByRole('button', { name: 'Gửi Biểu mẫu', exact: true }).click();
  expect(await reason.evaluate((el: HTMLTextAreaElement) => el.validity.valueMissing)).toBe(true);
  expect(await qa.records()).toHaveLength(0);
  await reason.fill('   ');
  await page.getByRole('button', { name: 'Gửi Biểu mẫu', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: "Trường 'Lý do'" })).toBeVisible();
  expect(await qa.records()).toHaveLength(0);
  await reason.fill('Lý do public hợp lệ');
  await submit(page, 'Gửi Biểu mẫu', `/api/public/forms/${qa.table.id}/submit`);
  await expect(page.getByRole('heading', { name: 'Gửi thông tin thành công!', exact: true })).toBeVisible();
  expect((await qa.record('PUBLIC-FIRST')).data).toMatchObject({ amount: 100.5, details: 'DEFAULT', approved: true });

  await page.getByRole('button', { name: 'Gửi phiếu khác', exact: true }).click();
  await expect(code).toHaveValue('');
  await expect(mode).toHaveValue('normal');
  await expect(page.getByRole('spinbutton', { name: 'Số tiền', exact: true })).toHaveValue('0');
  await expect(approved).not.toBeChecked();
  await expect(details).toHaveCount(0);
  // The lost-defaults regression made this condition false on the second form.
  await expect(reason).toHaveAttribute('required');
  await code.fill('PUBLIC-SECOND');
  await reason.fill('Phiếu thứ hai giữ đúng điều kiện');
  await submit(page, 'Gửi Biểu mẫu', `/api/public/forms/${qa.table.id}/submit`);
  await expect(page.getByRole('heading', { name: 'Gửi thông tin thành công!', exact: true })).toBeVisible();
  expect((await qa.record('PUBLIC-SECOND')).data).toMatchObject({ mode: 'normal', amount: 0, details: 'DEFAULT', approved: false });
  expect(await qa.records()).toHaveLength(2);
  expect((await page.context().cookies()).some(c => c.name === 'smartapps_token')).toBe(false);
});

test('public form normalizes boolean default 1 and retains it for subsequent submissions', async ({ page, qa }) => {
  await qa.setRules();
  await qa.setApprovedDefault('1');
  await qa.openPublic(page);
  for (const code of ['PUBLIC-DEFAULT-FIRST', 'PUBLIC-DEFAULT-SECOND']) {
    await expect(page.getByRole('checkbox', { name: 'Xác nhận', exact: true })).toBeChecked();
    await expect(page.getByRole('textbox', { name: 'Chế độ', exact: true })).toHaveValue('normal');
    await expect(page.getByRole('textbox', { name: /^Lý do/ })).not.toHaveAttribute('required');
    await page.getByRole('textbox', { name: /^Mã phiếu/ }).fill(code);
    await page.getByRole('textbox', { name: 'Chế độ', exact: true }).fill('show');
    await expect(page.getByRole('textbox', { name: 'Chi tiết', exact: true })).toBeDisabled();
    await expect(page.getByRole('textbox', { name: 'Chi tiết', exact: true })).toHaveValue('DEFAULT');
    await submit(page, 'Gửi Biểu mẫu', `/api/public/forms/${qa.table.id}/submit`);
    await expect(page.getByRole('heading', { name: 'Gửi thông tin thành công!', exact: true })).toBeVisible();
    expect((await qa.record(code)).data).toMatchObject({ approved: true, details: 'DEFAULT', amount: 0 });
    await page.getByRole('button', { name: 'Gửi phiếu khác', exact: true }).click();
  }
  expect(await qa.records()).toHaveLength(2);
});

test('backend rejects anonymous writes that bypass hidden, readonly, required or typed-source controls', async ({ page, qa }) => {
  await qa.setRules();
  await qa.openPublic(page);
  const attempts = [
    { field: 'details', data: { code: 'BAD-HIDDEN', mode: 'normal', approved: false, reason: 'OK', details: 'HACK' } },
    { field: 'details', data: { code: 'BAD-LOCKED', mode: 'show', approved: true, details: 'HACK' } },
    { field: 'reason', data: { code: 'BAD-REQUIRED', approved: true, amount: 150 } },
    { field: 'amount', data: { code: 'BAD-TYPE', approved: true, amount: 'invalid', reason: 'OK' } },
  ];
  for (const attempt of attempts) {
    // This request context shares the anonymous browser cookies, not the admin fixture.
    const response = await page.request.post(`/api/public/forms/${qa.table.id}/submit`, { data: attempt.data });
    expect(response.status(), await response.text()).toBe(422);
    expect(await response.json()).toMatchObject({ success: false, errors: expect.arrayContaining([expect.objectContaining({ field: attempt.field })]) });
  }
  expect(await qa.records()).toHaveLength(0);
  await expect(page.getByRole('button', { name: 'Gửi Biểu mẫu', exact: true })).toBeVisible();
});
