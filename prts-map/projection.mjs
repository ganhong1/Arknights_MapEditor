import {tiles as definitions} from './definitions.mjs';

// Display-only copy. LevelData rows are top-first; positions are bottom-origin.
export function projectLevel(level) {
  const map=level?.mapData?.map, definitionsIn=level?.mapData?.tiles;
  const rows=map?.length, cols=map?.[0]?.length;
  if(!rows||!cols||!Array.isArray(definitionsIn))throw Error('地图缺少 mapData');
  const copiedDefinitions=new Map();
  const tiles=[], unknownTiles=new Set(), objects=[],unplaced=[];
  for(let row=0;row<rows;row++) {
    const source=map[rows-1-row];
    if(!Array.isArray(source)||source.length!==cols)throw Error('地图必须为矩形');
    for(let col=0;col<cols;col++) {
      const index=source[col];
      if(!Number.isInteger(index)||!definitionsIn[index])throw Error('无效地块索引');
      if(!copiedDefinitions.has(index))copiedDefinitions.set(index,JSON.parse(JSON.stringify(definitionsIn[index])));
      const tile=copiedDefinitions.get(index);
      tiles.push({...tile,row,col});
      if(!definitions[tile.tileKey])unknownTiles.add(tile.tileKey||'(缺少 tileKey)');
    }
  }
  let cards=0;
  for(const scope of ['predefines','hardPredefines']) {
    const group=level[scope];if(!group)continue;
    for(const kind of ['characterCards','tokenCards'])cards+=Array.isArray(group[kind])?group[kind].length:0;
    for(const kind of ['characterInsts','tokenInsts']) {
      if(group[kind]!=null&&!Array.isArray(group[kind])){unplaced.push(`${scope}.${kind}: 非数组`);continue;}
      for(const [index,item] of (group[kind]||[]).entries()) {
        const id=`${scope}.${kind}[${index}]`, position=item?.position;
        const label=item?.alias||item?.inst?.characterKey||id;
        if(!Number.isInteger(position?.row)||!Number.isInteger(position?.col)||position.row<0||position.row>=rows||position.col<0||position.col>=cols){unplaced.push(`${id}: 缺少有效位置`);continue;}
        objects.push({id,label,row:position.row,col:position.col,hidden:item.hidden===true,hard:scope==='hardPredefines',raw:JSON.parse(JSON.stringify(item))});
      }
    }
  }
  return {rows,cols,tiles,objects,cards,unplaced,unknownTiles:[...unknownTiles]};
}
