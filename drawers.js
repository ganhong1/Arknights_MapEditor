(() => {
  'use strict';
  const handles=[...document.querySelectorAll('.drawer-handle')];
  const layout=document.querySelector('.map-editor-layout');
  const toolbar=document.querySelector('.canvas-toolbar');
  function alignDrawers(){
    const controls=document.querySelector('#prtsMapHost')?.shadowRoot?.querySelector('.controls');
    const bottom=Math.max(toolbar.getBoundingClientRect().bottom,controls?.getBoundingClientRect().bottom||0);
    layout.style.setProperty('--drawer-top',Math.max(0,bottom-layout.getBoundingClientRect().top+10)+'px');
  }
  const observer=new ResizeObserver(alignDrawers);observer.observe(toolbar);observer.observe(layout);
  window.addEventListener('map-controls-ready',()=>{
    const controls=document.querySelector('#prtsMapHost')?.shadowRoot?.querySelector('.controls');
    if(controls)observer.observe(controls);alignDrawers();
  });
  document.querySelector('#mapViewMode').addEventListener('change',()=>requestAnimationFrame(alignDrawers));
  alignDrawers();
  function setOpen(id,open,restoreFocus=false){
    const panel=document.getElementById(id);
    const handle=handles.find(button=>button.getAttribute('aria-controls')===id);
    if(open && window.matchMedia('(max-width: 900px)').matches){
      for(const other of handles)if(other!==handle)setOpen(other.getAttribute('aria-controls'),false);
    }
    panel.inert=!open;panel.classList.toggle('is-open',open);
    handle.setAttribute('aria-expanded',String(open));
    if(open)panel.querySelector('button').focus();
    else if(restoreFocus)handle.focus();
  }
  window.addEventListener('editor-reveal-panel',({target})=>{
    const drawer=target.closest('.editor-drawer');
    if(drawer&&!drawer.hidden&&!drawer.classList.contains('is-open'))setOpen(drawer.id,true);
  });
  for(const handle of handles)handle.onclick=()=>setOpen(handle.getAttribute('aria-controls'),handle.getAttribute('aria-expanded')!=='true');
  for(const button of document.querySelectorAll('[data-close-drawer]'))button.onclick=()=>setOpen(button.dataset.closeDrawer,false,true);
  document.addEventListener('keydown',event=>{
    if(event.key!=='Escape'||document.querySelector('dialog[open]'))return;
    const panel=document.activeElement.closest('.editor-drawer.is-open')||document.querySelector('.editor-drawer.is-open');
    if(panel){setOpen(panel.id,false,true);event.preventDefault();}
  });
})();
