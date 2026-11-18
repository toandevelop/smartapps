import { fileURLToPath } from 'node:url';
import { run, requireTestEnvironment } from './process.mjs';

requireTestEnvironment();
const backend = fileURLToPath(new URL('../../backend-dotnet/', import.meta.url));
const build = await run('dotnet', ['build', 'SmartApps.Api.csproj', '--configuration', 'E2E', '-p:UseAppHost=false'], { cwd: backend });
if (build !== 0) process.exit(build);
process.exitCode = await run('dotnet', ['bin/E2E/net9.0/SmartApps.Api.dll', '--urls', 'http://127.0.0.1:5520'], {
  cwd: backend,
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_ENVIRONMENT: 'Development' },
});
