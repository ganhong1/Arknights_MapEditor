import fs from 'node:fs';
import { createRequire } from 'node:module';
const [toolbox, gameData, output] = process.argv.slice(2);
if (!toolbox || !gameData || !output) {
  throw new Error('Usage: node build-terrain-search-metadata.mjs <Toolbox directory> <gamedata directory> <catalog directory>');
}
const requireToolbox = createRequire(new URL('file:///' + toolbox.replaceAll('\\', '/') + '/package.json'));
const { pinyin } = requireToolbox('pinyin-pro');
const Fuse = requireToolbox('fuse.js');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const catalog = read(output + '/catalog.json');
const terrainSamples = read(output + '/terrain-samples.json');
const stageTable = read(gameData + '/excel/stage_table.json');
const levelPaths = new Set(terrainSamples.levels);
const readings = name => ['normal', 'first'].map(pattern =>
  pinyin(name, { pattern, toneType: 'none' }).replace(/[^a-z0-9]/gi, ''));
const terrain = Object.fromEntries(catalog.entries.filter(item => item.category === 'terrain' && item.name)
  .map(item => [item.id, readings(item.name)]));
const stageNames = new Set(Object.values(stageTable.stages).map(stage => stage.name?.trim()).filter(Boolean));
const groupedSources = output + '/options-stage-sources.json';
if (fs.existsSync(groupedSources)) {
  for (const stage of read(groupedSources).stages) {
    if (stage[2]?.trim()) stageNames.add(stage[2].trim());
  }
}
const stages = {};
for (const stage of Object.values(stageTable.stages)) {
  const path = stage.levelId?.trim().toLowerCase() + '.json';
  if (!levelPaths.has(path)) continue;
  const name = stage.name?.trim() || '';
  const code = stage.code?.trim() || '';
  const label = [code, name].filter(Boolean).join(' ');
  const row = stages[path] ??= { names: [], ids: [], pinyin: [], initials: [] };
  if (label && !row.names.includes(label)) row.names.push(label);
  for (const id of [stage.stageId, code]) {
    if (id && !row.ids.includes(id)) row.ids.push(id);
  }
  if (name) {
    const [full, initials] = readings(name);
    if (!row.pinyin.includes(full)) row.pinyin.push(full);
    if (!row.initials.includes(initials)) row.initials.push(initials);
  }
}
fs.writeFileSync(output + '/terrain-search-metadata.json', JSON.stringify({
  version: 1, revision: terrainSamples.revision, terrain, stages,
  stageNames: Object.fromEntries([...stageNames].sort().map(name => [name, readings(name)])),
}) + '\n');
console.log(Object.keys(terrain).length, Object.keys(stages).length);


const keys = ['name', 'targetFile', 'namePinyin', 'nameInitials', 'category', 'description']
  .map((name, index) => ({ name, weight: [0.55, 0.22, 0.14, 0.06, 0.02, 0.01][index] }));
const paths = terrainSamples.levels;
const sourceDocuments = paths.map(path => {
  const stage = stages[path];
  return {
    name: stage?.names,
    targetFile: [path.split('/').at(-1).replace(/\.json$/, ''), ...(stage?.ids || [])],
    namePinyin: stage?.pinyin,
    nameInitials: stage?.initials,
    category: path
  };
});
const sourceIndex = Fuse.createIndex(keys, sourceDocuments).toJSON();
fs.writeFileSync(output + '/terrain-source-index.json', JSON.stringify({
  version: 1, revision: terrainSamples.revision, paths, index: sourceIndex
}) + '\n');
