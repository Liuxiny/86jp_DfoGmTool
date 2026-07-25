let inventoryMigrationBusy = false;
let inventoryMigrationPendingDirection = null;
let inventoryMigrationStatus = null;

function setInventoryMigrationButtons() {
  const upgrade = $('#btn-migrate-legacy-new');
  const downgrade = $('#btn-migrate-new-legacy');
  if (!upgrade || !downgrade) return;
  upgrade.disabled = inventoryMigrationBusy || !inventoryMigrationStatus?.canUpgrade;
  downgrade.disabled = inventoryMigrationBusy || !inventoryMigrationStatus?.canDowngrade;
}

function updateInventoryMigrationShortcut(status) {
  const shortcut = $('#btn-inventory-migration');
  if (!shortcut) return;
  const runtimeAllows = runtimeStatus && runtimeStatus.ready
    && (!runtimeStatus.authenticationRequired || runtimeStatus.authenticated);
  shortcut.classList.toggle('hidden', !runtimeAllows || !status?.canUpgrade);
}

async function refreshInventoryMigrationShortcut(status) {
  try {
    const latest = status || await api('/api/inventory-migration/status');
    updateInventoryMigrationShortcut(latest);
  } catch (_) {
    updateInventoryMigrationShortcut(null);
  }
}

function renderInventoryMigrationStatus(status) {
  inventoryMigrationStatus = status;
  updateInventoryMigrationShortcut(status);
  const legacy = Number(status?.legacyItemCount || 0) + Number(status?.legacyEquippedCount || 0) + Number(status?.legacyAccountCargoCount || 0);
  const modern = Number(status?.newItemCount || 0) + Number(status?.newAccountCargoCount || 0);
  $('#inventory-migration-status').innerHTML =
    `<div><b>旧版数据：</b>${legacy} 条` +
    `（背包 ${Number(status?.legacyItemCount || 0)}、穿戴 ${Number(status?.legacyEquippedCount || 0)}、账号仓库 ${Number(status?.legacyAccountCargoCount || 0)}）</div>` +
    `<div><b>新版数据：</b>${modern} 条` +
    `（角色物品/名称装饰 ${Number(status?.newItemCount || 0)}、账号仓库 ${Number(status?.newAccountCargoCount || 0)}）</div>` +
    (status?.running ? '<div class="err"><b>迁移事务正在执行，请等待。</b></div>' : '');
  setInventoryMigrationButtons();
}

async function loadInventoryMigrationStatus() {
  const status = await api('/api/inventory-migration/status');
  renderInventoryMigrationStatus(status);
  return status;
}

async function showInventoryMigrationPanel() {
  await showAccountPanel();
  if ($('#account-panel').classList.contains('hidden')) return;
  activateAccountTab('migration');
  $('#inventory-migration-report').classList.add('hidden');
  $('#inventory-migration-progress').textContent = '';
  try {
    await loadInventoryMigrationStatus();
  } catch (error) {
    $('#inventory-migration-progress').textContent = error.message;
    $('#inventory-migration-progress').className = 'hint err';
  }
}

function showInventoryMigrationConfirm(direction) {
  if (inventoryMigrationBusy) return;
  const allowed = direction === 'legacy-to-new'
    ? inventoryMigrationStatus?.canUpgrade
    : inventoryMigrationStatus?.canDowngrade;
  if (!allowed) return;
  inventoryMigrationPendingDirection = direction;
  $('#inventory-migration-confirm-direction').textContent = direction === 'legacy-to-new'
    ? '旧版背包 → 新版背包'
    : '新版背包 → 旧版背包';
  $('#inventory-migration-confirm').classList.remove('hidden');
}

function hideInventoryMigrationConfirm() {
  $('#inventory-migration-confirm').classList.add('hidden');
  inventoryMigrationPendingDirection = null;
}

function renderInventoryMigrationReport(report) {
  const complete = Array.isArray(report?.completeCharacters) ? report.completeCharacters : [];
  const residuals = Array.isArray(report?.residuals) ? report.residuals : [];
  const completeHtml = complete.length
    ? `<ul>${complete.map((x) => `<li>${escapeHtml(x.name || ('角色 ' + x.characterId))}（ID ${x.characterId}）</li>`).join('')}</ul>`
    : '<p>没有角色被完整迁移。</p>';
  const residualHtml = residuals.length
    ? `<ul>${residuals.map((x) => `<li>${escapeHtml(x.characterName || (x.characterId ? '角色 ' + x.characterId : '账号 ' + x.accountId))}：` +
      `${escapeHtml(x.bagType)}残余 ${Number(x.itemCount || 0)} 件；请清理至少 ${Number(x.requiredFreeSlots || 0)} 个空槽后再次尝试。` +
      `${x.reason ? `（${escapeHtml(x.reason)}）` : ''}</li>`).join('')}</ul>`
    : '<p>无残余数据。</p>';
  const panel = $('#inventory-migration-report');
  panel.innerHTML = `<h3>迁移完成：${Number(report?.migratedItems || 0)} 条</h3>` +
    `<b>完整迁移角色</b>${completeHtml}<b>仍有残余</b>${residualHtml}`;
  panel.classList.remove('hidden');
}

async function executeInventoryMigration() {
  const direction = inventoryMigrationPendingDirection;
  if (!direction || inventoryMigrationBusy) return;
  inventoryMigrationBusy = true;
  $('#inventory-migration-confirm').classList.add('hidden');
  $('#inventory-migration-progress').className = 'hint';
  $('#inventory-migration-progress').textContent = direction === 'legacy-to-new'
    ? '正在执行：旧版背包 → 新版背包。两个操作已锁定，请等待事务完成…'
    : '正在执行：新版背包 → 旧版背包。两个操作已锁定，请等待事务完成…';
  setInventoryMigrationButtons();
  try {
    const endpoint = direction === 'legacy-to-new'
      ? '/api/inventory-migration/legacy-to-new'
      : '/api/inventory-migration/new-to-legacy';
    const report = await post(endpoint);
    renderInventoryMigrationReport(report);
    $('#inventory-migration-progress').textContent = '迁移事务已完成。请查看下方完整角色与残余数据报告。';
    toast('背包数据迁移完成');
  } catch (error) {
    $('#inventory-migration-progress').className = 'hint err';
    $('#inventory-migration-progress').textContent = `迁移失败，事务已回滚：${error.message}`;
    toast(`迁移失败，已回滚：${error.message}`, true);
  } finally {
    inventoryMigrationBusy = false;
    inventoryMigrationPendingDirection = null;
    try { await loadInventoryMigrationStatus(); } catch (_) { setInventoryMigrationButtons(); }
  }
}

function bindInventoryMigration() {
  $('#btn-inventory-migration').onclick = showInventoryMigrationPanel;
  $('#btn-migrate-legacy-new').onclick = () => showInventoryMigrationConfirm('legacy-to-new');
  $('#btn-migrate-new-legacy').onclick = () => showInventoryMigrationConfirm('new-to-legacy');
  $('#btn-cancel-inventory-migration').onclick = hideInventoryMigrationConfirm;
  $('#btn-confirm-inventory-migration').onclick = executeInventoryMigration;
  document.querySelector('.acc-tab[data-acc-tab="migration"]')?.addEventListener('click', () => {
    loadInventoryMigrationStatus().catch((error) => {
      $('#inventory-migration-progress').className = 'hint err';
      $('#inventory-migration-progress').textContent = error.message;
    });
  });
}

bindInventoryMigration();
