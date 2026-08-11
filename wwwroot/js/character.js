async function setLevel() {
  if (!currentChar) return;
  const level = parseInt($('#level-input').value, 10);
  try {
    await post(`/api/characters/${currentChar.characterId}/level`, { level });
    toast('等级已设置为 ' + level);
    refreshHeader();
    loadCharacters();
    loadStats();
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}

async function maxPersonalCargo() {
  if (!currentChar) return;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/personal-cargo/max`);
    toast(`当前角色仓库满级已设置: ${r.listParam16}`);
  } catch (e) {
    toast(e.message, true);
  }
}

async function unlockExtraEquipmentSlots() {
  if (!currentChar) return;
  updateExtraEquipmentSlotButton();
  const unlockBtn = $('#btn-unlock-equipment-slots');
  if (unlockBtn && unlockBtn.disabled) return;
  const unlockOldText = unlockBtn ? unlockBtn.textContent : '';
  if (unlockBtn) {
    unlockBtn.disabled = true;
    unlockBtn.textContent = '正在开启...';
  }
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/equipment-slots/unlock`);
    const completed = r.completedQuestIds?.length || 0;
    toast(completed > 0 ? `左右槽已开启，已完成 ${completed} 个相关任务` : '左右槽已开启');
    refreshHeader();
    loadAllVisibleQuests();
    loadAchieveQuests();
  } catch (e) {
    toast(e.message, true);
  } finally {
    if (unlockBtn) {
      unlockBtn.textContent = unlockOldText;
      updateExtraEquipmentSlotButton();
    }
  }
  return;
}

async function unlockDungeonPermissions() {
  if (!currentChar) return;
  const btn = $('#btn-unlock-dungeon-permissions');
  const oldText = btn ? btn.textContent : '';
  if (btn) {
    btn.disabled = true;
    btn.textContent = '正在开启...';
  }
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/dungeon-permissions/unlock`);
    toast(`已为当前角色开启 ${r.insertedCount || 0} 个副本的最高难度`);
  } catch (e) {
    toast(e.message, true);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.textContent = oldText;
    }
  }
}

function updateExtraEquipmentSlotButton() {
  const btn = $('#btn-unlock-equipment-slots');
  if (!btn || !currentChar) return;
  const unlocked = currentChar.extraEquipmentSlotsUnlocked || (currentChar.exEquipSlotStat & 3) === 3;
  btn.disabled = currentChar.level < 70 || unlocked;
  if (currentChar.level < 70)
    btn.title = '角色达到 70 级后可开启';
  else if (unlocked)
    btn.title = '左右槽已经开启';
  else
    btn.title = '';
}

async function deleteCurrentCharacter() {
  if (!currentChar) return;
  const character = currentChar;
  if (!confirmCharacterSwitchedAway(character, '删除')) return;
  const message = `将彻底删除角色 ${character.name} (#${character.characterId})，并删除背包、任务等关联数据。仍在佣兵出战或奖励邮件尚未送达时会拒绝删除。此操作不可恢复。是否继续？`;
  if (!confirm(message)) return;

  const confirmText = prompt('请输入 删除角色 以确认彻底删除当前角色');
  if (confirmText !== '删除角色') {
    if (confirmText != null) toast('确认文本不正确，已取消删除', true);
    return;
  }

  const btn = $('#btn-delete-character');
  const oldText = btn ? btn.textContent : '';
  if (btn) {
    btn.disabled = true;
    btn.textContent = '正在删除...';
  }
  try {
    const r = await post(`/api/characters/${character.characterId}/delete`, { confirmText });
    toast(`已彻底删除角色 ${r.name || character.name} (#${r.characterId})`);
    closeCharacterClonePanel();
    currentChar = null;
    if (typeof updateCharacterMailboxButton === 'function') updateCharacterMailboxButton();
    selectEpoch++;
    $('#detail').classList.add('hidden');
    await loadAccounts();
  } catch (e) {
    toast(e.message, true);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.textContent = oldText;
    }
  }
}

let characterClonePlan = null;
let cloneNameAvailable = false;
let cloneRequestRunning = false;
const CLONE_OPTIONS_DEFAULT_OFF = new Set([
  'quests',
  'quest',
  'clearedQuests',
  'clearedQuest',
  'titleBook',
  'titlebook',
  'dailyWeekly',
  'dailyWeeklyState',
  'characterState',
  'characterStates',
  'extraCharacterState',
  'otherCharacterState',
  'audit',
  'itemAudit',
  'itemAuditLog',
]);

function confirmCharacterSwitchedAway(character, actionName) {
  return confirm(
    `${actionName}角色前，请先在客户端进入选择角色界面，并切换到其他角色。\n\n`
    + `当前操作角色：${character.name} (#${character.characterId})\n\n`
    + '确认已经切换到其他角色后，点击“确定”继续。'
  );
}

async function openCharacterClonePanel() {
  if (!currentChar) return;
  if (!confirmCharacterSwitchedAway(currentChar, '复制')) return;
  $('#character-clone-panel').classList.remove('hidden');
  $('#clone-character-state').textContent = '正在加载复制设置...';
  $('#clone-character-name').value = `${currentChar.name}_copy`;
  cloneNameAvailable = false;
  $('#clone-name-state').textContent = '';
  try {
    characterClonePlan = await api(`/api/characters/${currentChar.characterId}/clone-plan`);
    renderCharacterClonePlan();
  } catch (e) {
    $('#clone-character-state').textContent = e.message;
    toast(e.message, true);
  }
}

function closeCharacterClonePanel() {
  $('#character-clone-panel').classList.add('hidden');
}

function renderCharacterClonePlan() {
  const select = $('#clone-target-account');
  select.innerHTML = '';
  for (const account of characterClonePlan.accounts || []) {
    const option = document.createElement('option');
    option.value = account.accountId;
    option.textContent = `${account.name} (#${account.accountId}) ${account.characterCount}/${account.slotLimit}`
      + (account.isCurrent ? ' 当前账号' : '')
      + (!account.canAcceptCharacter ? ' 已满' : '');
    option.disabled = !account.canAcceptCharacter;
    select.appendChild(option);
  }
  select.value = String(characterClonePlan.sourceAccountId);
  updateCloneAccountLimit();

  const box = $('#clone-option-list');
  box.innerHTML = '';
  for (const option of characterClonePlan.options || []) {
    const label = document.createElement('label');
    const defaultChecked = option.defaultChecked && !isCloneOptionDefaultOff(option);
    label.innerHTML = `<input type="checkbox" value="${escapeHtml(option.key)}" ${defaultChecked ? 'checked' : ''}> ${escapeHtml(option.label)}`;
    const input = label.querySelector('input');
    if (option.key === 'basic') {
      input.checked = true;
      input.disabled = true;
    }
    box.appendChild(label);
  }
  updateCloneButtonState();
  if (!cloneRequestRunning)
    $('#clone-character-state').textContent = '';
}

function updateCloneButtonState() {
  const btn = $('#btn-run-character-clone');
  if (!btn) return;
  btn.disabled = cloneRequestRunning;
}

function isCloneOptionDefaultOff(option) {
  const key = String(option?.key || '').replace(/[-_\s]/g, '').toLowerCase();
  const label = String(option?.label || '').toLowerCase();
  if ([...CLONE_OPTIONS_DEFAULT_OFF].some((item) => key === item.replace(/[-_\s]/g, '').toLowerCase()))
    return true;
  return label.includes('任务')
    || label.includes('完成记录')
    || label.includes('称号簿')
    || label.includes('每日')
    || label.includes('周常')
    || label.includes('角色状态')
    || label.includes('审计');
}

function activateMainTab(tabName) {
  const tab = document.querySelector(`.tab[data-tab="${tabName}"]`);
  if (!tab) return;
  document.querySelectorAll('.tab[data-tab]').forEach((t) => t.classList.remove('active'));
  document.querySelectorAll('.tab-page').forEach((p) => p.classList.add('hidden'));
  tab.classList.add('active');
  $('#tab-' + tabName).classList.remove('hidden');
}

function activateAccountTab(tabName) {
  const tab = document.querySelector(`.acc-tab[data-acc-tab="${tabName}"]`);
  if (!tab) return;
  document.querySelectorAll('.acc-tab').forEach((t) => t.classList.remove('active'));
  document.querySelectorAll('.acc-tab-page').forEach((p) => p.classList.add('hidden'));
  tab.classList.add('active');
  $('#acc-tab-' + tabName).classList.remove('hidden');
}

async function jumpToCharacterCurrency() {
  if (!currentChar) return toast('请先选择角色', true);
  activateMainTab('inventory');
  activeCategory = '货币';
  invPage = 0;
  clearInventoryConfiguration();
  if (inventoryItems.length === 0)
    await loadItems();
  else {
    renderCategoryNav();
    renderItemTable();
  }
}

async function jumpToAccountCurrency() {
  const accountId = parseInt($('#account-select').value, 10);
  if (!accountId) return toast('请先选择账号', true);
  await showAccountPanel();
  activateAccountTab('currency');
}

function updateCloneAccountLimit() {
  const accountId = parseInt($('#clone-target-account').value, 10);
  const account = (characterClonePlan?.accounts || []).find((a) => a.accountId === accountId);
  $('#clone-account-limit').textContent = account
    ? `角色 ${account.characterCount}/${account.slotLimit}${account.isCurrent ? '，当前账号' : ''}`
    : '';
}

async function checkCloneCharacterName() {
  cloneNameAvailable = false;
  const name = $('#clone-character-name').value.trim();
  $('#clone-name-state').textContent = '正在检查...';
  try {
    const result = await api(`/api/characters/name-available?name=${encodeURIComponent(name)}`);
    cloneNameAvailable = result.available === true;
    $('#clone-name-state').textContent = cloneNameAvailable ? '角色名可用' : (result.reason || '角色名不可用');
    if (!cloneNameAvailable) toast($('#clone-name-state').textContent, true);
  } catch (e) {
    $('#clone-name-state').textContent = e.message;
    toast(e.message, true);
  }
  updateCloneButtonState();
}

async function runCharacterClone() {
  if (!currentChar || !characterClonePlan) return;
  if (!cloneNameAvailable) return toast('请先检查角色名，并确认角色名可用', true);

  const targetAccountId = parseInt($('#clone-target-account').value, 10);
  const newName = $('#clone-character-name').value.trim();
  const options = [...document.querySelectorAll('#clone-option-list input[type="checkbox"]:checked')]
    .map((input) => input.value);
  if (!confirm(`复制角色 ${currentChar.name} 到账号 #${targetAccountId}，新角色名 ${newName}？`))
    return;

  const btn = $('#btn-run-character-clone');
  cloneRequestRunning = true;
  updateCloneButtonState();
  $('#clone-character-state').textContent = '正在复制...';
  try {
    const result = await post(`/api/characters/${currentChar.characterId}/clone`, { targetAccountId, newName, options });
    $('#clone-character-state').textContent = `复制完成，新角色 #${result.characterId}；请在客户端正常重新选择角色或重新登录后进入新角色`;
    toast(`角色已复制为 ${result.name} (#${result.characterId})，请在客户端正常刷新角色列表`);
    cloneNameAvailable = false;
    await loadAccounts();
    $('#account-select').value = String(result.targetAccountId);
    await loadCharacters(result.targetAccountId);
  } catch (e) {
    $('#clone-character-state').textContent = e.message;
    toast(e.message, true);
  } finally {
    cloneRequestRunning = false;
    updateCloneButtonState();
  }
}

function openCloneAccountPanel() {
  $('#clone-account-panel').classList.remove('hidden');
  $('#clone-account-state').textContent = '';
}

function closeCloneAccountPanel() {
  $('#clone-account-panel').classList.add('hidden');
}

function togglePasswordInput(inputSelector, buttonSelector) {
  const input = $(inputSelector);
  const button = $(buttonSelector);
  const visible = input.type === 'text';
  input.type = visible ? 'password' : 'text';
  button.textContent = visible ? '显示' : '隐藏';
}

async function createCloneAccount(e) {
  e.preventDefault();
  $('#clone-account-state').textContent = '正在创建...';
  try {
    const result = await post('/api/accounts/create-for-clone', {
      accountName: $('#clone-account-name').value.trim(),
      password: $('#clone-account-password').value,
      confirmPassword: $('#clone-account-password-confirm').value,
    });
    toast(`账号 ${result.name} 已创建`);
    closeCloneAccountPanel();
    await loadAccounts();
    if (currentChar) {
      characterClonePlan = await api(`/api/characters/${currentChar.characterId}/clone-plan`);
      renderCharacterClonePlan();
      $('#clone-target-account').value = String(result.accountId);
      updateCloneAccountLimit();
    }
  } catch (err) {
    $('#clone-account-state').textContent = err.message;
    toast(err.message, true);
  }
}

let growOptions = null;

async function loadGrowOptions() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const fetched = await api(`/api/characters/${currentChar.characterId}/growoptions`);
    if (epoch !== selectEpoch) return;
    renderGrowOptions(fetched);
  } catch (e) {
    toast(e.message, true);
  }
}

async function loadGrowOptionsForJob() {
  if (!currentChar) return;
  const job = parseInt($('#grow-job').value, 10);
  const epoch = selectEpoch;
  try {
    const fetched = await api(`/api/characters/${currentChar.characterId}/growoptions?job=${encodeURIComponent(job)}`);
    if (epoch !== selectEpoch) return;
    renderGrowOptions(fetched);
  } catch (e) {
    toast(e.message, true);
  }
}

function renderGrowOptions(fetched) {
  growOptions = fetched;

  const jobSel = $('#grow-job');
  if (jobSel) {
    jobSel.innerHTML = '';
    const jobs = Array.isArray(growOptions.jobs) && growOptions.jobs.length
      ? growOptions.jobs
      : [{ value: growOptions.job, label: growOptions.options.baseName || `job ${growOptions.job}` }];
    for (const job of jobs) {
      const option = document.createElement('option');
      option.value = job.value;
      option.textContent = jobLabelWithGender(job.value, job.label || `job ${job.value}`);
      jobSel.appendChild(option);
    }
    jobSel.value = String(growOptions.job);
  }

  const firstSel = $('#grow-first');
  firstSel.innerHTML = '';
  const baseOption = document.createElement('option');
  baseOption.value = '0';
  baseOption.textContent = growOptions.options.baseName || '未转职';
  firstSel.appendChild(baseOption);
  for (const g of growOptions.options.growTypes) {
    const option = document.createElement('option');
    option.value = g.value;
    option.textContent = g.label;
    option.disabled = currentChar && currentChar.level < 15;
    firstSel.appendChild(option);
  }
  firstSel.value = String(growOptions.first);
  renderSecondOptions();
  $('#grow-second').value = String(growOptions.second);
}

function renderSecondOptions() {
  const first = parseInt($('#grow-first').value, 10);
  const secondSel = $('#grow-second');
  secondSel.innerHTML = '';
  const baseOption = document.createElement('option');
  baseOption.value = '0';
  baseOption.textContent = '未觉醒';
  secondSel.appendChild(baseOption);
  const grow = growOptions?.options.growTypes.find((g) => g.value === first);
  if (grow) {
    grow.awakenings.forEach((name, i) => {
      const value = i + 1;
      const option = document.createElement('option');
      option.value = value;
      option.textContent = name;
      option.disabled = currentChar && ((value === 1 && currentChar.level < 50) || (value >= 2 && currentChar.level < 75));
      secondSel.appendChild(option);
    });
  }
}

function jobLabelWithGender(job, label) {
  return label;
}

function jobGenderSuffix(job) {
  return '';
}

async function setGrowType() {
  if (!currentChar) return;
  const job = parseInt($('#grow-job').value, 10);
  const first = parseInt($('#grow-first').value, 10);
  const second = parseInt($('#grow-second').value, 10);
  try {
    await post(`/api/characters/${currentChar.characterId}/growtype`, { job, first, second });
    toast('职业/转职/觉醒已覆写，战斗属性和技能点已重算');
    refreshHeader();
    loadCharacters();
    loadStats();
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}

let inventoryLimitIs999 = false;
let goldLimitStatus = null;

function updateInventoryLimitButtons(is999, isSaving = false) {
  const setMaxButton = $('#btn-inventory-limit-999');
  const restoreButton = $('#btn-inventory-limit-restore');
  setMaxButton.disabled = isSaving || is999;
  restoreButton.disabled = isSaving || !is999;
}

async function loadStats() {
  if (!currentChar) return;
  updateInventoryLimitButtons(inventoryLimitIs999, true);
  updateGoldLimitButton(null, true);
  const epoch = selectEpoch;
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/stats`);
    if (epoch !== selectEpoch) return;
    $('#stats-meta').textContent = `Lv.${data.level} job=${data.job} growType=${data.growType}`;
    inventoryLimitIs999 = data.inventoryLimitOverridden;
    updateInventoryLimitButtons(inventoryLimitIs999);
    $('#inventory-limit-state').textContent = data.inventoryLimitOverridden
      ? '当前已设为 999。服务端重算属性后会恢复正常值。'
      : '当前使用职业/等级的正常负重。';
    const tbody = $('#stats-table tbody');
    tbody.innerHTML = '';
    const cell = (s) => s
      ? `<td${s.zeroBlock ? ' class="dim"' : ''}>${s.label}</td><td${s.zeroBlock ? ' class="dim"' : ''}>${Number(s.value).toLocaleString()}</td>`
      : '<td></td><td></td>';
    for (let i = 0; i < data.stats.length; i += 2) {
      const tr = document.createElement('tr');
      tr.innerHTML = cell(data.stats[i]) + cell(data.stats[i + 1]);
      tbody.appendChild(tr);
    }
  } catch (e) {
    toast(e.message, true);
  }
}

function updateGoldLimitButton(status, isSaving = false) {
  const button = $('#btn-gold-limit-max');
  button.disabled = isSaving || !status || !status.canSetMaximum;
}

async function loadGoldLimit() {
  if (!currentChar) return;
  updateGoldLimitButton(null, true);
  const epoch = selectEpoch;
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/gold-limit`);
    if (epoch !== selectEpoch) return;
    goldLimitStatus = data;
    updateGoldLimitButton(data);
    const suffix = data.isMaximum
      ? '（已满级）'
      : data.characterLevel < data.minimumUpgradeCharacterLevel
        ? `（需 ${data.minimumUpgradeCharacterLevel} 级）`
        : '';
    $('#gold-limit-state').textContent = `当前金币上限 ${Number(data.goldCarryLimit).toLocaleString()}${suffix}`;
  } catch (e) {
    goldLimitStatus = null;
    $('#gold-limit-state').textContent = '金币上限读取失败';
    toast(e.message, true);
  }
}

async function setMaximumGoldLimit() {
  if (!currentChar || !goldLimitStatus?.canSetMaximum) return;
  updateGoldLimitButton(goldLimitStatus, true);
  try {
    const data = await post(`/api/characters/${currentChar.characterId}/gold-limit/max`);
    toast(`金币上限已升至 ${Number(data.goldCarryLimit).toLocaleString()}`);
    await loadGoldLimit();
  } catch (e) {
    toast(e.message, true);
    updateGoldLimitButton(goldLimitStatus);
  }
}

async function setInventoryLimitTo999() {
  if (!currentChar) return;
  updateInventoryLimitButtons(inventoryLimitIs999, true);
  try {
    await post(`/api/characters/${currentChar.characterId}/inventory-limit/max`);
    toast('负重上限已设为 999');
    await loadStats();
  } catch (e) {
    toast(e.message, true);
    updateInventoryLimitButtons(inventoryLimitIs999);
  }
}

async function restoreNormalInventoryLimit() {
  if (!currentChar) return;
  updateInventoryLimitButtons(inventoryLimitIs999, true);
  try {
    await post(`/api/characters/${currentChar.characterId}/inventory-limit/restore`);
    toast('已恢复职业/等级对应的正常负重');
    await loadStats();
  } catch (e) {
    toast(e.message, true);
    updateInventoryLimitButtons(inventoryLimitIs999);
  }
}

async function loadSpTp() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const d = await api(`/api/characters/${currentChar.characterId}/sptp`);
    if (epoch !== selectEpoch) return;
    $('#sptp-view').innerHTML =
      `<b>剩余 SP ${d.remainingSp.toLocaleString()}</b>&nbsp;/ 总 SP ${d.totalSp.toLocaleString()}` +
      `&nbsp;&nbsp;|&nbsp;&nbsp;<b>剩余 TP ${d.remainingTp}</b>&nbsp;/ 总 TP ${d.totalTp}` +
      `&nbsp;&nbsp;(附加 SP ${d.bonusSp} / TP ${d.bonusTp})`;
    $('#sp-now').textContent = `当前附加 SP ${d.bonusSp} / TP ${d.bonusTp}`;
  } catch (e) {
    $('#sptp-view').textContent = e.message;
  }
}

async function adjustSp() {
  if (!currentChar) return;
  const sp = parseInt($('#sp-input').value, 10) || 0;
  const tp = parseInt($('#tp-input').value, 10) || 0;
  if (!sp && !tp) return toast('SP/TP 至少填写一个非零值', true);
  try {
    await post(`/api/characters/${currentChar.characterId}/sp`, { sp, tp });
    toast('附加点已调整');
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}
