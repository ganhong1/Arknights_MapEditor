import {classes,tiles as definitions} from './definitions.mjs';
import {tileBlackboard} from './blackboard.mjs';
import {projectLevel} from './projection.mjs';
const cls=(...names)=>names.filter(Boolean).map(name=>classes[name]||'').filter(Boolean).join(' ');

// Timeline main 5396314: CSS faces, decoration and projection fitting adapted
// for LevelData authoring. No Timeline transport, portraits or runtime state.
export function createEditorRenderer(host,onCell) {
  const root=host.attachShadow({mode:'open'}),doc=host.ownerDocument;
  const node=(tag,className,parent,text)=>{const e=doc.createElement(tag);e.className=className;if(text!==undefined)e.textContent=text;parent?.append(e);return e;};
  for(const file of ['reference.css','editor.css']){const link=node('link','',root);link.rel='stylesheet';link.href=new URL(file,import.meta.url).href;link.onload=()=>fit();}
  const controls=node('div','controls',root);
  const button=(label,fn)=>{const b=node('button','',controls,label);b.type='button';b.onclick=fn;return b;};
  const depth=button('3D 立体',()=>{scene.classList.add('map_render_hover');setDepth(true);});
  const flat=button('2D 平面',()=>{scene.classList.remove('map_render_hover');setDepth(false);});
  button('−',()=>{zoom=Math.max(.25,zoom/1.2);fit();});
  button('适应',()=>{zoom=1;pitch=30;yaw=0;camera();});
  button('+',()=>{zoom=Math.min(5,zoom*1.2);fit();});
  button('坐标',()=>scene.classList.toggle('hide-coords'));
  const freeButton=button('自由视角',()=>{free=!free;freeButton.setAttribute('aria-pressed',String(free));viewport.classList.toggle('free-view',free);});
  freeButton.setAttribute('aria-pressed','false');freeButton.title='开启后拖动旋转，关闭后继续绘制';
  const viewport=node('div','viewport',root),sizer=node('div','sizer',viewport);
  const scene=node('div','scene map_render_hover',sizer);
  const compatibility=doc.getElementById('mapCompatibility');
  const detail=doc.getElementById('mapSelectionDetail');
  let shape='',view=null,zoom=1,width=0,height=0,board=null,disposed=false;
  const cells=new Map(), tileIndex=new Map(), objectIndex=new Map();
  let free=false,drag=null,pitch=30,yaw=0,cameraFrame=0,routeSignature='';
  function camera(){cameraFrame=0;if(board)board.style.transform=`rotateX(${scene.classList.contains('map_render_hover')?pitch:0}deg) rotateZ(${yaw}deg)`;fit();}
  function scheduleCamera(){if(!cameraFrame)cameraFrame=requestAnimationFrame(camera);}
  viewport.addEventListener('wheel',event=>{event.preventDefault();const delta=event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?viewport.clientHeight:1);zoom=Math.max(.25,Math.min(5,zoom*Math.exp(-Math.max(-200,Math.min(200,delta))*.002)));scheduleCamera();},{passive:false});
  viewport.addEventListener('pointerdown',event=>{if(!free||event.button!==0)return;event.preventDefault();event.stopPropagation();drag={id:event.pointerId,x:event.clientX,y:event.clientY};viewport.setPointerCapture(event.pointerId);},{capture:true});
  viewport.addEventListener('pointermove',event=>{if(!drag||drag.id!==event.pointerId)return;yaw+=(event.clientX-drag.x)*.35;pitch=Math.max(0,Math.min(75,pitch-(event.clientY-drag.y)*.35));drag.x=event.clientX;drag.y=event.clientY;scheduleCamera();});
  const endDrag=()=>{drag=null;};
  viewport.addEventListener('pointerup',endDrag);viewport.addEventListener('pointercancel',endDrag);viewport.addEventListener('lostpointercapture',endDrag);

  function setDepth(value){depth.setAttribute('aria-pressed',String(value));flat.setAttribute('aria-pressed',String(!value));camera();}
  function fit(){
    if(disposed||!width||!viewport.clientWidth)return;
    const scale=Math.min(1,(viewport.clientWidth-12)/width)*zoom;
    scene.style.transform=`scale(${scale})`;scene.style.width=width+'px';scene.style.height=height+'px';
    sizer.style.width=width*scale+'px';sizer.style.height=height*scale+'px';
  }
  const observer=new ResizeObserver(fit);observer.observe(viewport);setDepth(true);
  function decorate(cell,t){
    const key=t.tileKey,definition=definitions[key]||{},kind=definition.render_as||key;
    const high=t.heightType===1||t.heightType==='HIGHLAND';
    const block=(high&&key!=='tile_flystart')||['tile_passable_wall','tile_passable_wall_forbidden'].includes(key);
    const box=['tile_start','tile_end','tile_flystart','tile_crate','tile_start_cooperate','tile_end_cooperate'].includes(key);
    const hole=['tile_hole','tile_deepwater'].includes(key),fence=['tile_fence','tile_fence_bound'].includes(key);
    const allowed=['MELEE','RANGED','ALL',1,2,3].includes(t.buildableType);
    cell.className=cls('tile',kind,block&&'tile_block',box&&'tile_box',hole&&'tile_hole',fence&&'tile_fence',allowed&&key!=='tile_crate'&&'tile_deployable')+(!definitions[key]?' unknown':'');
    cell.replaceChildren();cell.style.zIndex=5900-10*t.row;
    if(block||box||hole||fence)for(const face of ['tile_box_left','tile_box_top','tile_box_right'])node('span',cls(face),cell);
    const overlay=(kind,text,options=[])=>node('span',cls('tile_overlay',kind,...options,options.includes('content_on_top')&&'tile_block_overlay'),cell,text);
    const bb=tileBlackboard(t,'leveldata');
    if(definition.effect&&(!key.startsWith('tile_yinyang')||bb.yinYang||key==='tile_yinyang_switch'))overlay(definition.effect,undefined,[block&&'content_on_top',box&&'content_on_box',bb.yinYang]);
    if(definition.content)overlay('content',definition.content,[block&&definition.content_on_top&&'content_on_top',box&&'content_on_box']);
    if(key==='tile_grvtybtn')overlay('button_tile_grvtybtn',bb.gravity);
    if(allowed&&key!=='tile_crate')overlay('deployable',undefined,[block&&'content_on_top']);
    overlay('coord',`${t.col},${t.row}`,[block&&'content_on_top']).dataset.coordinate='';
    if(!definitions[key])node('span','badge',cell,'?');
    const placed=(objectIndex.get(`${t.row}:${t.col}`)||[]);
    if(placed.length)node('span','object'+(placed.every(o=>o.hidden)?' hidden':''),cell,placed.length===1?placed[0].label:`${placed.length} 个预设`);
    cell.title=`${key} (${t.col},${t.row})${placed.length?'\n'+placed.map(o=>o.label+(o.hidden?' [隐藏]':'')+(o.hard?' [hard]':'')).join('\n'):''}`;
  }
  function act(cell,event){
    if(free)return;
    const [row,col]=cell.dataset.cell.split(':').map(Number);
    const tile=tileIndex.get(`${row}:${col}`);
    detail.textContent=JSON.stringify({tile,predefines:(objectIndex.get(`${row}:${col}`)||[])},null,2);
    onCell(col,row,event);
  }
  function rebuild(){
    scene.replaceChildren();cells.clear();board=node('div',cls('tile_rows')+' board',scene);
    width=Math.max(360,view.cols*50+100);height=view.rows*50+160;
    for(let row=0;row<view.rows;row++){
      const line=node('div',cls('tile_row'),board);
      for(let col=0;col<view.cols;col++){
        const wrap=node('div',cls('tile_wrapper'),line),cell=node('div','',wrap);
        cell.dataset.cell=`${row}:${col}`;cell.tabIndex=0;cell.setAttribute('role','button');
        cell.onpointerdown=event=>{if(event.button===0||event.button===2){event.preventDefault();act(cell,event);}};
        cell.onpointerenter=event=>{if(event.buttons===1||event.buttons===2)act(cell,event);};
        cell.oncontextmenu=event=>event.preventDefault();
        cell.onkeydown=event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();act(cell,event);}};
        cells.set(`${row}:${col}`,cell);
      }
    }
  }
  function drawRoutes(routes,selected){
    board.querySelector('svg')?.remove();
    const ns='http://www.w3.org/2000/svg',svg=doc.createElementNS(ns,'svg');svg.classList.add('route-overlay');
    svg.setAttribute('width',view.cols*50-2);svg.setAttribute('height',view.rows*50-2);board.append(svg);
    for(const [index,route] of routes.entries()){
      const points=[route.startPosition,...(route.checkpoints||[]).filter(c=>['MOVE','PATROL_MOVE','APPEAR_AT_POS','MAP_OFFSET_MOVE',0,6,8,10].includes(c.type)).map(c=>c.position),route.endPosition].filter(p=>Number.isFinite(p?.row)&&Number.isFinite(p?.col));
      const line=doc.createElementNS(ns,'polyline');line.setAttribute('points',points.map(p=>`${p.col*50+24},${(view.rows-1-p.row)*50+24}`).join(' '));line.setAttribute('fill','none');line.setAttribute('stroke',index===selected?'#69ccee':'#69ccee66');line.setAttribute('stroke-width','3');svg.append(line);
    }
  }
  return {
    update(level,routes,selected){
      view=projectLevel(level);tileIndex.clear();objectIndex.clear();for(const t of view.tiles)tileIndex.set(`${t.row}:${t.col}`,t);for(const o of view.objects){const k=`${o.row}:${o.col}`;if(!objectIndex.has(k))objectIndex.set(k,[]);objectIndex.get(k).push(o);}const nextShape=`${view.rows}:${view.cols}`;
      if(shape!==nextShape){shape=nextShape;rebuild();routeSignature='';camera();}
      for(const tile of view.tiles){const cell=cells.get(`${tile.row}:${tile.col}`);const signature=JSON.stringify([tile,(objectIndex.get(`${tile.row}:${tile.col}`)||[])]);if(cell.dataset.signature!==signature){decorate(cell,tile);cell.dataset.signature=signature;cell.setAttribute('aria-label',cell.title);}}
      const nextRoutes=JSON.stringify([routes,selected,shape]);if(nextRoutes!==routeSignature){drawRoutes(routes,selected);routeSignature=nextRoutes;}fit();
      compatibility.textContent=`PRTSMap 配置示意，非游戏实景。${view.unknownTiles.length?' 专属样式未知：'+view.unknownTiles.join('、')+'；按高度显示并标 ?。':''} 预设 ${view.objects.length} 个（通用标记，隐藏项不代表已激活）；卡牌 ${view.cards} 个（不放置到地图）。${view.unplaced.length?' 无法定位：'+view.unplaced.join('；'):''}`;
    },
    dispose(){disposed=true;cancelAnimationFrame(cameraFrame);observer.disconnect();root.replaceChildren();}
  };
}
