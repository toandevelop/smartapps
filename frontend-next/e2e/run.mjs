import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import pg from 'pg';

const source = process.env.SMARTAPPS_E2E_POSTGRES;
if (!source) throw new Error('Set SMARTAPPS_E2E_POSTGRES to a local PostgreSQL admin URL, e.g. postgresql://postgres:postgres@127.0.0.1:5432/postgres.');
const url = new URL(source);
if (!['postgres:', 'postgresql:'].includes(url.protocol) || !['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) || url.pathname !== '/postgres') {
  throw new Error('E2E requires a local PostgreSQL URL pointing to the postgres maintenance database.');
}
const database = `smartapps_e2e_${randomUUID().replaceAll('-', '')}`;
const admin = new pg.Client({ connectionString: source, connectionTimeoutMillis: 10_000 });
const quote = value => `"${value.replaceAll('"', '""')}"`;
const connection = [
  ['Host', url.hostname.replace(/^\[|\]$/g, '')], ['Port', url.port || '5432'], ['Database', database],
  ['Username', decodeURIComponent(url.username)], ['Password', decodeURIComponent(url.password)],
].map(([key, value]) => `${key}=${quote(value)}`).join(';');
let created = false;
try {
  await admin.connect();
  await admin.query(`CREATE DATABASE "${database}"`);
  created = true;
  console.log(`[E2E] Created isolated database ${database}`);
  const cli = createRequire(import.meta.url).resolve('@playwright/test/cli');
  const child = spawn(process.execPath, [cli, 'test', ...process.argv.slice(2)], {
    cwd: fileURLToPath(new URL('../', import.meta.url)),
    stdio: 'inherit',
    windowsHide: true,
    env: { ...process.env, SMARTAPPS_E2E: '1', ConnectionStrings__Postgres: connection, Auth__Secret: randomUUID() },
  });
  const interrupt = () => child.kill('SIGINT');
  const terminate = () => child.kill('SIGTERM');
  process.once('SIGINT', interrupt);
  process.once('SIGTERM', terminate);
  try {
    process.exitCode = await new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('exit', code => resolve(code ?? 1));
    });
  } finally {
    process.removeListener('SIGINT', interrupt);
    process.removeListener('SIGTERM', terminate);
  }
} finally {
  try {
    if (created) {
      // Only the name generated and successfully created by this run is dropped.
      await admin.query(`DROP DATABASE "${database}" WITH (FORCE)`);
      console.log(`[E2E] Dropped isolated database ${database}`);
    }
  } finally {
    await admin.end();
  }
}
