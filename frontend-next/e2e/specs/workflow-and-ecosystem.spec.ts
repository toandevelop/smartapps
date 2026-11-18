import { randomUUID } from 'node:crypto';
import { test, expect, input, post, submit } from '../fixtures';

test('Record 360 drawer supports selective audit restore and approval recall/resubmit/approve flow', async ({ page, adminApi, qa }) => {
  await post(adminApi, '/api/tables', { ...qa.table, approvalEnabled: true });
  const created = await qa.save({ code: 'WF-001', mode: 'normal', amount: 50, reason: 'Lý do ban đầu' });

  await qa.openRuntime(page);
  const row = page.getByRole('row').filter({ hasText: 'WF-001' });
  await row.getByRole('button', { name: 'Mở form chi tiết', exact: true }).click();
  await input(page, 'lý do').fill('Lý do đã sửa sai');
  await submit(page, 'Lưu bản ghi', '/api/records');
  await expect(page.getByRole('button', { name: 'Lưu bản ghi', exact: true })).toBeHidden();

  await row.getByRole('button', { name: 'Xem Hồ sơ Chi tiết Đa Tab (360°)', exact: true }).click();
  const auditTab = page.getByRole('button', { name: /^Lịch sử \(/ });
  await expect(auditTab).toBeVisible();
  await auditTab.click();

  const updateAuditCard = page.locator('.card').filter({ hasText: 'Lý do đã sửa sai' });
  await expect(updateAuditCard.getByText('Lý do ban đầu', { exact: true })).toBeVisible();
  await expect(updateAuditCard.getByText('Lý do đã sửa sai', { exact: true })).toBeVisible();

  const restoreRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === `/api/records/${created.id}/restore-fields` && res.request().method() === 'POST',
  );
  await updateAuditCard.getByRole('button', { name: 'Khôi phục trường', exact: true }).click();
  expect((await restoreRes).status()).toBe(200);
  expect((await qa.record('WF-001')).data.reason).toBe('Lý do ban đầu');

  await page.getByRole('button', { name: /^Quy trình \(/ }).click();
  const submitApproval = page.waitForResponse(
    (res) => new URL(res.url()).pathname === `/api/records/${created.id}/submit-approval` && res.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Trình duyệt', exact: true }).click();
  expect((await submitApproval).status()).toBe(200);

  const recallBtn = page.getByRole('button', { name: 'Thu hồi', exact: true });
  await expect(recallBtn).toBeVisible();
  const recallRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === `/api/records/${created.id}/recall-approval` && res.request().method() === 'POST',
  );
  await recallBtn.click();
  expect((await recallRes).status()).toBe(200);

  const resubmitBtn = page.getByRole('button', { name: 'Nộp lại duyệt', exact: true });
  await expect(resubmitBtn).toBeVisible();
  const resubmitRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === `/api/records/${created.id}/submit-approval` && res.request().method() === 'POST',
  );
  await resubmitBtn.click();
  expect((await resubmitRes).status()).toBe(200);

  const approveBtn = page.getByRole('button', { name: 'Phê duyệt', exact: true });
  await expect(approveBtn).toBeVisible();
  const approveRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === `/api/records/${created.id}/approve` && res.request().method() === 'POST',
  );
  await approveBtn.click();
  expect((await approveRes).status()).toBe(200);

  expect((await qa.record('WF-001')).approvalStatus).toBe('APPROVED');
});

test('dark theme uses semantic surfaces without light-mode glare across controls', async ({ page, qa }) => {
  await qa.signIn(page);
  const initialSettingsLoaded = page.waitForResponse(
    (res) => new URL(res.url()).pathname === '/api/settings/me' && res.status() === 200,
  );
  await page.goto(`/apps/${qa.app.code}`);
  await expect(page.getByRole('heading', { name: qa.app.name, exact: true })).toBeVisible();
  await initialSettingsLoaded;

  await page.evaluate(() => document.body.classList.add('theme-dark'));

  const bodyBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  expect(bodyBg).toBe('rgb(15, 23, 42)');

  await page.getByRole('button', { name: 'Thêm Phiếu E2E điều kiện', exact: true }).click();
  const approvedSwitch = page.getByRole('switch', { name: 'Không / Sai', exact: true });
  await expect(approvedSwitch).toBeVisible();

  const uncheckedBg = await approvedSwitch.evaluate((el) => getComputedStyle(el).backgroundColor);
  expect(uncheckedBg).not.toBe('rgb(255, 255, 255)');
  expect(uncheckedBg).toBe('rgb(22, 32, 50)');

  await approvedSwitch.click();
  const checkedSwitch = page.getByRole('switch', { name: 'Có / Đúng', exact: true });
  const checkedBg = await checkedSwitch.evaluate((el) => getComputedStyle(el).backgroundColor);
  expect(checkedBg).not.toBe('rgb(240, 253, 244)');
  expect(checkedBg).not.toBe('rgb(255, 255, 255)');
  expect(checkedBg).toBe('rgba(34, 197, 94, 0.14)');
});

test('ecosystem exposes OpenAPI spec, MCP tools, Idempotency-Key deduplication, and metrics', async ({ adminApi, qa }) => {
  const openApiRes = await adminApi.get(`/api/apps/${encodeURIComponent(qa.app.code)}/openapi.json`);
  expect(openApiRes.status()).toBe(200);
  const spec = await openApiRes.json();
  expect(spec.openapi).toBe('3.0.3');
  expect(Object.keys(spec.paths || {}).length).toBeGreaterThan(0);

  const initRes = await adminApi.post(`/api/mcp/${encodeURIComponent(qa.app.code)}`, {
    data: { jsonrpc: '2.0', id: 1, method: 'initialize' },
  });
  expect(initRes.status()).toBe(200);
  const initJson = await initRes.json();
  expect(initJson.result?.protocolVersion).toBe('2024-11-05');

  const toolsRes = await adminApi.post(`/api/mcp/${encodeURIComponent(qa.app.code)}`, {
    data: { jsonrpc: '2.0', id: 2, method: 'tools/list' },
  });
  expect(toolsRes.status()).toBe(200);
  const toolsJson = await toolsRes.json();
  const toolNames = (toolsJson.result?.tools || []).map((t: { name: string }) => t.name);
  expect(toolNames).toEqual(expect.arrayContaining(['list_records', 'get_record', 'upsert_record']));

  const callRes = await adminApi.post(`/api/mcp/${encodeURIComponent(qa.app.code)}`, {
    data: {
      jsonrpc: '2.0',
      id: 3,
      method: 'tools/call',
      params: {
        name: 'upsert_record',
        arguments: {
          tableId: qa.table.id,
          data: { code: 'MCP-E2E-1', mode: 'normal', amount: 250, approved: true },
        },
      },
    },
  });
  expect(callRes.status()).toBe(200);
  const callJson = await callRes.json();
  expect(callJson.error).toBeUndefined();
  expect((await qa.record('MCP-E2E-1')).data.amount).toBe(250);

  const idempKey = `e2e-idemp-${randomUUID()}`;
  const reqBody = {
    appId: qa.app.id,
    moduleId: qa.module.id,
    tableId: qa.table.id,
    data: { code: 'IDEMP-001', mode: 'normal', amount: 10 },
  };
  const firstPost = await adminApi.post('/api/records', {
    headers: { 'Idempotency-Key': idempKey },
    data: reqBody,
  });
  expect(firstPost.status()).toBe(200);
  const firstJson = await firstPost.json();

  const secondPost = await adminApi.post('/api/records', {
    headers: { 'Idempotency-Key': idempKey },
    data: reqBody,
  });
  expect(secondPost.status()).toBe(200);
  const secondJson = await secondPost.json();
  expect(secondJson.data?.id).toBe(firstJson.data?.id);

  const metricsJsonRes = await adminApi.get('http://127.0.0.1:5520/metrics');
  expect(metricsJsonRes.status()).toBe(200);
  const metricsJson = await metricsJsonRes.json();
  expect(metricsJson.httpRequestsTotal ?? metricsJson.HttpRequestsTotal ?? 0).toBeGreaterThan(0);

  const metricsPromRes = await adminApi.get('http://127.0.0.1:5520/metrics?format=prometheus');
  expect(metricsPromRes.status()).toBe(200);
  const promText = await metricsPromRes.text();
  expect(promText).toContain('smartapps_http_requests_total');
});
