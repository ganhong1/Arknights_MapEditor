import {createEditorRenderer} from './editor-renderer.mjs?v=camera1';
const editor=window.MapEditor;
const host=document.getElementById('prtsMapHost');
const canvasWrap=document.querySelector('.map-editor-canvas-wrap');
const toggle=document.getElementById('mapViewMode');
let renderer, pendingFrame=0;
function render(){
  try {
    const state=editor.getState();
    renderer.update(editor.buildMapJson(),state.routes,state.routeIndex);
  } catch(error) {
    window.MapEditorPerspective.lastError=error.message;
    console.error('PRTSMap editor rendering failed',error);
    window.MapEditorPerspective.active=false;host.hidden=true;canvasWrap.hidden=false;
    toggle.value='canvas';document.getElementById('editorStatus').textContent='立体视图失败，已恢复二维画布：'+error.message;
    editor.draw();
  }
}
function update(){if(!pendingFrame)pendingFrame=requestAnimationFrame(()=>{pendingFrame=0;render();});}
renderer=createEditorRenderer(host,(col,row,event)=>editor.applyCellAction(col,row,event));
window.dispatchEvent(new Event('map-controls-ready'));
window.MapEditorPerspective={active:true,update};
toggle.onchange=()=>{
  const active=toggle.value==='prts';window.MapEditorPerspective.active=active;
  host.hidden=!active;canvasWrap.hidden=active;editor.draw();
};
canvasWrap.hidden=true;host.hidden=false;editor.draw();
window.addEventListener('pagehide',event=>{if(!event.persisted){cancelAnimationFrame(pendingFrame);renderer.dispose();}});
