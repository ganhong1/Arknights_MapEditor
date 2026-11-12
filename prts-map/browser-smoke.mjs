// Run via import in a dedicated browser test tab; restores the original document.
export async function runBrowserSmoke() {
  const E=window.MapEditor,M=window.MapModel,U=window.MapEditorUI;
  const original=E.buildMapJson(),root=document.querySelector('#prtsMapHost').shadowRoot;
  const settle=()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
  const checks=[];
  const check=(value,name)=>{if(!value)throw Error(name+' / '+window.MapEditorPerspective.lastError);checks.push(name);};
  const mode=value=>{const select=document.querySelector('#mapViewMode');select.value=value;select.dispatchEvent(new Event('change'));};
  const press=(row,col,options={})=>root.querySelector(`[data-cell="${row}:${col}"]`).dispatchEvent(new PointerEvent('pointerdown',{button:0,bubbles:true,...options}));
  try {
    U.load(M.create(),'smoke.json');mode('prts');await settle();
    press(0,0);check(E.getState().cells[0][0].tileKey==='tile_road','paint');
    const saved=JSON.stringify(E.buildMapJson());
    for(const label of ['2D 平面','3D 立体'])[...root.querySelectorAll('button')].find(b=>b.textContent===label).click();
    check(JSON.stringify(E.buildMapJson())===saved,'view modes preserve export');
    press(0,0,{button:2});check(E.getState().cells[0][0]===null,'erase');
    mode('canvas');check(!window.MapEditorPerspective.active,'canvas fallback');
    mode('prts');check(window.MapEditorPerspective.active,'PRTS restore');
    const d=M.create();d.customFutureField={value:3};
    d.mapData.tiles[0]={...M.tile('tile_unknown_test'),blackboard:[{key:'retained',value:7}]};
    d.predefines.tokenInsts=[{alias:'box',position:{row:0,col:0},hidden:true,inst:{characterKey:'trap_test'}}];
    U.load(d,'smoke.json');await settle();
    check(root.querySelector('[data-cell="0:0"]').classList.contains('unknown'),'unknown indicator');
    check(root.querySelector('.object.hidden')?.textContent==='box','hidden predefine');
    const beforePick=JSON.stringify(E.buildMapJson());press(0,0,{altKey:true});
    check(E.getState().brush.blackboard[0].value===7,'full blackboard pick');
    check(JSON.stringify(E.buildMapJson())===beforePick,'pick preserves data');
    document.querySelector('[data-editor-tool="START"]').click();press(1,2);
    check(E.activeRoute().startPosition.row===1&&E.activeRoute().startPosition.col===2,'route coordinate');
    check(E.buildMapJson().customFutureField.value===3,'unknown field retained');
    document.querySelector('#editorMapWidth').value=4;document.querySelector('#editorMapHeight').value=3;
    document.querySelector('#editorResizeMapButton').click();await settle();
    check(root.querySelectorAll('[data-cell]').length===12,'resize');
    check(window.MapEditorPerspective.active,'no render fallback');
    return {passed:checks.length,checks};
  } finally { U.load(original,'level_custom.json'); }
}
