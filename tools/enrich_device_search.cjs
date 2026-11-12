// Reuse the Toolbox's installed pinyin-pro, without adding a runtime dependency.
const fs = require('fs');
const path = require('path');
const { createRequire } = require('module');
const [toolboxDirectory, indexPath] = process.argv.slice(2);
if (!toolboxDirectory || !indexPath) {
  throw new Error('Usage: node tools/enrich_device_search.cjs <Toolbox directory> <device-index.json>');
}
const requireToolbox = createRequire(path.resolve(toolboxDirectory, 'package.json'));
const { pinyin } = requireToolbox('pinyin-pro');
const index = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
for (const entry of index.entries) {
  entry.pinyin = ['normal', 'first'].map(pattern =>
    pinyin(entry.name, { pattern, toneType: 'none' }).replace(/[^a-z0-9]/gi, ''));
}
fs.writeFileSync(indexPath, JSON.stringify(index) + '\n');
