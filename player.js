(() => {
  'use strict';
  const canvas=document.querySelector('#game'),overlay=document.querySelector('#overlay'),status=document.querySelector('#status'),detail=document.querySelector('#detail'),progress=document.querySelector('#progress'),start=document.querySelector('#start'),retry=document.querySelector('#retry'),fullscreen=document.querySelector('#fullscreen');
  window.gameState={phase:'idle',progress:0};
  function fail(message){window.gameState.phase='error';window.gameState.error=String(message);status.textContent='游戏加载遇到问题';detail.textContent=String(message)+'\n请检查网络后重试。';progress.hidden=true;retry.hidden=false;overlay.hidden=false;}
  retry.addEventListener('click',()=>location.reload());
  fullscreen.addEventListener('click',()=>{if(window.unityInstance)window.unityInstance.SetFullscreen(1);});
  start.addEventListener('click',async()=>{
    start.disabled=true;start.hidden=true;progress.hidden=false;status.textContent='正在加载游戏…';window.gameState.phase='loading';
    try{
      const response=await fetch('game.json');if(!response.ok)throw Error('游戏配置读取失败（'+response.status+'）');
      const game=await response.json();
      const script=document.createElement('script');script.src=game.loader;
      await new Promise((resolve,reject)=>{script.onload=resolve;script.onerror=()=>reject(Error('游戏加载器下载失败'));document.body.append(script);});
      const instance=await createUnityInstance(canvas,{
        dataUrl:game.dataUrl,frameworkUrl:game.frameworkUrl,codeUrl:game.codeUrl,streamingAssetsUrl:'StreamingAssets',
        companyName:'SillyBishop',productName:game.slug,productVersion:'1.0',autoSyncPersistentDataPath:true,
        devicePixelRatio:Math.min(devicePixelRatio||1,1.5),
        showBanner:(message,type)=>{if(type==='error')fail(message);else console.warn(message);}
      },value=>{progress.value=value;window.gameState.progress=value;detail.textContent=value<.9?'正在下载资源 '+Math.round(value*100)+'%':'资源已就绪，正在启动引擎…';});
      window.unityInstance=instance;if(window.gameState.phase==='error')return;
      window.gameState.phase='ready';overlay.hidden=true;fullscreen.disabled=false;canvas.focus();
    }catch(error){fail(error.message||error);}
  });
})();
