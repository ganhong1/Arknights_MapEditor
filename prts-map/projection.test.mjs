import {test} from 'node:test';
import assert from 'node:assert/strict';
import {projectLevel} from './projection.mjs';
import M from '../map-model.js';

test('projection preserves LevelData and bottom-origin coordinates', () => {
  const level=M.create();
  level.mapData.tiles[0]={...M.tile('tile_custom'),blackboard:[{key:'x',value:3}]};
  const before=JSON.stringify(level);
  const view=projectLevel(level);
  assert.equal(view.tiles.find(t=>t.row===0&&t.col===0).tileKey,'tile_custom');
  assert.equal(view.unknownTiles.includes('tile_custom'),true);
  view.tiles[0].blackboard?.push({key:'changed'});
  assert.equal(JSON.stringify(level),before);
});
test('predefines retain hidden/hard identity; cards are not map placements', () => {
  const level=M.create();
  const item={alias:'box',inst:{characterKey:'trap_box'},position:{row:2,col:3},hidden:true};
  level.predefines.tokenInsts=[item];
  level.hardPredefines.tokenInsts=[item];
  level.predefines.tokenCards=[{inst:{characterKey:'card'}}];
  level.predefines.characterInsts=[{position:{row:999,col:0}}];
  const view=projectLevel(level);
  assert.equal(view.objects.length,2);
  assert.notEqual(view.objects[0].id,view.objects[1].id);
  assert.equal(view.objects[0].hidden,true);
  assert.equal(view.cards,1);
  assert.equal(view.unplaced.length,1);
});
test('invalid map references fail rather than showing a fabricated board', () => {
  const level=M.create();level.mapData.map[0][0]=999;
  assert.throws(()=>projectLevel(level),/索引/);
});
