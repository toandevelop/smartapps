import { spawn } from 'node:child_process';

export function run(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { stdio: 'inherit', windowsHide: true, ...options });
    child.once('error', reject);
    child.once('exit', (code, signal) => resolve(code ?? (signal ? 1 : 0)));
  });
}

export function requireTestEnvironment() {
  if (process.env.SMARTAPPS_E2E !== '1' || !process.env.ConnectionStrings__Postgres?.includes('Database="smartapps_e2e_')) {
    throw new Error('Start the isolated E2E environment with npm run test:e2e.');
  }
}
