let currentChar = null;
// 切角色代次: 每次 selectCharacter 自增; 各异步加载在写 DOM 前校验代次,
// 防止慢返回的旧角色数据覆盖新角色视图(或旧行按钮打到新角色身上)
let selectEpoch = 0;

const $ = (sel) => document.querySelector(sel);

function toast(message, isError) {
  const el = $('#toast');
  el.textContent = message;
  el.className = 'toast' + (isError ? ' err' : '');
  clearTimeout(el._timer);
  el._timer = setTimeout(() => el.classList.add('hidden'), 3500);
}

async function api(path, options) {
  const response = await fetch(path, options);
  const data = await response.json();
  if (data && data.success === false) throw new Error(data.error || '操作失败');
  return data;
}

function post(path, body) {
  return api(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body || {}),
  });
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g,
    (ch) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
}

// 破坏性操作所在的表格: 切角色瞬间立即清空, 消灭"旧角色的行还可点"的窗口
const INTERACTIVE_TBODY_SELECTORS = [
  '#item-table tbody', '#quest-table tbody', '#main-quest-table tbody',
  '#achieve-quest-table tbody', '#cleared-table tbody',
];

async function loadStatus() {
  try {
    const status = await api('/api/status');
    const el = $('#status');
    if (status.indexError) {
      el.textContent = '物品索引构建失败: ' + status.indexError;
      el.className = 'status err';
    } else if (!status.indexReady) {
      el.textContent = 'DB 已连接 · 物品索引构建中…';
      el.className = 'status';
      setTimeout(loadStatus, 2000);
    } else {
      el.textContent = 'DB 已连接 · 物品索引就绪';
      el.className = 'status ok';
    }
  } catch (e) {
    $('#status').textContent = '后端无响应';
    $('#status').className = 'status err';
  }
}
