(function (g) {
  'use strict';
  const S = typeof module !== 'undefined' ? require('./schema.js') : g.MapSchema;
  const clone = v => JSON.parse(JSON.stringify(v));
  const checkpoint = (type = 'MOVE') => ({type,time:0,position:{row:0,col:0},reachOffset:{x:0,y:0},randomizeReachOffset:false,reachDistance:0});
  const route = () => ({motionMode:'WALK',startPosition:{row:0,col:0},endPosition:{row:0,col:1},spawnRandomRange:{x:0,y:0},spawnOffset:{x:0,y:0},checkpoints:[],allowDiagonalMove:true,visitEveryTileCenter:false,visitEveryNodeCenter:false,visitEveryCheckPoint:false});
  const tile = (key = 'tile_forbidden') => clone(S.tiles[key] || {tileKey:key,heightType:'HIGHLAND',buildableType:'NONE',passableMask:'FLY_ONLY',playerSideMask:'ALL',blackboard:null,effects:null});
  const action = (branch = false) => ({...clone(S.action),managedByScheduler:!branch});
  const fragment = () => ({preDelay:0,actions:[]});
  const wave = () => ({preDelay:0,postDelay:0,maxTimeWaitingForNextWave:-1,fragments:[fragment()]});
  function create() {
    const d = clone(S.base); d.routes=[route()];d.routes[0].endPosition={row:5,col:9};
    d.mapData.tiles=Array.from({length:60},()=>tile());
    d.mapData.map=Array.from({length:6},(_,y)=>Array.from({length:10},(_,x)=>(5-y)*10+x));
    d.waves=[wave()]; return d;
  }
  function assertMap(d) {
    if (!d || typeof d !== 'object' || Array.isArray(d)) throw Error('地图根节点必须为对象');
    const m=d.mapData;
    if (!m || !Array.isArray(m.map) || !m.map.length || !Array.isArray(m.tiles)) throw Error('缺少 mapData.map / tiles');
    const w=m.map[0]?.length;
    if (!w || w>256 || m.map.length>256) throw Error('地图尺寸必须在 1–256 格以内');
    if (m.map.some(row=>!Array.isArray(row)||row.length!==w||row.some(i=>!Number.isInteger(i)||i<0||i>=m.tiles.length||!m.tiles[i]))) throw Error('map 必须为矩形，且每格必须引用有效的 tiles 索引');
    for (const k of ['routes','extraRoutes','waves','enemyDbRefs']) if(d[k]!=null&&!Array.isArray(d[k])) throw Error(k+' 必须为数组');
    for(const k of ['routes','extraRoutes']) for(const r of d[k]||[]) {
      if(!r?.startPosition || !r.endPosition || (r.checkpoints!=null&&!Array.isArray(r.checkpoints))) throw Error(k+' 的路线缺少起终点或 checkpoints');
      for(const p of [r.startPosition,r.endPosition,...(r.checkpoints||[]).map(c=>c.position).filter(Boolean)]) if(!Number.isFinite(p.row)||!Number.isFinite(p.col)) throw Error('路线坐标必须为数字');
    }
    for(const w of d.waves||[]) if(!Array.isArray(w?.fragments)) throw Error('wave.fragments 必须为数组');
    if(d.branches!=null && (typeof d.branches!=='object'||Array.isArray(d.branches))) throw Error('branches 必须为名称到 Branch 的对象或 null');
    for(const b of Object.values(d.branches||{})) if(!Array.isArray(b?.phases)) throw Error('branch.phases 必须为数组');
    for(const {part} of parts(d)) if(!Array.isArray(part?.actions)||part.actions.some(a=>!a||typeof a!=='object'||Array.isArray(a))) throw Error('片段/phase.actions 必须为动作对象数组');
  }
  function cells(d) { return d.mapData.map.slice().reverse().map(row=>row.map(i=>clone(d.mapData.tiles[i]))); }
  function exportMap(d, grid) {
    const out=clone(d), old=out.mapData, h=grid.length,w=grid[0].length;
    const sameSize=old.map.length===h&&old.map[0].length===w;
    const definitions=sameSize?clone(old.tiles):[];
    const uses=new Map();if(sameSize)for(const i of old.map.flat())uses.set(i,(uses.get(i)||0)+1);
    const map=Array.from({length:h},()=>[]);
    for(let y=0;y<h;y++) for(let x=0;x<w;x++) {
      const t=clone(grid[y][x]||tile());
      const original=sameSize?old.map[h-1-y][x]:-1;
      if(original>=0&&JSON.stringify(old.tiles[original])===JSON.stringify(t)) map[h-1-y][x]=original;
      else if(original>=0&&uses.get(original)===1){definitions[original]=t;map[h-1-y][x]=original;}
      else {map[h-1-y][x]=definitions.length;definitions.push(t);}
    }
    out.mapData={...old,map,tiles:definitions};
    // Remove only the known legacy calculator envelope, never arbitrary game fields.
    const legacy=!!out.routeCalculatorTest;delete out.routeCalculatorTest;
    if(legacy&&Array.isArray(out.optionalRunes)&&!out.optionalRunes.length)out.optionalRunes=null;
    for(const key of ['routes','extraRoutes']) for(const r of out[key]||[]) {delete r.editorId;delete r.visitEveryNodeStably;if(legacy&&r.checkpoints)r.checkpoints=r.checkpoints.map(c=>({...checkpoint(c.type),...c}));}
    for(const {part} of parts(out)) for(const a of part.actions||[]) delete a.editorEnemyId;
    return out;
  }
  function parts(d) {
    const out=[];
    (d.waves||[]).forEach((w,wi)=>(w.fragments||[]).forEach((part,fi)=>out.push({part,branch:false,waveIndex:wi,fragmentIndex:fi,path:`waves[${wi}].fragments[${fi}]`})));
    Object.entries(d.branches||{}).forEach(([k,b])=>(b.phases||[]).forEach((part,i)=>out.push({part,branch:true,branchKey:k,fragmentIndex:i,path:`branches.${k}.phases[${i}]`})));
    return out;
  }
  const routeLibrary=(_action,branch)=>branch?'extraRoutes':'routes';
  // PC 2.7.81 opera nodes verified against config_common.ab.
  const operaRoutes = {
    usesRoute: {
      "act12side": ["screen_submerged_start"],
      "act34side_09": ["cannon_fire", "kill_all"],
      "common_guide": ["effect_deploy", "effect_warning_01", "finish_effect_deploy"],
      "main_08-15": ["fire_effect"],
      "main_16_boss2": ["move_camera"],
      "rune_05-01": ["dscout_skill_start"],
    },
    ignoresRoute: {
      "act17d7_01": ["count_down", "play_screen_eff_blue", "switch_screen_eff_any_to_fever", "switch_screen_eff_blue_to_red", "switch_screen_eff_red_to_blue"],
      "act24side": ["init", "move_camera"],
      "common_guide": ["effect_warning_fullscreen"],
      "hard_08-04": ["blast_effect_x", "blast_effect_y"],
      "level_act46side_10": ["init", "move_camera_1", "move_camera_2"],
      "level_lt17_06": ["summon_boss"],
      "main_08-17": ["blast_effect_x", "blast_effect_y"],
      "main_14-09": ["main_14_shake"],
      "main_16_boss2": ["init"],
      "rogue_4_boss4_formal": ["init", "move_camera"],
      "rogue_4_boss5_formal": ["init", "move_camera_1", "move_camera_2"],
      "rogue_6_boss1": ["kill_boss"],
      "sandbox_v2_dlc1": ["racing_spawn_pause"],
    },
  };
  function operaRouteUse(d, action) {
    const config = d.operaConfig;
    if (typeof config !== 'string' || typeof action.key !== 'string') return null;
    if (Object.hasOwn(operaRoutes.ignoresRoute, config) && operaRoutes.ignoresRoute[config].includes(action.key)) return false;
    return Object.hasOwn(operaRoutes.usesRoute, config) && operaRoutes.usesRoute[config].includes(action.key)
      && (action.useExtraRoute === undefined || action.useExtraRoute === false) ? true : null;
  }
  function deleteRoute(d, library, index) {
    if (!['routes', 'extraRoutes'].includes(library) || !Array.isArray(d[library]) ||
        !Number.isInteger(index) || index < 0 || index >= d[library].length) {
      throw Error('路线索引无效');
    }
    const refs = [];
    const understood = new Set();
    for (const {part, branch, path} of parts(d)) {
      for (const [actionIndex, a] of (part.actions || []).entries()) {
        const actionPath = path + '.actions[' + actionIndex + ']';
        const type = a.actionType;
        const known = typeof type === 'string' ? type !== 'E_NUM' && Object.hasOwn(S.enums.ActionType, type)
          : Number.isInteger(type) && type >= 0 && type < S.enums.ActionType.E_NUM;
        if (known) understood.add(a);
        if (['SPAWN', 'PREVIEW_CURSOR', 0, 1].includes(a.actionType) && routeLibrary(a, branch) === library) {
          refs.push({a, path: actionPath, pos: a.routeIndex});
        } else if (branch && (type === 'EMPTY' || type === 13) && library === 'extraRoutes') {
          const pos = a.routeIndex === undefined ? 0 : a.routeIndex;
          if (!Number.isInteger(pos)) throw Error('无效的分支路线索引：' + actionPath);
          if (pos >= 0 && pos < d[library].length) refs.push({a, path: actionPath, pos});
        } else if (type === 'PLAY_OPERA' || type === 7) {
          const use = operaRouteUse(d, a);
          const pos = a.routeIndex === undefined ? 0 : a.routeIndex;
          const legacy = a.useExtraRoute !== undefined && a.useExtraRoute !== false;
          if (use === null && (library === 'routes' || legacy) &&
              (!Number.isInteger(pos) || (pos >= index && pos < d[library].length))) {
            throw Error('无法解析演出动作的路线引用：' + actionPath);
          }
          if (use === true && library === 'routes') {
            if (!Number.isInteger(pos)) throw Error('无效的演出路线索引：' + actionPath);
            if (pos >= 0 && pos < d[library].length) refs.push({a, path: actionPath, pos});
          }
        }
      }
    }
    const pending = [d];
    while (pending.length > 0) {
      const value = pending.pop();
      if (value === null || typeof value !== 'object') continue;
      if (Object.hasOwn(value, 'routeIndex') && !understood.has(value)) throw Error('存在无法解析的路线索引');
      for (const child of Object.values(value)) if (child !== null && typeof child === 'object') pending.push(child);
    }
    const used=refs.find(({pos})=>pos===index);
    if(used) throw Error(`路线仍被 ${used.path} 使用，请先修改动作的 routeIndex`);
    d[library].splice(index,1);refs.forEach(({a,pos})=>{if(pos>index)a.routeIndex=pos-1;});
  }
  function validate(d) {
    const errors=[],warnings=[];
    try{assertMap(d);}catch(e){return {errors:[e.message],warnings};}
    const checkNumber=(v,path,min,integer=false)=>{if(!Number.isFinite(v)||v<min||(integer&&!Number.isInteger(v)))errors.push(path+' 数值无效');};
    const checkEnum=(v,name,path)=>{if(!Object.hasOwn(S.enums[name],v)&&!Object.values(S.enums[name]).includes(v)) warnings.push(path+' 含未知枚举，将原样保留：'+v);};
    for(const [i,w] of (d.waves||[]).entries()) {checkNumber(w.preDelay,`waves[${i}].preDelay`,0);checkNumber(w.postDelay,`waves[${i}].postDelay`,0);checkNumber(w.maxTimeWaitingForNextWave,`waves[${i}].maxTimeWaitingForNextWave`,-1);}
    for(const {part,branch,path} of parts(d)) {
      checkNumber(part.preDelay,path+'.preDelay',0);
      part.actions.forEach((a,i)=>{
        const p=path+`.actions[${i}]`;
        checkEnum(a.actionType,'ActionType',p+'.actionType');
        for(const k of ['preDelay','interval','count']) checkNumber(a[k],p+'.'+k,0,k==='count');
        for(const k of ['randomType','refreshType']) if(a[k]!=null)checkEnum(a[k],k==='randomType'?'RandomType':'RefreshType',p+'.'+k);
        if(['SPAWN','PREVIEW_CURSOR',0,1].includes(a.actionType)) {
          const rs=d[routeLibrary(a,branch)]||[];
          if(!Number.isInteger(a.routeIndex)||!rs[a.routeIndex]) errors.push(p+'.routeIndex 未引用有效 '+routeLibrary(a,branch));
        }
        if(['SPAWN',0].includes(a.actionType)&&!(d.enemyDbRefs||[]).some(e=>e.id===a.key)) warnings.push(p+' 的敌人 key 未列入 enemyDbRefs：'+a.key);
        if(['PLAY_OPERA','BATTLE_EVENTS','TRIGGER_PREDEFINED','STORY','TUTORIAL','DIALOG'].includes(a.actionType)) warnings.push(p+' 依赖外部演出/脚本或预设 key，预览不执行该行为');
      });
    }
    for(const k of ['routes','extraRoutes']) for(const [i,r] of (d[k]||[]).entries()) {
      checkEnum(r.motionMode,'MotionMode',`${k}[${i}].motionMode`);
      if(r.motionMode==='E_NUM'||r.motionMode===2)errors.push(`${k}[${i}].motionMode E_NUM 是计数哨兵`);
      for(const c of r.checkpoints||[])checkEnum(c.type,'CheckpointType',`${k}[${i}].checkpoints`);
    }
    return {errors,warnings:[...new Set(warnings)]};
  }
  const api={clone,checkpoint,route,tile,action,fragment,wave,create,assertMap,cells,exportMap,parts,routeLibrary,deleteRoute,validate};
  if(typeof module!=='undefined')module.exports=api;else g.MapModel=api;
})(globalThis);
