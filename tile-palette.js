(() => {
  'use strict';
  const palette=document.querySelector('.tile-legend-grid');
  const input=document.getElementById('editorTileKey');
  const node=(tag,parent,text)=>{const element=document.createElement(tag);if(text!==undefined)element.textContent=text;parent.append(element);return element;};
  const height={LOWLAND:'地面',HIGHLAND:'高台'};
  const build={NONE:'不可部署',MELEE:'近战位',RANGED:'远程位',ALL:'均可部署'};
  const movement={ALL:'地面与空中通行',FLY_ONLY:'仅空中通行',WALK_ONLY:'仅地面通行',NONE:'不可通行'};
  const nameOf=key=>{const name=window.TileInfo[key]?.name;return !name||name==='说明表未列名'?'名称未收录':name;};
  const dialog=node('dialog',document.body);dialog.className='tile-info-dialog';dialog.setAttribute('aria-labelledby','tileInfoTitle');
  const heading=node('header',dialog);heading.className='tile-info-heading';
  const title=node('h2',heading);title.id='tileInfoTitle';
  const close=node('button',heading,'关闭');close.type='button';close.autofocus=true;close.onclick=()=>dialog.close();
  const content=node('div',dialog);content.className='tile-info-content';
  const wikiPages={"tile_bigforce": "特种战术点", "tile_character": "字格", "tile_characterf": "字格#字格（？？？）", "tile_deepsea": "深水区", "tile_deepwater": "深水区#深水区（火蓝之心）", "tile_defup": "防御符文", "tile_dygmny_1": "化境地块", "tile_dygmny_2": "化境地块#化境地块（不可部署）", "tile_dygmny_3": "化境地块#化境地块（高台）", "tile_empty": "基本地形#empty", "tile_end": "基本地形#保护目标", "tile_fence": "围栏", "tile_fence_bound": "围墙", "tile_floor": "基本地形#不可放置位", "tile_flystart": "基本地形#空袭侵入点", "tile_forbidden": "基本地形#禁入区", "tile_gazebo": "防空符文", "tile_grvtybtn": "重力感应机关", "tile_passable_wall": "玉门天灾工事", "tile_passable_wall_forbidden": "玉门天灾工事", "tile_quicksand": "盐漠", "tile_rabbithole_in_1": "兔子洞入口", "tile_rabbithole_out_1": "兔子洞入口#兔子洞出口", "tile_rabbithole_out_2": "兔子洞入口#隐蔽的兔子洞出口", "tile_rcm_crate": "基本地形#推荐障碍放置点", "tile_rcm_operator": "基本地形#推荐干员放置点", "tile_road": "基本地形#平地", "tile_start": "基本地形#侵入点", "tile_telin": "基本地形#通道入口", "tile_telout": "基本地形#通道出口", "tile_wall": "基本地形#高台", "tile_woodrd": "临时步道"};
  // Wiki describes these tiles within the associated entity/mechanism page.
  wikiPages.tile_sleep_road='入梦砖#入梦砖';
  wikiPages.tile_sleep_wall='入梦砖#入梦砖';
  let opener=null;
  dialog.addEventListener('close',()=>{content.replaceChildren();opener?.focus();});
  function showDetails(key,button){
    opener=button;title.textContent=nameOf(key);content.replaceChildren();
    node('code',content,key);
    const page=wikiPages[key];
    const url='https://prts.wiki/w/'+(page ? page.split('#').map(encodeURIComponent).join('#') : encodeURIComponent('特殊地形'));
    const toolbar=node('div',content);toolbar.className='tile-wiki-toolbar';
    node('span',toolbar,page ? ((key==='tile_sleep_road'||key==='tile_sleep_wall')?'关联机制说明（含附着实体资料） · ':'PRTS Wiki · ')+page : '暂无精确页面绑定，显示特殊地形总览');
    const link=node('a',toolbar,'在新标签页打开');link.href=url;link.target='_blank';link.rel='noopener noreferrer';
    node('p',content,'页面由 PRTS Wiki 提供；若未显示，请使用上方链接打开。').className='muted';
    const showFallback=message=>{
      content.querySelector('iframe')?.remove();
      const notice=node('p',content,message);notice.setAttribute('role','status');
      node('p',content,window.TileInfo[key]?.description||'暂无本地说明。');
    };
    if(!page){
      showFallback('此地块尚无准确的 Wiki 页面绑定，暂时显示本地说明。可通过上方链接在新标签页查阅总览。');
    }else{
      const frame=node('iframe',content);frame.className='tile-wiki-frame';frame.title=nameOf(key)+' — PRTS Wiki';
      frame.referrerPolicy='no-referrer';
      frame.setAttribute('sandbox','allow-scripts allow-same-origin allow-popups allow-popups-to-escape-sandbox');
      let recentLoads=[];
      frame.addEventListener('load',()=>{
        const now=Date.now();recentLoads=recentLoads.filter(time=>now-time<15000);recentLoads.push(now);
        if(recentLoads.length>=3 && frame.isConnected)showFallback('检测到嵌入页面连续重新加载，已停止。可能需要在新标签页完成网站验证，请使用上方链接。');
      });
      frame.addEventListener('error',()=>{if(frame.isConnected)showFallback('Wiki 嵌入加载失败，请在新标签页打开。');});
      frame.src=url;
    }
    dialog.showModal();
  }
  function sync(){for(const item of palette.querySelectorAll('[data-tile-key]'))item.setAttribute('aria-pressed',String(item.dataset.tileKey===input.value));}
  palette.replaceChildren();document.querySelector('.tile-legend-title').textContent='选择地形';
  for(const [key,sample] of Object.entries(window.MapSchema.tiles)){
    const row=node('div',palette);row.className='tile-palette-entry';
    const select=node('button',row);select.type='button';select.className='tile-legend-item tile-palette-button';select.dataset.tileKey=key;
    const icon=node('i',select);icon.className='tile-legend-icon '+key.replaceAll('_','-');icon.setAttribute('aria-hidden','true');
    const text=node('span',select);node('strong',text,nameOf(key));node('small',text,`${height[sample.heightType]||sample.heightType} · ${build[sample.buildableType]||sample.buildableType}`);node('small',text,key);
    select.onclick=()=>{input.value=key;input.dispatchEvent(new Event('change',{bubbles:true}));sync();document.getElementById('editorStatus').textContent='已选择 '+nameOf(key)+'，点击地图绘制。';};
    const info=node('button',row);info.type='button';info.className='tile-info-button';info.dataset.tileInfo=key;
    info.setAttribute('aria-label','查看'+nameOf(key)+'详情（'+key+'）');info.title='查看详情';info.setAttribute('aria-haspopup','dialog');
    const image=node('img',info);image.src='assets/icons/info.svg';image.alt='';image.width=20;image.height=20;
    info.onclick=()=>showDetails(key,info);
  }
  input.addEventListener('change',sync);window.addEventListener('editor-changed',sync);sync();
})();
