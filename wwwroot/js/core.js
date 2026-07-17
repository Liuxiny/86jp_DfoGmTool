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
  if (data && data.success === false) {
    if (data.loginRequired === true && typeof handleAuthenticationRequired === 'function')
      handleAuthenticationRequired();
    throw new Error(data.error || '操作失败');
  }
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

const SERVER_TIME_ZONE = 'Asia/Shanghai';
const SERVER_UTC_OFFSET_SECONDS = 8 * 60 * 60;
const DAILY_DELETE_HOUR = 6;
const SERVER_DATE_FORMATTER = new Intl.DateTimeFormat('zh-CN', {
  timeZone: SERVER_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

function positiveEpochSeconds(value) {
  const seconds = Number(value);
  return Number.isFinite(seconds) && seconds > 0 ? Math.floor(seconds) : 0;
}

function positiveDays(value) {
  const days = Number(value);
  return Number.isFinite(days) && days > 0 ? Math.floor(days) : 0;
}

function formatServerTime(seconds) {
  const date = new Date(seconds * 1000);
  if (Number.isNaN(date.getTime())) return '';

  const parts = {};
  for (const part of SERVER_DATE_FORMATTER.formatToParts(date))
    if (part.type !== 'literal') parts[part.type] = part.value;
  return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}`;
}

function formatRemainingTime(seconds) {
  const remaining = Math.max(0, Math.floor(seconds));
  const days = Math.floor(remaining / 86400);
  const hours = Math.floor((remaining % 86400) / 3600);
  const minutes = Math.floor((remaining % 3600) / 60);
  if (days > 0) return `剩余 ${days} 天${hours > 0 ? ` ${hours} 小时` : ''}`;
  if (hours > 0) return `剩余 ${hours} 小时${minutes > 0 ? ` ${minutes} 分钟` : ''}`;
  return minutes > 0 ? `剩余 ${minutes} 分钟` : '不足 1 分钟';
}

function nextDailyDeleteTime(now = Math.floor(Date.now() / 1000)) {
  const serverDayStart = Math.floor((now + SERVER_UTC_OFFSET_SECONDS) / 86400) * 86400 - SERVER_UTC_OFFSET_SECONDS;
  const todayDeleteTime = serverDayStart + DAILY_DELETE_HOUR * 60 * 60;
  return now < todayDeleteTime ? todayDeleteTime : todayDeleteTime + 86400;
}

function templateExpirationState(item) {
  const expiration = item && item.templateExpiration;
  if (!expiration || expiration.known !== true)
    return { kind: 'unknown', primary: '期限未知', detail: 'PVF 索引尚未提供期限定义' };
  if (expiration.invalid === true)
    return { kind: 'warning', primary: '期限定义异常', detail: 'PVF 期限字段无法解析' };

  if (expiration.dailyDeleteItem === true)
    return { kind: 'daily', primary: '每日 06:00 清除', detail: 'PVF 每日删除' };

  const usablePeriodDays = positiveDays(expiration.usablePeriodDays);
  if (usablePeriodDays > 0)
    return { kind: 'relative', primary: `获得后 ${usablePeriodDays} 天`, detail: '相对期限' };

  const absoluteExpireTime = positiveEpochSeconds(expiration.absoluteExpireTime);
  if (absoluteExpireTime > 0) {
    const formatted = formatServerTime(absoluteExpireTime);
    return absoluteExpireTime <= Math.floor(Date.now() / 1000)
      ? { kind: 'expired', primary: '已过期', detail: `固定截止 ${formatted}` }
      : { kind: 'absolute', primary: `固定截止 ${formatted}`, detail: '绝对期限' };
  }

  return { kind: 'none', primary: '无期限', detail: 'PVF 未定义期限' };
}

function renderExpiration(state) {
  const detail = state.detail ? `<span class="expiry-detail">${escapeHtml(state.detail)}</span>` : '';
  const title = state.detail ? `${state.primary}，${state.detail}` : state.primary;
  return `<span class="expiry expiry-${state.kind}" title="${escapeHtml(title)}">` +
    `<span class="expiry-primary">${escapeHtml(state.primary)}</span>${detail}</span>`;
}

function templateExpirationLabel(item) {
  return renderExpiration(templateExpirationState(item));
}

function absoluteInventoryExpirationState(expireTime, source, now) {
  const formatted = formatServerTime(expireTime);
  if (expireTime <= now) {
    return {
      kind: 'expired',
      expiresAt: expireTime,
      primary: '已过期',
      detail: `${source}，截止 ${formatted}`,
    };
  }

  return {
    kind: 'active',
    expiresAt: expireTime,
    primary: `有效至 ${formatted}`,
    detail: `${source}，${formatRemainingTime(expireTime - now)}`,
  };
}

function supplementalExpirationSource(item) {
  const source = item && item.supplementalExpiration && item.supplementalExpiration.source;
  return source === 'rental' ? '租赁期限' : '附加期限';
}

function inventoryExpirationState(item) {
  const now = Math.floor(Date.now() / 1000);
  const templateState = templateExpirationState(item);
  const expireTime = positiveEpochSeconds(item && item.expireTime);

  if (expireTime > 0) {
    const source = templateState.kind === 'relative'
      ? templateState.primary
      : templateState.kind === 'absolute' ? '固定期限' : '实例期限';
    return absoluteInventoryExpirationState(expireTime, source, now);
  }

  const supplementalExpireTime = positiveEpochSeconds(item && item.supplementalExpiration && item.supplementalExpiration.expireTime);
  if (supplementalExpireTime > 0)
    return absoluteInventoryExpirationState(supplementalExpireTime, supplementalExpirationSource(item), now);

  if (templateState.kind === 'daily') {
    const expiresAt = nextDailyDeleteTime(now);
    return {
      kind: 'daily',
      expiresAt,
      primary: templateState.primary,
      detail: `下次 ${formatServerTime(expiresAt)}，${formatRemainingTime(expiresAt - now)}`,
    };
  }

  if (templateState.kind === 'none' || templateState.kind === 'unknown' || templateState.kind === 'warning')
    return templateState;

  if (templateState.kind === 'expired')
    return templateState;

  return {
    kind: 'warning',
    filter: 'missing',
    primary: '期限缺失',
    detail: templateState.primary,
  };
}

function inventoryExpirationLabel(item) {
  return renderExpiration(inventoryExpirationState(item));
}

function inventoryExpirationMatchesFilter(item, filter) {
  if (!filter || filter === 'all') return true;

  const state = inventoryExpirationState(item);
  switch (filter) {
    case 'active':
      return state.kind === 'active' || state.kind === 'daily';
    case 'daily':
      return state.kind === 'daily';
    case 'soon':
      return state.kind === 'daily'
        || (state.kind === 'active' && state.expiresAt - Math.floor(Date.now() / 1000) <= 7 * 86400);
    case 'expired':
      return state.kind === 'expired';
    case 'missing':
      return state.filter === 'missing';
    case 'none':
      return state.kind === 'none';
    default:
      return true;
  }
}

// 破坏性操作所在的表格: 切角色瞬间立即清空, 消灭"旧角色的行还可点"的窗口
const INTERACTIVE_TBODY_SELECTORS = [
  '#item-table tbody', '#quest-table tbody', '#all-quest-table tbody', '#main-quest-table tbody',
  '#achieve-quest-table tbody', '#cleared-table tbody',
];

function renderRuntimeStatus(status) {
  const el = $('#status');
  if (status && status.authenticationRequired && !status.authenticated) {
    el.textContent = '请先登录';
    el.className = 'status';
    return;
  }

  if (!status || !status.configured) {
    el.textContent = status && status.error ? '数据源不可用' : '等待选择数据源';
    el.className = 'status' + (status && status.error ? ' err' : '');
    return;
  }

  if (status.error || status.hasError) {
    el.textContent = 'PVF 加载失败';
    el.className = 'status err';
  } else if (!status.ready) {
    el.textContent = 'PVF 加载中…';
    el.className = 'status';
  } else {
    el.textContent = '数据源已就绪';
    el.className = 'status ok';
  }
}
