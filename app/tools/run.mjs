// Runs a TypeScript tool on the app's own modules through Vite's module runner — no build step and
// nothing to install; JSON imports and extensionless paths resolve as in the app.
//
//   node tools/run.mjs tools/measure/comp.ts <argumente>      (npm run measure -- <argumente>)
//
// The tool exports `main(args): Promise<number>`; its result is the exit code.

import path from 'node:path';
import { runnerImport } from 'vite';

const [script, ...args] = process.argv.slice(2);
if (!script) {
  console.error('Aufruf: node tools/run.mjs <werkzeug.ts> [argumente]');
  process.exit(2);
}

const { module } = await runnerImport(path.resolve(script), { configFile: false, logLevel: 'error' });
if (typeof module.main !== 'function') {
  console.error(`${script}: exportiert keine main()`);
  process.exit(2);
}
process.exitCode = await module.main(args);
