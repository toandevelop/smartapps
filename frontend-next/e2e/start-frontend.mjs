import { createRequire } from 'node:module';
import { run, requireTestEnvironment } from './process.mjs';

requireTestEnvironment();
const next = createRequire(import.meta.url).resolve('next/dist/bin/next');
const env = { ...process.env, NEXT_PUBLIC_API_ORIGIN: 'http://127.0.0.1:5520', NEXT_TELEMETRY_DISABLED: '1' };
const build = await run(process.execPath, [next, 'build'], { env });
if (build !== 0) process.exit(build);
process.exitCode = await run(process.execPath, [next, 'start', '-p', '5510', '-H', '127.0.0.1'], { env });
