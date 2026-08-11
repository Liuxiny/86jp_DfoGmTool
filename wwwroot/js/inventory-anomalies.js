// ---- 新版背包异常物品维护 ----
// 这里只负责状态读取、渲染和操作流程；按钮事件统一由 bindings.js 绑定。

let inventoryAnomalyStatus = null;
let inventoryAnomalyError = '';
let inventoryAnomalyBusy = false;
let inventoryAnomalyRequestSerial = 0;

const INVENTORY_ANOMALY_REASON_LABELS = {
  item_core_null_or_invalid_length: 'ItemCore 长度无效',
  item_core_decode_failed: 'ItemCore 解码失败',
  item_id_non_positive: '物品 ID 非正数',
  item_id_not_in_pvf: '物品 ID 不在当前 PVF 合法集合',
};

function inventoryAnomalyRuntimeAllows() {
  return Boolean(runtimeReady
    && runtimeStatus
    && runtimeStatus.ready
    && (!runtimeStatus.authenticationRequired || runtimeStatus.authenticated));
}

function resetInventoryAnomalyState() {
  inventoryAnomalyRequestSerial++;
  inventoryAnomalyStatus = null;
  inventoryAnomalyError = '';
  inventoryAnomalyBusy = false;
  renderInventoryAnomalyStatus(null);
  updateInventoryAnomalyShortcut(null);
}

function updateInventoryAnomalyShortcut(status) {
  const shortcut = $('#btn-inventory-anomalies');
  if (!shortcut) return;
  const visible = inventoryAnomalyRuntimeAllows()
    && status?.success === true
    && status?.hasAnomalies === true;
  shortcut.classList.toggle('hidden', !visible);
  shortcut.disabled = inventoryAnomalyBusy;
}

function inventoryAnomalyNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? Math.floor(number) : 0;
}

function inventoryAnomalySourceLabel(value) {
  return value === 'accountCargo' ? '账号金库' : '角色库存';
}

function inventoryAnomalyReasonLabel(value) {
  return INVENTORY_ANOMALY_REASON_LABELS[value] || String(value || '未知原因');
}

function renderInventoryAnomalyDetails(details) {
  const box = $('#inventory-anomaly-details');
  if (!box) return;
  const rows = Array.isArray(details) ? details : [];
  if (rows.length === 0) {
    box.innerHTML = '<p class="hint">当前 PVF 下未发现异常物品。</p>';
    return;
  }

  box.innerHTML = '<div class="table-scroll"><table class="inventory-anomaly-table">' +
    '<thead><tr><th>来源</th><th>账号 ID</th><th>角色</th><th>容器 / listType</th>' +
    '<th>槽位</th><th>物品 ID</th><th>物品 UID</th><th>原因</th></tr></thead><tbody>' +
    rows.map((detail) => {
      const characterId = inventoryAnomalyNumber(detail?.characterId);
      const characterName = detail?.characterName
        ? String(detail.characterName)
        : (characterId > 0 ? `角色 ${characterId}` : '（无角色）');
      const character = characterId > 0
        ? `${characterName}（#${characterId}）`
        : characterName;
      const source = inventoryAnomalySourceLabel(detail?.source);
      const listType = inventoryAnomalyNumber(detail?.listType);
      return `<tr><td>${escapeHtml(source)}</td>` +
        `<td>${escapeHtml(inventoryAnomalyNumber(detail?.accountId))}</td>` +
        `<td>${escapeHtml(character)}</td>` +
        `<td>${escapeHtml(detail?.container || '未知容器')} / ${escapeHtml(listType)}</td>` +
        `<td>${escapeHtml(inventoryAnomalyNumber(detail?.slot))}</td>` +
        `<td>${escapeHtml(detail?.itemId ?? '')}</td>` +
        `<td>${escapeHtml(detail?.itemUid ?? '')}</td>` +
        `<td>${escapeHtml(inventoryAnomalyReasonLabel(detail?.reason))}</td></tr>`;
    }).join('') + '</tbody></table></div>';
}

function renderInventoryAnomalyStatus(status) {
  const summary = $('#inventory-anomaly-status');
  const progress = $('#inventory-anomaly-progress');
  const refresh = $('#btn-refresh-inventory-anomalies');
  const clean = $('#btn-clean-inventory-anomalies');
  if (!summary || !progress || !clean) return;

  if (!status || status.success !== true) {
    summary.textContent = inventoryAnomalyError
      ? `异常物品状态读取失败：${inventoryAnomalyError}`
      : '尚未读取异常物品状态。';
    summary.className = inventoryAnomalyError ? 'migration-status err' : 'migration-status';
    const details = $('#inventory-anomaly-details');
    if (details) {
      details.innerHTML = inventoryAnomalyError
        ? '<p class="err">状态未知，未能确认当前是否存在异常物品。</p>'
        : '<p class="hint">请点击“刷新”读取当前 PVF 下的异常物品状态。</p>';
    }
    clean.disabled = true;
    if (refresh) refresh.disabled = inventoryAnomalyBusy;
    updateInventoryAnomalyShortcut(null);
    return;
  }

  const total = inventoryAnomalyNumber(status.totalCount);
  const characterCount = inventoryAnomalyNumber(status.characterCount);
  const accountCargoCount = inventoryAnomalyNumber(status.accountCargoCount);
  summary.className = 'migration-status';
  summary.innerHTML = `<div><b>当前异常物品：</b>${total} 件` +
    `（角色库存 ${characterCount}、账号金库 ${accountCargoCount}）</div>` +
    (status.running ? '<div class="err"><b>清理事务正在执行，请等待。</b></div>' : '') +
    (status.statusRefreshError
      ? `<div class="err">清理已提交，但状态刷新有警告：${escapeHtml(status.statusRefreshError)}</div>`
      : '') +
    (inventoryAnomalyError
      ? `<div class="err">最近一次状态刷新失败：${escapeHtml(inventoryAnomalyError)}</div>`
      : '');
  if (!inventoryAnomalyBusy && status.running)
    progress.textContent = '正在读取清理状态…';
  renderInventoryAnomalyDetails(status.details);
  clean.disabled = inventoryAnomalyBusy || status.running || total <= 0 || Boolean(inventoryAnomalyError);
  if (refresh) refresh.disabled = inventoryAnomalyBusy;
  updateInventoryAnomalyShortcut(status);
}

async function refreshInventoryAnomalyStatus(expectedRuntimeEpoch = runtimeSourceEpoch) {
  const epoch = Number.isInteger(expectedRuntimeEpoch) ? expectedRuntimeEpoch : runtimeSourceEpoch;
  if (epoch !== runtimeSourceEpoch || !inventoryAnomalyRuntimeAllows()) return null;
  const request = ++inventoryAnomalyRequestSerial;
  try {
    const status = await api('/api/inventory-anomalies/status');
    if (epoch !== runtimeSourceEpoch || request !== inventoryAnomalyRequestSerial) return null;
    if (!status || status.success !== true)
      throw new Error(status?.error || '异常物品状态读取失败');
    inventoryAnomalyStatus = status;
    inventoryAnomalyError = '';
    renderInventoryAnomalyStatus(status);
    return status;
  } catch (error) {
    if (epoch !== runtimeSourceEpoch || request !== inventoryAnomalyRequestSerial) return null;
    inventoryAnomalyError = error.message || String(error);
    // 保留上一次成功状态，避免失败响应里的 hasAnomalies=false 把快捷入口
    // 误隐藏；数据源重置时 resetInventoryAnomalyState 才会清空旧状态。
    renderInventoryAnomalyStatus(inventoryAnomalyStatus);
    return null;
  }
}

async function showInventoryAnomalyPanel() {
  if (!inventoryAnomalyRuntimeAllows()) {
    toast('运行时或 PVF 尚未就绪', true);
    return;
  }
  if (!parseInt($('#account-select').value, 10)) {
    toast('请先选择账号', true);
    return;
  }
  try {
    await showAccountPanel();
    if ($('#account-panel').classList.contains('hidden')) return;
    activateAccountTab('anomalies');
    await refreshInventoryAnomalyStatus();
  } catch (error) {
    inventoryAnomalyError = error.message || String(error);
    renderInventoryAnomalyStatus(inventoryAnomalyStatus);
    toast(inventoryAnomalyError, true);
  }
}

async function cleanInventoryAnomalies() {
  if (inventoryAnomalyBusy || !inventoryAnomalyRuntimeAllows()) return;
  const status = inventoryAnomalyStatus;
  const total = inventoryAnomalyNumber(status?.totalCount);
  if (!status || status.success !== true || status.running || total <= 0) return;
  if (!confirm('将按当前 PVF 对所有账号的新版背包与账号金库重新扫描并删除异常物品，不可撤销。' +
      `\n当前发现 ${total} 件异常物品，是否继续？`)) return;

  const epoch = runtimeSourceEpoch;
  inventoryAnomalyBusy = true;
  inventoryAnomalyError = '';
  const progress = $('#inventory-anomaly-progress');
  if (progress) {
    progress.className = 'hint';
    progress.textContent = '正在按当前 PVF 扫描并清理所有账号的新版库存，请等待…';
  }
  renderInventoryAnomalyStatus(status);
  try {
    const result = await post('/api/inventory-anomalies/clean');
    if (epoch !== runtimeSourceEpoch) return;
    if (!result || result.success !== true)
      throw new Error(result?.error || '异常物品清理失败');
    inventoryAnomalyStatus = result;
    inventoryAnomalyError = '';
    const deleted = inventoryAnomalyNumber(result.deletedCount);
    if (progress) {
      progress.className = result.statusRefreshError ? 'hint err' : 'hint';
      progress.textContent = result.statusRefreshError
        ? `清理已提交，共删除 ${deleted} 件；状态刷新有警告：${result.statusRefreshError}`
        : `清理已提交，共删除 ${deleted} 件。`;
    }
    renderInventoryAnomalyStatus(result);
    toast(result.statusRefreshError
      ? `异常物品清理已提交（${deleted} 件），但状态刷新有警告`
      : `异常物品清理完成，共删除 ${deleted} 件`);
    // 后端返回的是事务内快照；再读一次确认当前数据源的最新状态。
    await refreshInventoryAnomalyStatus(epoch);
  } catch (error) {
    if (epoch !== runtimeSourceEpoch) return;
    inventoryAnomalyError = error.message || String(error);
    if (progress) {
      progress.className = 'hint err';
      progress.textContent = `清理失败，未确认删除结果：${inventoryAnomalyError}`;
    }
    renderInventoryAnomalyStatus(inventoryAnomalyStatus);
    toast(`异常物品清理失败：${inventoryAnomalyError}`, true);
  } finally {
    if (epoch === runtimeSourceEpoch) {
      inventoryAnomalyBusy = false;
      renderInventoryAnomalyStatus(inventoryAnomalyStatus);
    }
  }
}

function bindInventoryAnomalies() {
  const shortcut = $('#btn-inventory-anomalies');
  const refresh = $('#btn-refresh-inventory-anomalies');
  const clean = $('#btn-clean-inventory-anomalies');
  const tab = document.querySelector('.acc-tab[data-acc-tab="anomalies"]');
  if (shortcut) shortcut.onclick = showInventoryAnomalyPanel;
  if (refresh) refresh.onclick = () => {
    if (!inventoryAnomalyBusy) refreshInventoryAnomalyStatus();
  };
  if (clean) clean.onclick = cleanInventoryAnomalies;
  if (tab) tab.addEventListener('click', () => {
    if (!inventoryAnomalyBusy) refreshInventoryAnomalyStatus();
  });
  renderInventoryAnomalyStatus(inventoryAnomalyStatus);
}
