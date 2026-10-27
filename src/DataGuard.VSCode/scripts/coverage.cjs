// Real V8 coverage with source maps, including production modules never imported by a test.
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
function walk(dir) {
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
        const file = path.join(dir, entry.name);
        return entry.isDirectory() ? walk(file) : [file.replaceAll('\\', '/')];
    });
}
const tests = walk('out').filter(file => file.endsWith('.test.js')).sort();
const production = walk('src').filter(file => file.endsWith('.ts') && !file.endsWith('.test.ts') && !file.endsWith('.d.ts')).sort();
fs.mkdirSync('coverage', { recursive: true });
const result = spawnSync(process.execPath, [
    'node_modules/c8/bin/c8.js', '--all', '--src', 'out', '--include', 'out/**/*.js',
    '--exclude', '**/*.test.js', '--reporter', 'lcovonly', '--reporter', 'json-summary', '--reporter', 'text',
    process.execPath, '--test', '--test-reporter=spec', '--test-reporter-destination=stdout',
    '--test-reporter=junit', '--test-reporter-destination=coverage/junit.xml', ...tests,
], { stdio: 'inherit' });
if (result.status !== 0) process.exit(result.status || 1);
const report = fs.readFileSync('coverage/lcov.info', 'utf8');
const sources = report.split('\n').filter(line => line.startsWith('SF:')).map(line => line.slice(3).replaceAll('\\', '/')).sort();
if (JSON.stringify(sources) !== JSON.stringify(production)) {
    throw new Error(`Coverage source inventory differs from production files: ${JSON.stringify({ sources, production })}`);
}
fs.writeFileSync('coverage/lcov.info', report.replace(/^SF:/gm, 'SF:src/DataGuard.VSCode/'));
fs.writeFileSync('coverage/manifest.json', JSON.stringify({ scope: 'VS Code production TypeScript; all modules, excluding tests and declarations', sources: production, reports: ['src/DataGuard.VSCode/coverage/lcov.info'] }, null, 2) + '\n');
