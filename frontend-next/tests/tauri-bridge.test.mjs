import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/lib/tauri-bridge.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;

const memoryStorage = new Map();
globalThis.window = {
  location: { host: 'localhost:3000' },
};
globalThis.localStorage = {
  getItem: (k) => (memoryStorage.has(k) ? memoryStorage.get(k) : null),
  setItem: (k, v) => { memoryStorage.set(k, String(v)); },
  removeItem: (k) => { memoryStorage.delete(k); },
};

const mod = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);

test('scopeOfflineKey isolates keys by workspace host and user without unsafe characters', () => {
  const k1 = mod.scopeOfflineKey('CRM', 'an.nguyen');
  const k2 = mod.scopeOfflineKey('CRM', 'binh.tran');
  assert.notEqual(k1, k2);
  assert.match(k1, /^[a-zA-Z0-9_-]+$/);
});

test('loadOfflineSnapshot reads scoped snapshot fallback from storage', async () => {
  const scoped = mod.scopeOfflineKey('FIN', 'ketoan1');
  globalThis.localStorage.setItem(
    `smartapps_offline_snapshot_${scoped}`,
    JSON.stringify({ records: [{ id: 'rec_1' }] })
  );
  const loaded = await mod.loadOfflineSnapshot('FIN', 'ketoan1');
  assert.equal(loaded.records[0].id, 'rec_1');
  assert.equal(await mod.loadOfflineSnapshot('FIN', 'other_user'), null);
});

test('syncAllOfflineMutations sends Idempotency-Key header and records 409 conflicts', async () => {
  memoryStorage.clear();
  const seenHeaders = [];
  await mod.queueOfflineMutation({
    id: 'off_item_ok',
    appCode: 'CRM',
    tableId: 'tbl_1',
    method: 'POST',
    endpoint: '/api/records',
    payload: { data: { name: 'A' } },
    createdAt: '2026-11-18T00:00:00Z',
  });
  await mod.queueOfflineMutation({
    id: 'off_item_conflict',
    appCode: 'CRM',
    tableId: 'tbl_1',
    method: 'POST',
    endpoint: '/api/records',
    payload: { id: 'r1', expectedVersion: 1, data: { name: 'B' } },
    createdAt: '2026-11-18T00:01:00Z',
  });

  globalThis.fetch = async (_url, init) => {
    seenHeaders.push(init?.headers?.['Idempotency-Key']);
    if (init?.headers?.['Idempotency-Key'] === 'off_item_conflict') {
      return {
        ok: false,
        status: 409,
        json: async () => ({ message: 'Xung đột v2' }),
      };
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({ success: true }),
    };
  };

  const summary = await mod.syncAllOfflineMutations();
  assert.equal(summary.synced, 1);
  assert.equal(summary.conflicts, 1);
  assert.deepEqual(seenHeaders, ['off_item_ok', 'off_item_conflict']);

  const remaining = await mod.listOfflineQueue();
  assert.equal(remaining.length, 1);
  assert.equal(remaining[0].id, 'off_item_conflict');

  const conflictMap = mod.getOfflineConflictMap();
  assert.equal(conflictMap.off_item_conflict.status, 'conflict');
  assert.equal(conflictMap.off_item_conflict.message, 'Xung đột v2');
  assert.equal(conflictMap.off_item_ok, undefined);
});

test('saveOfflineSnapshot persists wrapped timestamped snapshot in localStorage fallback and unwraps cleanly', async () => {
  memoryStorage.clear();
  const savedKey = await mod.saveOfflineSnapshot('HRM', { records: [{ id: 'emp_1' }] }, 'hr01');
  assert.match(savedKey, /^localStorage:HRM_/);
  const loaded = await mod.loadOfflineSnapshot('HRM', 'hr01');
  assert.equal(loaded.records[0].id, 'emp_1');
  const info = mod.getOfflineSnapshotInfo('HRM', 'hr01');
  assert.equal(typeof info.savedAt, 'string');
  assert.ok(info.savedAt.length > 10);
});

test('syncSingleOfflineMutation forceOverwrite strips expectedVersion and clears 409 conflict', async () => {
  memoryStorage.clear();
  await mod.queueOfflineMutation({
    id: 'off_conflict_fix',
    appCode: 'ERP',
    tableId: 'tbl_erp',
    method: 'POST',
    endpoint: '/api/records',
    payload: { id: 'rec_erp', expectedVersion: 2, data: { total: 900 } },
    createdAt: '2026-11-18T00:05:00Z',
  });

  let receivedBody = null;
  let receivedKey = null;
  globalThis.fetch = async (_url, init) => {
    receivedBody = JSON.parse(init.body);
    receivedKey = init.headers['Idempotency-Key'];
    if ('expectedVersion' in receivedBody) {
      return { ok: false, status: 409, json: async () => ({ message: 'Conflict v3' }) };
    }
    return { ok: true, status: 200, json: async () => ({ success: true }) };
  };

  const firstAttempt = await mod.syncSingleOfflineMutation('off_conflict_fix');
  assert.equal(firstAttempt.status, 'conflict');
  assert.equal(receivedKey, 'off_conflict_fix');

  const forcedAttempt = await mod.syncSingleOfflineMutation('off_conflict_fix', { forceOverwrite: true });
  assert.equal(forcedAttempt.status, 'synced');
  assert.equal('expectedVersion' in receivedBody, false);
  assert.equal(receivedBody.data.total, 900);
  assert.match(receivedKey, /^off_conflict_fix-force-/);
  assert.equal((await mod.listOfflineQueue()).length, 0);
  assert.equal(mod.getOfflineConflictMap().off_conflict_fix, undefined);
});

test('requestLocalTokenSign supports Tauri PKCS11 invoke and graceful offline signer fallback', async () => {
  globalThis.window.__TAURI_INTERNALS__ = {
    invoke: async (cmd, args) => {
      assert.equal(cmd, 'sign_document_pkcs11');
      assert.equal(args.request.documentHash, 'abc123');
      return {
        success: true,
        provider: 'vnpt-smartca',
        signTransactionId: 'USB-999',
        signatureBase64: 'U0lH',
        certificateSubject: 'CONG TY SMARTAPPS',
        certificateSerial: '5401-ABCD',
        message: 'OK',
      };
    },
  };
  const signed = await mod.requestLocalTokenSign({
    documentId: 'DOC-1',
    documentHash: 'abc123',
    providerCode: 'vnpt-smartca',
  });
  assert.equal(signed.success, true);
  assert.equal(signed.signTransactionId, 'USB-999');

  delete globalThis.window.__TAURI_INTERNALS__;
  globalThis.fetch = async () => {
    throw new Error('ECONNREFUSED');
  };
  const unreachable = await mod.requestLocalTokenSign({
    documentId: 'DOC-1',
    documentHash: 'abc123',
  });
  assert.equal(unreachable.success, false);
  assert.match(unreachable.message, /USB Token Agent/);
});
