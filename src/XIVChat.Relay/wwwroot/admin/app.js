'use strict';
const $ = id => document.getElementById(id);
const zh = {
 welcomeTitle:'让聊天，\n跨过网络的距离。',welcomeText:'把游戏电脑与桌面客户端连接在一起。\n所有设备与授权，在这里轻松管理。',privacy:'端到端加密 · 不保存聊天内容',loginTitle:'欢迎回来',loginHint:'使用部署时设置的管理密码登录。',password:'管理密码',login:'登录后台',loginFoot:'登录最长有效 8 小时。忘记密码时，在部署配置中修改管理密码并重启服务。',disabledHelp:'后台尚未启用。请在部署的 .env 中设置 RELAY_ADMIN_PASSWORD（至少 16 位），填写 RELAY_DOMAIN，然后重新创建中继服务。',console:'中继管理',workspace:'工作空间',overview:'总览',devices:'游戏设备',clients:'客户端授权',help:'连接指南',privateRelay:'你的私有中继',privacyShort:'只转发加密数据，不保存聊天和截图。',logout:'退出登录',refresh:'刷新',onlineDevices:'在线游戏设备',authorizedClients:'有效客户端授权',activeConnections:'当前连接',clientLimit:'每台设备最多 32 个客户端',quickTitle:'连接，从一台游戏设备开始',quickText:'添加设备 → 在游戏插件中启用中继 → 生成邀请配对桌面端',addDevice:'＋ 添加游戏设备',liveTitle:'实时连接',liveHint:'每 15 秒更新一次，仅显示连接状态。',serverAddress:'中继服务地址',copy:'复制',showRevoked:'显示已撤销',searchDevices:'搜索设备名称或 ID…',searchClients:'搜索客户端或所属设备…',clientList:'已配对的桌面客户端',clientListHint:'每个客户端拥有独立授权，可单独撤销。',step1Title:'添加游戏设备',step1Text:'在“游戏设备”中点击添加，给游戏电脑起个名字。复制服务地址和只显示一次的注册凭据。',step2Title:'让插件上线',step2Text:'打开游戏内 XIVChat 插件的中继设置，填入服务地址与注册凭据，保存并启用。回到后台查看设备是否在线。',step3Title:'邀请桌面客户端',step3Text:'在线设备点击“生成邀请”，把邀请粘贴到桌面端的自建中继连接中。与游戏插件核对完整指纹后确认配对，再完成首次设备信任。',rememberTitle:'连接前，记住这几件事',rememberText:'邀请有效期为 10 分钟，只能兑换一次；生成新邀请会使旧邀请失效。撤销授权无法恢复，活动连接通常在 2 秒内关闭。更换电脑或端点证书时，请新建设备并重新配对。',passwordHelp:'管理密码在部署配置中更改，重启中继后生效，所有后台会话随之失效。游戏设备和客户端凭据继续保留。',footer:'XIVChat · 私有连接，自在聊天',subOverview:'查看中继运行状态，管理每一次连接。',subDevices:'为每台游戏电脑分配独立凭据，邀请桌面客户端加入。',subClients:'查看客户端所属设备，管理访问权限。',subHelp:'完成这三个步骤，即可通过中继连接。',healthy:'服务正常',stale:'状态待刷新',loading:'正在连接',registered:'已添加 {n} 台有效设备',capacity:'全实例上限 {n} 个会话',updated:'更新于 {time}',online:'在线',offline:'离线',revoked:'已撤销',unregistered:'等待插件上线',connecting:'连接中',authorized:'已授权',noConnections:'还没有正在使用的连接',noConnectionsHint:'游戏插件上线并配对桌面客户端后，连接会出现在这里。',noDevices:'添加你的第一台游戏设备',noDevicesHint:'给游戏电脑起个名字，获取它专属的中继注册凭据。',noClients:'还没有已配对的客户端',noClientsHint:'在在线游戏设备上生成邀请，然后到桌面客户端完成配对。',noMatches:'没有匹配的记录',noMatchesHint:'试试其他关键词，或勾选“显示已撤销”。',deviceName:'设备名称',namePlaceholder:'例如：我的游戏电脑',nameHint:'名称仅用于识别设备，最多 100 个字符。',cancel:'取消',create:'创建设备',save:'保存',close:'关闭',done:'已复制，完成',creating:'正在处理…',rename:'重命名',invite:'生成邀请',revoke:'撤销授权',deviceCreated:'设备已创建',credential:'注册凭据（仅显示这一次）',credentialWarning:'请先复制并保存凭据，再关闭窗口。后台无法再次显示原始凭据；如果遗失，请撤销该设备并重新添加。',credentialHint:'在游戏插件的中继设置中填入以下地址与凭据，保存并启用中继。',inviteTitle:'邀请桌面客户端',inviteWarning:'邀请有效期 10 分钟，仅能使用一次。新邀请会使上一份未使用邀请失效。',inviteConfirm:'为“{name}”生成新的配对邀请？',inviteReady:'一次性邀请已生成',invitation:'配对邀请',fingerprint:'端点证书指纹',fingerprintHint:'请通过游戏插件核对完整指纹。中继后台显示的指纹不能替代独立核对。',expires:'有效期至 {time}',inviteExpired:'邀请已过期，请关闭窗口并重新生成。',revokeDeviceTitle:'撤销游戏设备？',revokeDeviceHint:'“{name}”及其全部客户端将失去中继访问权限，活动连接通常在 2 秒内关闭。此操作无法恢复；需要重新添加设备并配对。',revokeClientTitle:'撤销客户端授权？',revokeClientHint:'“{name}”将失去访问权限，其他客户端不受影响。活动连接通常在 2 秒内关闭。若需再次连接，请重新生成邀请配对。',revokedNotice:'授权已撤销',renamedNotice:'设备名称已更新',copied:'已复制到剪贴板',copyFallback:'无法自动复制，内容已选中，请手动复制。',clientsCount:'有效客户端：{n} / 32',deviceColumn:'游戏设备',clientColumn:'客户端',stateColumn:'状态',actionColumn:'操作',network_error:'暂时无法连接中继，请检查网络后重试。',admin_not_configured:'管理后台尚未启用。',wrong_origin:'请使用部署配置中的中继服务地址打开后台。',login_required:'登录已失效，请重新登录。',invalid_password:'管理密码不正确。',login_rate_limited:'登录尝试过于频繁，请 1 分钟后再试。',session_limit:'后台登录数量达到上限，请稍后重试。',csrf_invalid:'会话校验失效，请退出后重新登录。',invalid_name:'请输入 1–100 个字符的设备名称，不能含控制字符。',device_unavailable:'设备不存在或已撤销，请刷新列表。',client_unavailable:'客户端不存在或已撤销，请刷新列表。',device_offline:'设备未上线或已撤销。请先启用游戏插件的中继连接。',request_failed:'操作未完成，请刷新页面后重试。',rate_limited:'请求过于频繁，请稍后再试。',syncFailed:'无法刷新，以下为上次获取的状态。',sessionEnded:'登录已失效，请重新登录。'
};
const en = {
 welcomeTitle:'Keep your conversations\nwithin reach.',welcomeText:'Connect your game PC and desktop clients.\nManage every device and permission in one place.',privacy:'End-to-end encryption · No chat storage',loginTitle:'Welcome back',loginHint:'Sign in with the password set during deployment.',password:'Management password',login:'Sign in',loginFoot:'Sessions last up to 8 hours. To reset your password, update the deployment configuration and restart the relay.',disabledHelp:'Administration is disabled. Set RELAY_ADMIN_PASSWORD (at least 16 characters) and RELAY_DOMAIN in the deployment .env, then recreate the relay service.',console:'Relay console',workspace:'WORKSPACE',overview:'Overview',devices:'Game devices',clients:'Client access',help:'Connection guide',privateRelay:'Your private relay',privacyShort:'Forwards encrypted data. Never stores chat or screenshots.',logout:'Sign out',refresh:'Refresh',onlineDevices:'Game devices online',authorizedClients:'Authorized clients',activeConnections:'Active connections',clientLimit:'Up to 32 clients per game device',quickTitle:'Start with your game PC',quickText:'Add a device → Enable relay in the plugin → Invite a desktop client',addDevice:'＋ Add game device',liveTitle:'Live connections',liveHint:'Refreshes every 15 seconds. Connection status only.',serverAddress:'Relay address',copy:'Copy',showRevoked:'Show revoked',searchDevices:'Search device name or ID…',searchClients:'Search clients or game devices…',clientList:'Paired desktop clients',clientListHint:'Each client has independent access that can be revoked.',step1Title:'Add your game PC',step1Text:'Add a device on the Game devices page. Give it a name and copy the relay address and registration credential shown once.',step2Title:'Connect the plugin',step2Text:'Open relay settings in the XIVChat game plugin. Enter the address and credential, save and enable the relay. Check that the device is online here.',step3Title:'Invite a desktop client',step3Text:'Generate an invitation on an online device and paste it into a self-hosted relay connection in the desktop app. Verify the complete fingerprint against the game plugin, then finish pairing and initial device trust.',rememberTitle:'Before you connect',rememberText:'Invitations last 10 minutes and can be redeemed once. A new invitation replaces the previous one. Revocation cannot be undone; active connections usually close within 2 seconds. Add a new device and pair again when moving to a new PC or changing the endpoint certificate.',passwordHelp:'Change the management password in the deployment configuration and restart the relay. All management sessions will expire. Game device and client credentials are retained.',footer:'XIVChat · Stay connected, chat freely',subOverview:'Monitor your relay and manage every connection.',subDevices:'Give each game PC its own credential and invite desktop clients.',subClients:'See which game device each client can access.',subHelp:'Follow these three steps to connect through your relay.',healthy:'Service healthy',stale:'Refresh needed',loading:'Connecting',registered:'{n} active devices registered',capacity:'Instance capacity: {n} sessions',updated:'Updated {time}',online:'Online',offline:'Offline',revoked:'Revoked',unregistered:'Waiting for plugin',connecting:'Connecting',authorized:'Authorized',noConnections:'No active connections yet',noConnectionsHint:'Connections appear here when a paired desktop client connects to an online game plugin.',noDevices:'Add your first game device',noDevicesHint:'Name your game PC and get its unique relay registration credential.',noClients:'No paired clients yet',noClientsHint:'Generate an invitation from an online game device, then pair it in the desktop app.',noMatches:'No matching records',noMatchesHint:'Try another search or enable “Show revoked”.',deviceName:'Device name',namePlaceholder:'e.g. My game PC',nameHint:'A label to help identify this device. Up to 100 characters.',cancel:'Cancel',create:'Create device',save:'Save',close:'Close',done:'Copied, done',creating:'Working…',rename:'Rename',invite:'Create invitation',revoke:'Revoke access',deviceCreated:'Device created',credential:'Registration credential (shown once)',credentialWarning:'Copy and save this credential before closing. It cannot be displayed again. If lost, revoke this device and add a new one.',credentialHint:'Enter this address and credential in the game plugin’s relay settings, then save and enable the relay.',inviteTitle:'Invite a desktop client',inviteWarning:'Valid for 10 minutes and one use only. Creating a new invitation invalidates the previous unused invitation.',inviteConfirm:'Create a new pairing invitation for “{name}”?',inviteReady:'Invitation ready',invitation:'Pairing invitation',fingerprint:'Endpoint certificate fingerprint',fingerprintHint:'Verify the complete fingerprint directly against the game plugin. This console does not replace independent verification.',expires:'Expires at {time}',inviteExpired:'This invitation has expired. Close this window and create a new one.',revokeDeviceTitle:'Revoke this game device?',revokeDeviceHint:'“{name}” and ALL its clients will lose relay access. Active connections usually close within 2 seconds. This cannot be undone; add a new device and pair again to reconnect.',revokeClientTitle:'Revoke this client?',revokeClientHint:'“{name}” will lose access. Other clients are unaffected. Active connections usually close within 2 seconds. Create a new invitation to pair this client again.',revokedNotice:'Access revoked',renamedNotice:'Device renamed',copied:'Copied to clipboard',copyFallback:'Automatic copy failed. The text is selected; please copy it manually.',clientsCount:'Authorized clients: {n} / 32',deviceColumn:'Game device',clientColumn:'Client',stateColumn:'Status',actionColumn:'Action',network_error:'Cannot reach the relay. Check your connection and try again.',admin_not_configured:'Administration has not been enabled.',wrong_origin:'Open this console at the relay address set in the deployment configuration.',login_required:'Your session expired. Please sign in again.',invalid_password:'Incorrect management password.',login_rate_limited:'Too many login attempts. Try again in one minute.',session_limit:'Too many management sessions. Please try again later.',csrf_invalid:'Session verification failed. Sign out and sign in again.',invalid_name:'Enter a device name with 1–100 printable characters.',device_unavailable:'Device not found or revoked. Refresh the list.',client_unavailable:'Client not found or revoked. Refresh the list.',device_offline:'This device is offline or revoked. Enable the relay connection in the game plugin first.',request_failed:'The operation did not complete. Refresh and try again.',rate_limited:'Too many requests. Please try again shortly.',syncFailed:'Refresh failed. The last known state is shown below.',sessionEnded:'Your session expired. Please sign in again.'
};
let lang = 'zh'; try { lang = localStorage.getItem('relay-language') === 'en' ? 'en' : 'zh'; } catch {}
let csrf = '', data = null, view = 'overview', authenticated = false, refreshing = false, busy = false, noticeTimer, expiryTimer, lastFingerprint = '', serviceState = 'loading';
const t = (key, values = {}) => Object.entries(values).reduce((s, [k,v]) => s.replaceAll('{'+k+'}',String(v)), (lang === 'en' ? en : zh)[key] || key);
const el = (tag, text, cls) => { const n = document.createElement(tag); if(text !== undefined)n.textContent = text; if(cls)n.className = cls; return n; };
const btn = (text, action, cls = 'secondary') => { const b = el('button', text, cls); b.type = 'button'; b.addEventListener('click', action); return b; };
const badge = (key, cls = 'neutral') => el('span',t(key),'badge '+cls);
const toast = text => { $('notice').textContent = text; $('notice').hidden = false; clearTimeout(noticeTimer); noticeTimer = setTimeout(() => $('notice').hidden = true, 4500); };
function translate() {
 document.documentElement.lang = lang === 'en' ? 'en' : 'zh-CN'; document.title = 'XIVChat · '+t('console');
 document.querySelectorAll('[data-i18n]').forEach(n => n.textContent = t(n.dataset.i18n));
 document.querySelectorAll('[data-placeholder]').forEach(n => { n.placeholder = t(n.dataset.placeholder); n.setAttribute('aria-label',t(n.dataset.placeholder)); });
 document.querySelectorAll('.language').forEach(n => n.textContent = lang === 'en' ? '简体中文' : 'English');
 $('dialog-close').setAttribute('aria-label',t('close')); selectView(view); if(data)render();
 $('service-status').textContent = t(serviceState);
 if(data)$('last-updated').textContent = t('updated',{time:new Date(data.serverTime).toLocaleTimeString(lang === 'en'?'en-GB':'zh-CN')});
}
async function api(path, body) {
 let response;
 try { response = await fetch('/admin/api/'+path, { method:body === undefined?'GET':'POST', credentials:'same-origin', cache:'no-store', headers:body === undefined?{}:{'Content-Type':'application/json','X-XIVChat-CSRF':csrf}, body:body === undefined?undefined:JSON.stringify(body), signal:AbortSignal.timeout(12000) }); }
 catch { throw new Error(t('network_error')); }
 const result = await response.json().catch(() => ({}));
 if(!response.ok) {
  if(response.status === 401 && path !== 'login')signOutLocal(true);
  throw new Error(t(result.error || (response.status === 429?'rate_limited':'request_failed')));
 }
 return result;
}
function signOutLocal(expired = false) {
 authenticated = false; csrf = ''; data = null; lastFingerprint = ''; closeDialog(true);
 $('console-view').hidden = true; $('login-view').hidden = false; $('password').value = '';
 $('login-error').textContent = expired?t('sessionEnded'):'';
 ['device-list','client-list','connection-list'].forEach(id => $(id).replaceChildren());
}
async function loadSession() {
 try {
  const session = await api('session');
  if(session.publicUrl !== location.origin)throw new Error(t('wrong_origin'));
  $('password').disabled = false; $('login-form').querySelector('button[type=submit]').disabled = false;
  if(session.authenticated) { csrf = session.csrf; await enter(); }
 } catch(error) {
  $('login-error').textContent = error.message;
  if(error.message === t('admin_not_configured')) { $('disabled-help').hidden = false; $('login-form').querySelector('button[type=submit]').disabled = true; $('password').disabled = true; }
 }
}
async function enter() {
 authenticated = true; $('login-view').hidden = true; $('console-view').hidden = false; $('password').value = ''; await refresh();
}
async function refresh() {
 if(refreshing || !authenticated)return;
 refreshing = true; $('refresh').disabled = true;
 try {
  data = await api('dashboard'); $('sync-error').hidden = true;
  serviceState = 'healthy'; $('service-status').className = 'badge good'; $('service-status').textContent = t(serviceState);
  const fingerprint = JSON.stringify([data.devices,data.clients,data.live]);
  if(fingerprint !== lastFingerprint) { render(); lastFingerprint = fingerprint; }
  $('last-updated').textContent = t('updated',{time:new Date(data.serverTime).toLocaleTimeString(lang === 'en'?'en-GB':'zh-CN')});
 } catch(error) {
  if(authenticated){ serviceState = 'stale'; $('sync-error').textContent = t('syncFailed')+' '+error.message; $('sync-error').hidden = false; $('service-status').textContent = t(serviceState); $('service-status').className = 'badge warning'; }
 } finally { refreshing = false; $('refresh').disabled = false; }
}
function selectView(next) {
 view = next;
 document.querySelectorAll('[data-view]').forEach(n => { n.classList.toggle('active',n.dataset.view === view); if(n.dataset.view === view)n.setAttribute('aria-current','page');else n.removeAttribute('aria-current'); });
 document.querySelectorAll('.view').forEach(n => n.hidden = n.id !== view+'-view');
 $('page-title').textContent = t(view); $('page-subtitle').textContent = t('sub'+view[0].toUpperCase()+view.slice(1));
}
function empty(target, title, hint, action) {
 const box = el('div',undefined,'empty'); box.append(el('span','◇','empty-symbol'),el('strong',t(title)),el('p',t(hint))); if(action)box.append(btn(t('addDevice'),createDevice,'primary')); target.append(box);
}
function table(target, headings, rows) {
 const wrap = el('div',undefined,'table-wrap'), tab = el('table'), head = el('thead'), tr = el('tr'), body = el('tbody');
 headings.forEach(h => {const th = el('th',t(h));th.scope = 'col';tr.append(th);}); head.append(tr);
 rows.forEach(cells => { const row = el('tr');cells.forEach(c => { const td = el('td');td.append(c instanceof Node?c:document.createTextNode(c));row.append(td); });body.append(row); });
 tab.append(head,body);wrap.append(tab);target.append(wrap);
}
function identity(name,id) { const n = el('div',name);n.append(el('small',id));return n; }
function render() {
 if(!data)return;
 $('stat-online').textContent = data.live.onlineDevices.length;
 $('stat-devices').textContent = t('registered',{n:data.devices.filter(d=>!d.revoked).length});
 $('stat-clients').textContent = data.clients.filter(c=>!c.revoked).length;
 $('stat-connections').textContent = data.live.connections.length;
 $('stat-limit').textContent = t('capacity',{n:data.live.maxSessions});
 $('server-address').textContent = data.publicUrl; $('footer-address').textContent = data.publicUrl;
 $('connection-list').replaceChildren();
 if(!data.live.connections.length)empty($('connection-list'),'noConnections','noConnectionsHint');
 else table($('connection-list'),['clientColumn','deviceColumn','stateColumn'],data.live.connections.map(c=>[identity(data.clients.find(x=>x.id===c.clientId)?.name||c.clientId,c.clientId),data.devices.find(x=>x.id===c.deviceId)?.name||c.deviceId,badge(c.ready?'online':'connecting',c.ready?'good':'warning')]));
 renderDevices(); renderClients();
}
function renderDevices() {
 if(!data)return;
 const target = $('device-list'); target.replaceChildren();
 const q = $('device-search').value.trim().toLowerCase(), show = $('show-revoked-devices').checked;
 const devices = data.devices.filter(d => (show||!d.revoked) && (d.name+' '+d.id).toLowerCase().includes(q));
 if(!devices.length){ empty(target,q||show?'noMatches':'noDevices',q||show?'noMatchesHint':'noDevicesHint',!q&&!show);return; }
 devices.forEach(d => {
  const online = data.live.onlineDevices.includes(d.id), card = el('article',undefined,'device-card'+(d.revoked?' revoked':'')), top = el('div',undefined,'device-top'), name = el('div',undefined,'device-name');
  name.append(el('h2',d.name),el('code',d.id));top.append(el('div','▣','device-icon'),name,badge(d.revoked?'revoked':online?'online':d.fingerprint?'offline':'unregistered',d.revoked?'neutral':online?'good':'neutral'));
  const details = el('div',undefined,'device-detail'); details.append(el('p',t('clientsCount',{n:data.clients.filter(c=>c.deviceId===d.id&&!c.revoked).length})));
  if(d.fingerprint){details.append(el('p',t('fingerprint')),el('code',d.fingerprint,'fingerprint'));}
  else details.append(el('p',t('step2Text')));
  card.append(top,details);
  if(!d.revoked){
   const actions = el('div',undefined,'device-actions'), invite = btn(t('invite'),()=>createInvitation(d),'secondary');invite.disabled = !online; if(!online)invite.title = t('device_offline');
   actions.append(invite,btn(t('rename'),()=>renameDevice(d),'ghost'),btn(t('revoke'),()=>revoke('device',d),'ghost danger-link'));card.append(actions);
  }
  target.append(card);
 });
}
function renderClients() {
 if(!data)return;
 const target = $('client-list');target.replaceChildren();const q = $('client-search').value.trim().toLowerCase(),show = $('show-revoked-clients').checked;
 const clients = data.clients.filter(c => (show||!c.revoked) && (c.name+' '+c.id+' '+(data.devices.find(d=>d.id===c.deviceId)?.name||'')).toLowerCase().includes(q));
 if(!clients.length){ empty(target,q||show?'noMatches':'noClients',q||show?'noMatchesHint':'noClientsHint');return; }
 table(target,['clientColumn','deviceColumn','stateColumn','actionColumn'],clients.map(c => {
  const live = data.live.connections.find(s=>s.clientId===c.id);
  return [identity(c.name,c.id),data.devices.find(d=>d.id===c.deviceId)?.name||c.deviceId,badge(c.revoked?'revoked':live?(live.ready?'online':'connecting'):'authorized',c.revoked?'neutral':live?'good':'neutral'),c.revoked?'—':btn(t('revoke'),()=>revoke('client',c),'ghost danger-link')];
 }));
}
function closeDialog(force = false) {
 if(busy && !force)return;
 clearTimeout(expiryTimer); $('dialog').close(); $('dialog-body').replaceChildren(); $('dialog-actions').replaceChildren(); $('dialog-error').textContent = '';
}
function dialog(title) {
 closeDialog(); $('dialog-title').textContent = t(title); $('dialog').showModal(); return $('dialog-body');
}
function actionButtons(label, action, danger = false) {
 $('dialog-actions').replaceChildren(btn(t('cancel'),()=>closeDialog(),'ghost'),btn(t(label),()=>perform(action),danger?'danger':'primary'));
}
async function perform(action) {
 if(busy)return; busy = true; $('dialog-error').textContent = '';
 const buttons = [...$('dialog').querySelectorAll('button')];buttons.forEach(b=>b.disabled = true);
 try { await action(); } catch(error) { $('dialog-error').textContent = error.message; }
 finally { busy = false; buttons.forEach(b=>b.disabled = false); }
}
function nameField(body, value = '') {
 const label = el('label',t('deviceName')), input = el('input');input.id = 'edit-name';input.maxLength = 100;input.value = value;input.placeholder = t('namePlaceholder');input.autocomplete = 'off';label.htmlFor = input.id;
 body.append(label,input,el('p',t('nameHint'))); input.focus();return input;
}
function createDevice() {
 const body = dialog('addDevice'), input = nameField(body);
 const submit = async () => {
  if(!input.value.trim())throw new Error(t('invalid_name'));
  const result = await api('devices',{name:input.value.trim()});
  $('dialog-title').textContent = t('deviceCreated');body.replaceChildren(el('p',t('credentialHint')));
  secretField(body,'serverAddress',result.publicUrl);secretField(body,'credential',result.credential);
  body.append(el('p',t('credentialWarning'),'warning-note'));
  $('dialog-actions').replaceChildren(btn(t('done'),()=>closeDialog(),'primary'));await refresh();
 };
 actionButtons('create',submit);input.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();perform(submit);}});
}
function renameDevice(device) {
 const body = dialog('rename'), input = nameField(body,device.name);
 actionButtons('save',async()=>{await api('devices/'+device.id+'/rename',{name:input.value.trim()});closeDialog(true);toast(t('renamedNotice'));await refresh();});
}
function secretField(body, key, value) {
 const label = el('label',t(key)), row = el('div',undefined,'secret-row'), input = el('textarea'); input.readOnly = true; input.value = value;input.id = 'secret-'+key;label.htmlFor = input.id;
 row.append(input,btn(t('copy'),()=>copyText(value,input),'secondary'));body.append(label,row);
}
async function copyText(value, input) {
 try { await navigator.clipboard.writeText(value);toast(t('copied')); }
 catch {
  if(input){input.focus();input.select();toast(t('copyFallback'));}
  else {const body = dialog('serverAddress');secretField(body,'serverAddress',value);$('dialog-actions').append(btn(t('close'),()=>closeDialog(),'primary'));}
 }
}
function createInvitation(device) {
 const body = dialog('inviteTitle');body.append(el('p',t('inviteConfirm',{name:device.name})),el('p',t('inviteWarning'),'warning-note'));
 actionButtons('invite',async()=>{
  const result = await api('devices/'+device.id+'/invitation',{});$('dialog-title').textContent = t('inviteReady');body.replaceChildren();
  secretField(body,'invitation',result.invitation);body.append(el('p',t('expires',{time:new Date(result.expiresAt).toLocaleString(lang==='en'?'en-GB':'zh-CN')}),'warning-note'));
  const label = el('label',t('fingerprint'));body.append(label,el('code',result.fingerprint,'fingerprint'),el('p',t('fingerprintHint')));
  $('dialog-actions').replaceChildren(btn(t('close'),()=>closeDialog(),'primary'));
  expiryTimer = setTimeout(()=>{ if($('dialog').open){body.replaceChildren(el('p',t('inviteExpired'),'warning-note'));} },Math.max(0,new Date(result.expiresAt).getTime()-Date.now()));
 });
}
function revoke(kind,item) {
 const body = dialog(kind==='device'?'revokeDeviceTitle':'revokeClientTitle');body.append(el('p',t(kind==='device'?'revokeDeviceHint':'revokeClientHint',{name:item.name}),'warning-note'));
 actionButtons('revoke',async()=>{await api((kind==='device'?'devices/':'clients/')+item.id+'/revoke',{});closeDialog(true);toast(t('revokedNotice'));await refresh();},true);
}
$('login-form').addEventListener('submit',async e=>{
 e.preventDefault();const button = e.currentTarget.querySelector('button[type=submit]');button.disabled = true;$('login-error').textContent = '';
 try {const result = await api('login',{password:$('password').value});csrf = result.csrf;await enter();}
 catch(error){$('login-error').textContent = error.message;}
 finally{button.disabled = false;}
});
$('logout').addEventListener('click',async()=>{try{await api('logout',{});signOutLocal();}catch(error){toast(error.message);}});
$('refresh').addEventListener('click',refresh);
$('copy-address').addEventListener('click',()=>data&&copyText(data.publicUrl));
$('dialog-close').addEventListener('click',()=>closeDialog());
$('dialog').addEventListener('cancel',e=>{e.preventDefault();closeDialog();});
document.querySelectorAll('[data-view]').forEach(n=>n.addEventListener('click',()=>selectView(n.dataset.view)));
document.querySelectorAll('.create-device').forEach(n=>n.addEventListener('click',createDevice));
document.querySelectorAll('.language').forEach(n=>n.addEventListener('click',()=>{lang=lang==='en'?'zh':'en';try{localStorage.setItem('relay-language',lang);}catch{}translate();}));
['device-search','show-revoked-devices'].forEach(id=>$(id).addEventListener('input',renderDevices));
['client-search','show-revoked-clients'].forEach(id=>$(id).addEventListener('input',renderClients));
document.addEventListener('visibilitychange',()=>{if(!document.hidden)refresh();});
setInterval(()=>{if(!document.hidden&&!$('dialog').open)refresh();},15000);
translate();$('service-status').textContent = t('loading');loadSession();
