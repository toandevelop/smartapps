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
  const initialMeLoaded = page.waitForResponse(
    (res) => new URL(res.url()).pathname === '/api/settings/me' && res.status() === 200,
  );
  const initialSysLoaded = page.waitForResponse(
    (res) => new URL(res.url()).pathname === '/api/settings/system' && res.status() === 200,
  );
  await page.goto(`/apps/${qa.app.code}`);
  await expect(page.getByRole('heading', { name: qa.app.name, exact: true })).toBeVisible();
  await Promise.all([initialMeLoaded, initialSysLoaded]);

  await page.evaluate(() => document.body.classList.add('theme-dark'));

  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(15, 23, 42)');

  await page.getByRole('button', { name: 'Thêm Phiếu E2E điều kiện', exact: true }).click();
  const approvedSwitch = page.getByRole('switch', { name: 'Không / Sai', exact: true });
  await expect(approvedSwitch).toBeVisible();

  await expect(approvedSwitch).not.toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await expect(approvedSwitch).toHaveCSS('background-color', 'rgb(22, 32, 50)');

  await approvedSwitch.click();
  const checkedSwitch = page.getByRole('switch', { name: 'Có / Đúng', exact: true });
  await expect(checkedSwitch).not.toHaveCSS('background-color', 'rgb(240, 253, 244)');
  await expect(checkedSwitch).not.toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await expect(checkedSwitch).toHaveCSS('background-color', 'rgba(34, 197, 94, 0.14)');
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

test('apps run on v2.0.0 with rich templates and Studio upgrades legacy v1.0 apps cleanly', async ({ page, adminApi, qa }) => {
  const templatesRes = await adminApi.get('/api/apps/templates');
  expect(templatesRes.status()).toBe(200);
  const templatesJson = await templatesRes.json();
  expect(templatesJson.success).toBe(true);
  const templates = templatesJson.data as Array<{
    code: string;
    version: string;
    formRuleCount: number;
    workflowCount: number;
    printTemplateCount: number;
    savedViewCount: number;
  }>;
  expect(templates.length).toBeGreaterThanOrEqual(15);
  for (const tpl of templates) {
    expect(tpl.version).toBe('2.0.0');
    expect(tpl.formRuleCount).toBeGreaterThanOrEqual(1);
    expect(tpl.workflowCount).toBeGreaterThanOrEqual(1);
    expect(tpl.printTemplateCount).toBeGreaterThanOrEqual(1);
    expect(tpl.savedViewCount).toBeGreaterThanOrEqual(1);
  }

  await qa.addColumn({
    columnCode: 'chi_tiet_v1',
    columnName: 'Chi tiết v1',
    dataType: 'SUB_TABLE',
    defaultValue: 'ten_hang,so_luong,don_gia,thanh_tien',
  });

  await qa.signIn(page);
  await page.goto('/');
  await expect(page.getByText('v2.0.0').first()).toBeVisible();

  await qa.openStudio(page);
  await page.getByRole('button', { name: 'Nâng cấp Apps 2.0', exact: true }).click();

  const dialog = page.locator('.modal-dialog').filter({ hasText: 'Nâng cấp Ứng dụng lên Phiên bản 2.0.0' });
  await expect(dialog).toBeVisible();

  const targetRow = dialog
    .locator('div')
    .filter({ hasText: qa.app.code })
    .filter({ hasText: 'v1.0.0 → 2.0.0' })
    .filter({ has: page.getByRole('button', { name: 'Nâng cấp lên 2.0.0', exact: true }) })
    .last();
  await expect(targetRow).toBeVisible();

  const upgradeRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === `/api/apps/${encodeURIComponent(qa.app.id)}/upgrade-v2` && res.request().method() === 'POST',
  );
  await targetRow.getByRole('button', { name: 'Nâng cấp lên 2.0.0', exact: true }).click();
  expect((await upgradeRes).status()).toBe(200);

  await expect(dialog.getByText('Kết quả nâng cấp vừa thực hiện:')).toBeVisible();
  await expect(dialog.getByText(`${qa.app.code} (v1.0.0 → v2.0.0)`)).toBeVisible();

  await dialog.getByRole('button', { name: 'Đóng', exact: true }).first().click();
  await page.goto(`/apps/${qa.app.code}?previewDraft=true`);
  await expect(page.getByText('Chế độ xem thử Bản Nháp (Draft Preview):')).toBeVisible();
  await page.getByRole('button', { name: 'Về Bản Phát hành', exact: true }).click();
  await expect(page.getByText('Chế độ xem thử Bản Nháp (Draft Preview):')).toHaveCount(0);
  await expect(page.getByText('v2.0.0', { exact: true }).first()).toBeVisible();
});

test('online app store browses, downloads .smartapp.json, installs apps, and syncs remote registry', async ({ page, adminApi, qa }) => {
  await qa.signIn(page);
  await page.goto('/store');
  await expect(page.getByRole('heading', { name: 'Kho Apps Online (SmartApps Marketplace)', exact: true })).toBeVisible();

  const searchInput = page.getByPlaceholder('Tìm kiếm App trên Kho Online (CRM, TMĐT, BĐS, HIS, MES...)');
  await expect(searchInput).toBeVisible();
  await searchInput.fill('MANUFACTURE_MES');

  const mesCard = page.locator('.card').filter({ hasText: 'MANUFACTURE_MES' }).first();
  await expect(mesCard).toBeVisible();

  // 1. Download .smartapp.json package to disk and verify contents
  const downloadPromise = page.waitForEvent('download');
  await mesCard.getByRole('button', { name: 'Tải về (.json)', exact: true }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toContain('smartapp-manufacture_mes');
  const dlPath = await download.path();
  expect(dlPath).toBeTruthy();

  // 2. Open detail modal, customize code, install 1-click and verify runtime works
  await mesCard.getByRole('button', { name: 'Chi tiết', exact: true }).click();
  const detailDialog = page.locator('.modal-dialog').filter({ hasText: 'Mã gói: MANUFACTURE_MES' });
  await expect(detailDialog).toBeVisible();
  const storeCode = `MES_E2E_${Date.now().toString().slice(-4)}`;
  await detailDialog.getByPlaceholder('Mã App...').fill(storeCode);

  const installRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === '/api/store/apps/MANUFACTURE_MES/install' && res.request().method() === 'POST',
  );
  await detailDialog.getByRole('button', { name: 'Tải & Cài đặt ngay', exact: true }).click();
  expect((await installRes).status()).toBe(200);
  await expect(page.getByRole('link', { name: 'Mở App ngay', exact: true })).toBeVisible();

  // 3. Re-install from downloaded .smartapp.json file via file input
  const fileInstallRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === '/api/store/install-package' && res.request().method() === 'POST',
  );
  await page.locator('input[type="file"][accept*=".smartapp.json"]').setInputFiles(dlPath!);
  expect((await fileInstallRes).status()).toBe(200);

  // 4. Connect live remote Online Store registry pointing to manifest.json and sync
  await page.getByRole('button', { name: /Nguồn Kho Online/ }).click();
  const regDialog = page.locator('.modal-dialog').filter({ hasText: 'Nguồn Kho Apps Online Từ Xa' });
  await expect(regDialog).toBeVisible();
  await regDialog.getByPlaceholder('Tên kho (VD: Partner Hub)').fill('Local Mirror Hub');
  await regDialog.getByPlaceholder(/URL Manifest/).fill('http://127.0.0.1:5520/api/store/manifest.json');
  const regCreateRes = page.waitForResponse(
    (res) => new URL(res.url()).pathname === '/api/store/registries' && res.request().method() === 'POST',
  );
  await regDialog.getByRole('button', { name: 'Kết nối & Đồng bộ', exact: true }).click();
  expect((await regCreateRes).status()).toBe(200);
  await expect(regDialog.getByText('Local Mirror Hub')).toBeVisible();
  await expect(regDialog.getByText('Đang bật')).toBeVisible();

  // Toggle registry off & back on cleanly
  const toggleOffRes = page.waitForResponse(
    (res) => res.url().includes('/api/store/registries/') && res.url().endsWith('/toggle') && res.request().method() === 'PATCH',
  );
  await regDialog.getByRole('button', { name: 'Tạm dừng', exact: true }).first().click();
  expect((await toggleOffRes).status()).toBe(200);
  await expect(regDialog.getByText('Tạm dừng')).toBeVisible();

  const toggleOnRes = page.waitForResponse(
    (res) => res.url().includes('/api/store/registries/') && res.url().endsWith('/toggle') && res.request().method() === 'PATCH',
  );
  await regDialog.getByRole('button', { name: 'Kích hoạt', exact: true }).first().click();
  expect((await toggleOnRes).status()).toBe(200);

  await regDialog.getByRole('button', { name: 'Đóng', exact: true }).click();

  await searchInput.fill('');
  await page.getByRole('button', { name: /Kho Từ xa \(/ }).click();
  await expect(page.locator('.card').filter({ hasText: 'Local Mirror Hub' }).first()).toBeVisible();

  // 4b. Verify Store History modal & Workspace Export modal work
  const histRes = page.waitForResponse((res) => new URL(res.url()).pathname === '/api/store/history');
  await page.getByRole('button', { name: 'Lịch sử Kho', exact: true }).click();
  expect((await histRes).status()).toBe(200);
  const histDialog = page.locator('.modal-dialog').filter({ hasText: 'Lịch sử Tải & Cài đặt Kho Apps Online' });
  await expect(histDialog.getByText('MANUFACTURE_MES').first()).toBeVisible();
  await histDialog.getByRole('button', { name: 'Đóng', exact: true }).click();

  await page.getByRole('button', { name: 'Đóng gói Xuất App', exact: true }).click();
  const exportDialog = page.locator('.modal-dialog').filter({ hasText: 'Đóng gói Ứng dụng thành Gói Store' });
  await expect(exportDialog.getByText(storeCode)).toBeVisible();
  await exportDialog.getByRole('button', { name: 'Đóng', exact: true }).click();

  // 5. Open newly installed v2.0 Store App in Runtime
  await page.goto(`/apps/${storeCode}`);
  await expect(page.getByText('v2.0.0', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('LSX-2026-0001')).toBeVisible();

  // Cleanup any created store test apps & registries via adminApi
  const appsRes = await adminApi.get('/api/apps');
  const allApps = (await appsRes.json()).data as Array<{ id: string; code: string }>;
  for (const a of allApps) {
    if (a.code.startsWith('MES_E2E_') || a.code.startsWith('MANUFACTURE_MES')) {
      await adminApi.delete(`/api/apps/${encodeURIComponent(a.id)}`);
    }
  }
});

test('UI/UX 3.0 Phase 1: Creation Hub, Sidebar Pinning, Admin Audit Tab, Density & Runtime Split View + Floating Bulk Bar', async ({ page, qa }) => {
  await qa.signIn(page);
  await qa.save({ code: 'UX3-001', mode: 'normal', amount: 120, reason: 'Kiểm thử Split View & Bulk' });

  // 1. Portal Creation Hub
  await page.goto('/');
  await page.getByRole('button', { name: /Thêm \/ Tạo App/ }).click();
  const hubDialog = page.getByRole('dialog', { name: 'Trung tâm Tạo & Cài đặt Ứng dụng' });
  await expect(hubDialog).toBeVisible();
  await expect(hubDialog.getByText('Kho Apps Online (20 gói v2.0)')).toBeVisible();
  await expect(hubDialog.getByText('Tạo App bằng AI Copilot')).toBeVisible();
  await expect(hubDialog.getByText('Sinh App từ File Excel (.xlsx)')).toBeVisible();
  await expect(hubDialog.getByText('Tạo App Trống Từ Đầu')).toBeVisible();
  await hubDialog.getByRole('button', { name: 'Đóng', exact: true }).click();

  // 2. Sidebar Pinning persists across reload
  const pinBtn = page.locator('.sidebar-pin-btn').first();
  const savePinRes = page.waitForResponse((res) => new URL(res.url()).pathname === '/api/settings/me' && res.request().method() === 'PUT');
  await pinBtn.click({ force: true });
  expect((await savePinRes).status()).toBe(200);
  await page.reload();
  await expect(page.getByText(/Đã ghim \(1\)/)).toBeVisible();
  // Unpin to restore clean state
  const unpinRes = page.waitForResponse((res) => new URL(res.url()).pathname === '/api/settings/me' && res.request().method() === 'PUT');
  await page.locator('.sidebar-pin-btn.pinned').first().click({ force: true });
  expect((await unpinRes).status()).toBe(200);

  // 3. Admin Global Audit Trail deep link (/admin?tab=audit)
  await page.goto('/admin?tab=audit');
  await expect(page.getByText('Nhật ký Kiểm toán Toàn hệ thống (Global Audit Trail)')).toBeVisible();

  // 4. 3-level Display Density in Settings
  await page.goto('/settings');
  await page.getByRole('button', { name: /Giao diện/ }).click();
  await page.getByRole('button', { name: /Thoải mái \(Spacious\)/ }).click();
  const saveDensityRes = page.waitForResponse((res) => new URL(res.url()).pathname === '/api/settings/me' && res.request().method() === 'PUT');
  await page.getByRole('button', { name: /Áp dụng & Lưu giao diện/ }).click();
  expect((await saveDensityRes).status()).toBe(200);
  await expect.poll(() => page.evaluate(() => document.body.classList.contains('density-spacious'))).toBe(true);
  // Restore default density
  await page.getByRole('button', { name: /Tiêu chuẩn/ }).click();
  const restoreDensityRes = page.waitForResponse((res) => new URL(res.url()).pathname === '/api/settings/me' && res.request().method() === 'PUT');
  await page.getByRole('button', { name: /Áp dụng & Lưu giao diện/ }).click();
  expect((await restoreDensityRes).status()).toBe(200);

  // 5. Runtime Split View & Floating Bulk Action Bar
  await qa.openRuntime(page);
  await expect(page.getByRole('button', { name: /Chia đôi/ })).toBeVisible();
  await page.getByRole('button', { name: /Chia đôi/ }).click();
  await expect(page.locator('.runtime-split-grid')).toBeVisible();
  await expect(page.locator('.runtime-split-detail')).toBeVisible();
  await expect(page.locator('.runtime-split-detail').getByText('UX3-001').first()).toBeVisible();

  // Back to Table View & select row to trigger Floating Bulk Action Bar
  await page.getByRole('button', { name: /Bảng động/ }).click();
  const rowCheckbox = page.getByRole('row').filter({ hasText: 'UX3-001' }).locator('input[type="checkbox"]').first();
  await expect(rowCheckbox).toBeVisible();
  await rowCheckbox.check();
  const bulkBar = page.locator('.floating-bulk-bar');
  await expect(bulkBar).toBeVisible();
  await expect(bulkBar.getByText(/Đã chọn 1 bản ghi/)).toBeVisible();
  await bulkBar.getByRole('button', { name: 'Bỏ chọn' }).click();
  await expect(bulkBar).toBeHidden();
});

test('Phase 2: Form Layout Wizard, Vietnamese accent-insensitive search, Gallery & Timeline Views, and QR Scanner input', async ({ page, adminApi, qa }) => {
  await qa.signIn(page);

  // Add a DATE column so Timeline/Gantt View activates, then configure 2-step Wizard FormLayout
  await post(adminApi, '/api/columns', {
    tableId: qa.table.id,
    columnCode: 'due_date',
    columnName: 'Hạn hoàn thành',
    dataType: 'DATE',
    orderIndex: 7,
  });
  await post(adminApi, '/api/tables', {
    ...qa.table,
    formLayout: {
      mode: 'wizard',
      sections: [
        {
          id: 'step_1',
          title: 'Thông tin cơ bản',
          columns: 2,
          calloutText: 'Nhập mã và chế độ xử lý trước khi qua bước 2',
          fields: [
            { columnCode: 'code', colSpan: 1 },
            { columnCode: 'mode', colSpan: 1 },
          ],
        },
        {
          id: 'step_2',
          title: 'Số tiền & Diễn giải',
          columns: 2,
          fields: [
            { columnCode: 'amount', colSpan: 1 },
            { columnCode: 'approved', colSpan: 1 },
            { columnCode: 'reason', colSpan: 2 },
            { columnCode: 'due_date', colSpan: 1 },
          ],
        },
      ],
    },
  });
  await qa.save({
    code: 'P2-VN-01',
    mode: 'normal',
    amount: 250,
    reason: 'Hồ sơ Nguyễn Văn Đức chuyển đổi số',
    due_date: '2026-11-25',
  });

  // 1. Runtime opens Wizard Form Layout & QR Scanner injects code into field
  await qa.openRuntime(page);
  await page.getByRole('button', { name: 'Thêm Phiếu E2E điều kiện', exact: true }).click();
  await expect(page.getByText('Bước 1/2: Thông tin cơ bản')).toBeVisible();
  await expect(page.getByText('Nhập mã và chế độ xử lý trước khi qua bước 2')).toBeVisible();

  await page.getByRole('button', { name: /Quét mã QR vào/ }).first().click();
  const qrDialog = page.getByRole('dialog', { name: /Quét mã vào:/ });
  await expect(qrDialog).toBeVisible();
  await qrDialog.getByPlaceholder('Hoặc nhập / dán mã quét thủ công...').fill('QR-SCANNED-99');
  await qrDialog.getByRole('button', { name: 'Áp dụng', exact: true }).click();
  await expect(qrDialog).toBeHidden();
  await expect(input(page, 'mã phiếu')).toHaveValue('QR-SCANNED-99');

  await page.getByRole('button', { name: /Bước tiếp theo/ }).click();
  await expect(page.getByText('Bước 2/2: Số tiền & Diễn giải')).toBeVisible();
  await page.getByRole('button', { name: 'Hủy', exact: true }).click();
  await page.getByRole('button', { name: 'Bỏ thay đổi', exact: true }).click();

  // 2. Vietnamese accent-insensitive search on Runtime table
  const searchBox = page.getByPlaceholder('Tìm nhanh trên bảng...', { exact: true });
  await searchBox.fill('nguyen van duc');
  await expect(page.getByRole('row').filter({ hasText: 'P2-VN-01' })).toBeVisible();
  await searchBox.fill('');

  // 3. Switch to Gallery View & Timeline/Gantt View
  await page.getByRole('button', { name: /Thư viện/ }).click();
  await expect(page.locator('.runtime-gallery-grid')).toBeVisible();
  await expect(page.locator('.runtime-gallery-card').filter({ hasText: 'P2-VN-01' })).toBeVisible();

  await page.getByRole('button', { name: 'Tiến độ', exact: true }).click();
  await expect(page.getByText(/Tiến độ Timeline/)).toBeVisible();
  await expect(page.getByRole('button', { name: /P2-VN-01/ }).first()).toBeVisible();
});

test('Phase 3: Global AI Copilot Drawer, Solution Suites 1-Click & DMS_VAN_BAN, and Admin HA Cluster Health', async ({ page, qa }) => {
  await qa.signIn(page);
  await qa.save({
    code: 'P3-COPILOT-01',
    mode: 'normal',
    amount: 500,
    reason: 'Nghị quyết chuyển đổi số tập đoàn Hoàng Gia',
  });

  // 1. Open Global AI Copilot Drawer from Navbar and search accent-insensitively
  await page.goto('/');
  await page.getByRole('button', { name: 'Mở Trợ lý AI Copilot', exact: true }).click();
  const copilotDialog = page.getByRole('dialog', { name: 'Trợ lý AI Doanh nghiệp SmartApps Copilot', exact: true });
  await expect(copilotDialog).toBeVisible();

  await copilotDialog.getByRole('textbox', { name: 'Nhập câu hỏi cho Trợ lý AI Copilot', exact: true }).fill('Tìm hồ sơ hoang gia');
  await copilotDialog.getByRole('button', { name: 'Gửi câu hỏi tới AI Copilot', exact: true }).click();
  await expect(copilotDialog.getByText(/search_records/)).toBeVisible();
  await expect(copilotDialog.getByRole('button', { name: /P3-COPILOT-01/ })).toBeVisible();
  await copilotDialog.getByRole('button', { name: 'Đóng Trợ lý AI', exact: true }).click();
  await expect(copilotDialog).toBeHidden();

  // 2. Open Online App Store -> verify Solution Suites 1-Click & DMS_VAN_BAN package
  await page.goto('/store');
  await expect(page.getByText('Bộ Giải pháp Doanh nghiệp 1-Click (Solution Suites)')).toBeVisible();
  await expect(page.getByText('Bộ Giải pháp Vận hành & Hành chính Số (SME Core)')).toBeVisible();
  await expect(page.getByText('Quản lý Văn bản Đi/Đến & Trình Ký số (NĐ30)').first()).toBeVisible();

  // 3. Open Admin Storage & HA Cluster Health -> verify Multi-Instance Cluster Health card
  await page.goto('/admin?tab=storage');
  await expect(page.getByText('Trạng thái Cụm HA (Multi-Instance Cluster Health)')).toBeVisible();
  await expect(page.getByText(/API Node hoạt động/)).toBeVisible();
});

