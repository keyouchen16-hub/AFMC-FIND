const fs=require('fs'),path=require('path');
const out=path.resolve(__dirname,'../review-20261105');fs.mkdirSync(out,{recursive:true});
let main=fs.readFileSync(path.resolve(__dirname,'../ui-quiet-glass-20261105/index.html'),'utf8');
const replace=(s,a,b)=>{if(!s.includes(a))throw Error('Missing fragment: '+a.slice(0,80));return s.replace(a,b)};
main=replace(main,'<small>0.8.12</small>','<small>布局修订预览</small>');
main=replace(main,'<button class="more-trigger" id="more-button" aria-label="更多操作" aria-expanded="false">${icon(\'more\')}</button>','<button class="secondary" id="copy-link-action">${icon(\'copy\')}复制链接</button>');
main=replace(main,"$('#more-button').onclick=e=>{e.stopPropagation();const menu=$('#more-menu');menu.hidden=!menu.hidden;$('#more-button').setAttribute('aria-expanded',String(!menu.hidden));if(!menu.hidden)menu.querySelector('button').focus()};","$('#copy-link-action').onclick=()=>toast('预览：正式版复制当前资源链接，不调用下载软件。');");
main=replace(main,'<p>${r.file}</p></details></div>','<p>${r.file}</p></details><div class="source-note"><span>来源说明</span><p>${r.source} · 此处为虚构示例来源。正式版展示当前条目的原始来源与元数据差异，不再放进更多菜单。</p></div></div>');
const playerStart=main.indexOf('function openPlayer(row)'),playerEnd=main.indexOf("$('#search-form')",playerStart);
main=main.slice(0,playerStart)+"function openPlayer(row){toast(row&&row.category!=='video'?'预览：交给下载软件。':'沿用已安装软件的播放器，本次不重做播放器，也不从网页预览启动播放。');}\n"+main.slice(playerEnd);
const sourceStart=main.indexOf("$('#sources-button').onclick="),sourceEnd=main.indexOf("$('#player-button').onclick=",sourceStart);
main=main.slice(0,sourceStart)+"$('#sources-button').onclick=()=>location.href='sources.html';"+main.slice(sourceEnd);
const settingsStart=main.indexOf("$('#settings-button').onclick="),settingsEnd=main.indexOf("$('#sources-button').onclick=",settingsStart);
main=main.slice(0,settingsStart)+`$('#settings-button').onclick=()=>{openDialog('设置',
'<div class="settings-brand"><img src="../../src/Afmc.Find.App/Assets/afmc-find.png" alt="AFMC FIND Logo"><b>AFMC FIND</b><img class="family-mark" src="../../src/Afmc.Find.App/Assets/family-mark.png" alt="家族标志"></div><p class="description">让工具安静地配合你的习惯。</p><div class="settings-card">'+
[['tray','关闭后留在托盘','从托盘图标回到当前搜索。'],['minimize','最小化时收起到托盘','关闭后，最小化仍保留在任务栏。'],['motion','减少动态效果','关闭淡入与细微过渡。']].map(([key,title,hint])=>'<div class="setting-row"><div class="setting-text"><strong>'+title+'</strong><p>'+hint+'</p></div><button role="switch" aria-label="'+title+'" class="switch" data-setting="'+key+'"><span></span></button></div>').join('')+'</div><p class="brand-phrase">Find the You<span>.</span></p><p class="description">有建议，或遇到问题，欢迎联系。</p><p class="feedback-mail">keyouchen16@gmail.com</p><button id="copy-feedback" class="secondary">复制邮箱</button><p class="modal-text">设置仅影响预览，不会修改软件配置。</p>',
'<div class="dialog-footer"><button class="primary" id="settings-done">完成预览</button></div>');$$('[data-setting]').forEach(b=>{b.setAttribute('aria-checked',String(settings[b.dataset.setting]));b.onclick=()=>{const key=b.dataset.setting;settings[key]=!settings[key];b.setAttribute('aria-checked',String(settings[key]));document.body.classList.toggle('reduce-motion',settings.motion)}});$('#settings-done').onclick=closeDialog;$('#copy-feedback').onclick=()=>navigator.clipboard.writeText('keyouchen16@gmail.com').then(()=>toast('邮箱已复制')).catch(()=>toast('邮箱：keyouchen16@gmail.com'));};
`+main.slice(settingsEnd);
main=replace(main,'</style>',`.secondary-row .secondary{flex:1 1 0;min-width:0;padding:0 4px}.source-note{border-top:1px solid var(--line);padding-top:14px;margin-top:16px;color:var(--muted);font-size:11px}.source-note p{line-height:1.8;margin:8px 0 0}.overlay{position:fixed}.dialog{max-height:calc(100dvh - 48px);overflow:auto}.settings-brand{display:flex;align-items:center;gap:10px;margin:18px 0}.settings-brand img{width:28px;height:28px;object-fit:contain}.settings-brand .family-mark{margin-left:auto;width:48px;height:48px;opacity:.8}.settings-card{padding:0 14px;border:1px solid var(--line);border-radius:12px}.brand-phrase{font-size:27px;letter-spacing:-.6px;margin:22px 0 12px}.brand-phrase span{color:var(--accent)}.feedback-mail{font-size:13px;margin:12px 0 16px}</style>`);
fs.writeFileSync(path.join(out,'index.html'),main);
let sources=fs.readFileSync(path.join(__dirname,'index.html'),'utf8');
sources=replace(sources,'<span class="small muted" id="source-count"></span>','<div style="display:flex;align-items:center;gap:16px"><span class="small muted" id="source-count"></span><button class="primary" id="add-source">＋ 添加搜索源</button></div>');
sources=replace(sources,'<form id="detect"','<p class="note">通过网址添加：粘贴网址 → 识别 → 确认添加。也可以切换「已适配目录」，直接点击条目右侧的「添加」。</p><form id="detect"');
sources=replace(sources,'<p id="detect-state" class="note" hidden></p>','<p id="detect-state" class="note" hidden></p><div id="candidate" class="statusbar" hidden><span>网址来源 E · 演示识别结果，未验证连接</span><button id="confirm-add" class="primary">添加到搜索源（演示）</button></div>');
sources=replace(sources,'<div class="dialog-head"><b>AFMC FIND · 设置</b>','<div class="dialog-head" style="gap:10px"><img class="logo" src="../../src/Afmc.Find.App/Assets/afmc-find.png" alt="AFMC FIND Logo"><b>AFMC FIND · 设置</b>');
sources=replace(sources,'opacity:.45;margin-left:auto','opacity:.8;margin-left:auto');
sources=replace(sources,'</script>',`$('#add-source').onclick=()=>{$('#website').focus();$('#website').scrollIntoView({block:'center'});};
$('#detect').onsubmit=e=>{e.preventDefault();$('#detect-state').hidden=false;$('#detect-state').textContent='以下仅演示添加流程，没有实际探测该网站。正式版必须验证搜索结果与下载链接后才能添加。';$('#candidate').hidden=false;};
$('#confirm-add').onclick=()=>{if(!sources.some(s=>s.name==='网址来源 E'))sources.push({name:'网址来源 E',type:'综合',access:'public',added:true,on:false,state:'演示 · 未验证连接'});$('#candidate').hidden=true;$('#added').click();toast('已添加到演示列表，未写入软件。');};
document.querySelector('[data-page="search"]').onclick=()=>location.href='index.html';
</script>`);
fs.writeFileSync(path.join(out,'sources.html'),sources);
console.log('Updated review preview: '+out);
