// ---- 状态与角色列表 ----

let accounts = [];

function resetAccountWorkspace() {
  accounts = [];
  currentChar = null;
  selectEpoch++;
  $('#account-search').value = '';
  $('#account-select').innerHTML = '';
  $('#account-info').innerHTML = '';
  $('#char-list').innerHTML = '';
  $('#char-count').textContent = '';
  $('#detail').classList.add('hidden');
  $('#account-panel').classList.add('hidden');
  if (typeof updateCharacterMailboxButton === 'function') updateCharacterMailboxButton();
}

async function loadAccounts(expectedRuntimeEpoch) {
  const epoch = Number.isInteger(expectedRuntimeEpoch) ? expectedRuntimeEpoch : null;
  const data = await api('/api/accounts');
  if (epoch != null && epoch !== runtimeSourceEpoch) return;
  accounts = data.accounts;
  renderAccountOptions();
  onAccountChanged();
}

// 只刷新侧栏的账号数据(下拉+货币清单), 不动面板可见性/当前角色 —
// 供账号面板覆写成功后回显用(loadAccounts→onAccountChanged 会隐藏面板并清选中)
async function refreshAccountsSidebar() {
  try {
    const data = await api('/api/accounts');
    accounts = data.accounts;
    renderAccountOptions();
    const accountId = parseInt($('#account-select').value, 10);
    const account = accounts.find((a) => a.accountId === accountId);
    if (account) {
      $('#account-info').innerHTML = [
        ['点券', account.cera], ['代币券', account.tokenCera], ['幸运星', account.luckyStar],
      ].map(([k, v]) =>
        `<div class="acct-stat"><span class="k">${k}</span><span class="v">${Number(v).toLocaleString()}</span></div>`
      ).join('');
    }
  } catch (e) {
    toast(e.message, true);
  }
}

// 按搜索词过滤下拉选项: 匹配账号名/ID, 也按角色名反查账号(命中的角色标在选项里);
// 已选账号仍在结果里时保持选中
function renderAccountOptions() {
  const filter = $('#account-search').value.trim().toLowerCase();
  const select = $('#account-select');
  const previous = select.value;
  select.innerHTML = '';
  const matched = [];
  for (const a of accounts) {
    if (!filter || a.name.toLowerCase().includes(filter) || String(a.accountId).includes(filter)) {
      matched.push({ account: a, viaChar: null });
      continue;
    }
    const hit = (a.characterNames || []).find((n) => n.toLowerCase().includes(filter));
    if (hit) matched.push({ account: a, viaChar: hit });
  }
  for (const m of matched) {
    const option = document.createElement('option');
    option.value = m.account.accountId;
    option.textContent = `${m.account.name} (#${m.account.accountId} · ${m.account.characterCount} 角色)`
      + (m.viaChar ? ` · 角色: ${m.viaChar}` : '');
    select.appendChild(option);
  }
  if (previous && matched.some((m) => String(m.account.accountId) === previous))
    select.value = previous;
  return matched.length;
}

function onAccountChanged() {
  const accountId = parseInt($('#account-select').value, 10);
  const account = accounts.find((a) => a.accountId === accountId);
  const info = $('#account-info');
  if (account) {
    info.innerHTML = [
      ['点券', account.cera], ['代币券', account.tokenCera], ['幸运星', account.luckyStar],
    ].map(([k, v]) =>
      `<div class="acct-stat"><span class="k">${k}</span><span class="v">${Number(v).toLocaleString()}</span></div>`
    ).join('');
  } else {
    info.innerHTML = '<div class="hint">无匹配账号</div>';
  }
  $('#detail').classList.add('hidden');
  $('#account-panel').classList.add('hidden');
  currentChar = null;
  if (typeof updateCharacterMailboxButton === 'function') updateCharacterMailboxButton();
  if (account) {
    loadCharacters(accountId);
  } else {
    $('#char-list').innerHTML = '';
    $('#char-count').textContent = '';
  }
}

// ---- 账号数据管理 ----

async function showAccountPanel() {
  const accountId = parseInt($('#account-select').value, 10);
  if (!accountId) return;
  try {
    const detail = await api(`/api/accounts/${accountId}/detail`);
    $('#detail').classList.add('hidden');
    currentChar = null;
    if (typeof updateCharacterMailboxButton === 'function') updateCharacterMailboxButton();
    document.querySelectorAll('#char-list li').forEach((el) => el.classList.remove('active'));
    renderAccountPanel(accountId, detail);
    $('#account-panel').classList.remove('hidden');
  } catch (e) {
    toast(e.message, true);
  }
}

function renderAccountPanel(accountId, detail) {
  const c = detail.currencies;
  $('#account-panel-header').innerHTML =
    `<b>账号 ${escapeHtml(c.name)}</b> (#${accountId})`;

  const currencyDefs = [
    { type: 'cera', label: '点券', value: c.cera },
    { type: 'token', label: '代币券', value: c.tokenCera },
    { type: 'luckyStar', label: '幸运星', value: c.luckyStar },
    { type: 'seriaLuck', label: '赛利亚幸运值', value: c.seriaLuck },
  ];
  const box = $('#account-currencies');
  box.innerHTML = '';
  for (const def of currencyDefs) {
    const row = document.createElement('div');
    row.className = 'row';
    row.innerHTML = `<span style="width:110px">${def.label}</span>
      <b style="width:130px">${Number(def.value).toLocaleString()}</b>
      <input type="number" min="0" class="val-input" value="${def.value}"><button class="mini">覆写</button>`;
    row.querySelector('button').onclick = async () => {
      const value = parseInt(row.querySelector('input').value, 10);
      if (isNaN(value) || value < 0) return toast('请输入非负整数', true);
      try {
        await post(`/api/accounts/${accountId}/currency`, { type: def.type, value });
        toast(`${def.label}已覆写为 ${value.toLocaleString()}`);
        showAccountPanel();
        refreshAccountsSidebar(); // 不能走 loadAccounts→onAccountChanged, 那条链会把面板藏掉
      } catch (e) {
        toast(e.message, true);
      }
    };
    box.appendChild(row);
  }

  renderAccountProgress(accountId, detail.progress);

  const cubeBody = $('#cube-table tbody');
  cubeBody.innerHTML = '';
  for (const cube of detail.cubes) {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${cube.itemId}</td><td>${escapeHtml(cube.name || '')}</td>
      <td>${cube.count.toLocaleString()}</td>
      <td><input type="number" min="0" class="val-input" value="${cube.count}"></td><td><button class="mini">覆写</button></td>`;
    tr.querySelector('button').onclick = async () => {
      const value = parseInt(tr.querySelector('input').value, 10);
      if (isNaN(value) || value < 0) return toast('请输入非负整数', true);
      try {
        await post(`/api/accounts/${accountId}/cube`, { itemId: cube.itemId, value });
        toast('晶块已覆写');
        showAccountPanel();
      } catch (e) {
        toast(e.message, true);
      }
    };
    cubeBody.appendChild(tr);
  }

  const cargoBody = $('#cargo-table tbody');
  cargoBody.innerHTML = '';
  for (const item of detail.cargo) {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${item.slot}</td><td>${item.templateId}</td>
      <td>${escapeHtml(item.name || '')}</td><td>${item.count}</td><td>${item.durability}</td>
      <td><button class="mini danger">删除</button></td>`;
    tr.querySelector('button').onclick = async () => {
      try {
        await post(`/api/accounts/${accountId}/cargo/delete`, { slot: item.slot });
        toast('已删除');
        showAccountPanel();
      } catch (e) {
        toast(e.message, true);
      }
    };
    cargoBody.appendChild(tr);
  }
  if (detail.cargo.length === 0)
    cargoBody.innerHTML = '<tr><td colspan="6" class="hint">账号金库为空</td></tr>';

  $('#btn-max-cargo').onclick = async () => {
    try {
      const r = await post(`/api/accounts/${accountId}/cargo/max`);
      toast(`账号金库满级已设置: ${r.listParam16}`);
      showAccountPanel();
    } catch (e) {
      toast(e.message, true);
    }
  };

  $('#btn-clear-cargo').onclick = async () => {
    if (detail.cargo.length === 0)
      return toast('账号金库已是空的', true);
    if (!confirm(`清空账号金库共 ${detail.cargo.length} 件物品？此操作不可撤销。`))
      return;
    try {
      const r = await post(`/api/accounts/${accountId}/cargo/clear`);
      toast(`已清空账号金库 (${r.deleted} 件)`);
      showAccountPanel();
    } catch (e) {
      toast(e.message, true);
    }
  };

  bindAccountBackupPanel(accountId);
}

function bindAccountBackupPanel(accountId) {
  const state = $('#account-backup-state');
  const fileInput = $('#account-backup-file');

  $('#btn-export-account-backup').onclick = async () => {
    try {
      if (state) state.textContent = '正在导出...';
      const backup = await post(`/api/accounts/${accountId}/backup`);
      const blob = new Blob([JSON.stringify(backup, null, 2)], { type: 'application/json' });
      const link = document.createElement('a');
      const stamp = new Date().toISOString().replace(/[:.]/g, '-');
      link.href = URL.createObjectURL(blob);
      link.download = `账号备份-account_${accountId}-${stamp}.json`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(link.href);
      if (state) {
        state.textContent =
          `已导出格式 v${backup.version || '?'}：${backup.tables?.length || 0} 张表，${backup.characterIDs?.length || 0} 个角色；包含邮箱与待领取附件。`;
      }
      toast('账号备份已导出');
    } catch (e) {
      if (state) state.textContent = e.message;
      toast(e.message, true);
    }
  };

  if (fileInput) {
    fileInput.onchange = () => {
      const file = fileInput.files && fileInput.files[0];
      if (state) state.textContent = file ? `已选择：${file.name}；恢复前会校验备份版本和服务端结构。` : '';
    };
  }

  $('#btn-restore-account-backup').onclick = async () => {
    const file = fileInput && fileInput.files && fileInput.files[0];
    if (!file) return toast('请先选择备份 JSON 文件', true);

    const confirmText = prompt('恢复会覆盖备份文件内账号 ID 对应的当前账号数据。请输入“恢复账号”确认。');
    if (confirmText !== '恢复账号')
      return toast('已取消恢复', true);

    try {
      if (state) state.textContent = '正在读取备份...';
      const backup = JSON.parse(await file.text());
      if (state) state.textContent = `正在校验并恢复格式 v${backup.version || '?'} 的备份...`;
      const result = await post('/api/accounts/restore', backup);
      if (state) {
        const upgradeText = result.upgradedFromVersion
          ? `；已从 v${result.upgradedFromVersion} 安全升级到 v2`
          : '';
        const remapCount = (result.remappedMailboxMessageIdCount || 0)
          + (result.remappedMailboxAuditIdCount || 0);
        const remapText = remapCount ? `；已安全处理 ${remapCount} 个邮件编号冲突` : '';
        state.textContent =
          `已恢复账号 #${result.accountID}，角色 ${result.restoredCharacterCount} 个，覆盖旧角色 ${result.deletedExistingCharacterCount} 个；邮箱和待领取附件已恢复${upgradeText}${remapText}。`;
      }
      toast('账号备份已恢复');
      await loadAccounts(runtimeSourceEpoch);
      if (result.accountID) {
        $('#account-select').value = String(result.accountID);
        await showAccountPanel();
      }
    } catch (e) {
      if (state) state.textContent = e.message;
      toast(e.message, true);
    }
  };
}

function renderAccountProgress(accountId, progress) {
  const honorBox = $('#account-honor-progress');
  const capsuleBox = $('#account-growth-capsule-progress');
  if (!progress || !progress.honor || !progress.growthCapsule) {
    honorBox.innerHTML = '<span class="hint">账号进度读取失败</span>';
    capsuleBox.innerHTML = '';
    return;
  }

  const honor = progress.honor;
  const honorRow = document.createElement('div');
  honorRow.className = 'row';
  honorRow.innerHTML = `<span class="account-progress-value">当前 Lv.${honor.level} / ${honor.maxLevel}</span>
    <span class="account-progress-detail">等级经验 ${Number(honor.currentLevelExp).toLocaleString()} / ${Number(honor.currentLevelExpCap).toLocaleString()}，总经验 ${Number(honor.totalExp).toLocaleString()} / ${Number(honor.maxTotalExp).toLocaleString()}</span>
    <label>目标等级 <input type="number" min="1" max="${honor.maxLevel}" class="val-input" value="${honor.level}"></label>
    <button class="mini" data-action="set">调整</button><button class="mini" data-action="max">一键满级</button>`;
  const honorInput = honorRow.querySelector('input');
  honorRow.querySelector('[data-action="set"]').onclick = async () => {
    const level = Number(honorInput.value);
    if (!Number.isSafeInteger(level) || level < 1 || level > honor.maxLevel)
      return toast(`请输入 1 到 ${honor.maxLevel} 的整数`, true);
    try {
      const result = await post(`/api/accounts/${accountId}/honor-level`, { level });
      renderAccountProgress(accountId, result.progress);
      toast(`荣誉等级已调整为 Lv.${result.progress.honor.level}`);
    } catch (e) {
      toast(e.message, true);
    }
  };
  honorRow.querySelector('[data-action="max"]').onclick = async () => {
    try {
      const result = await post(`/api/accounts/${accountId}/honor-level/max`);
      renderAccountProgress(accountId, result.progress);
      toast('荣誉等级已满级');
    } catch (e) {
      toast(e.message, true);
    }
  };
  honorBox.replaceChildren(honorRow);

  const capsule = progress.growthCapsule;
  const capsuleRow = document.createElement('div');
  capsuleRow.className = 'row';
  capsuleRow.innerHTML = `<span class="account-progress-value">当前 ${Number(capsule.totalExp).toLocaleString()} / ${Number(capsule.requiredExp).toLocaleString()}</span>
    <label>覆写为 <input type="number" min="0" max="${capsule.requiredExp}" class="val-input" value="${capsule.totalExp}"></label>
    <button class="mini" data-action="set">覆写</button><button class="mini" data-action="max">一键满级</button>`;
  const capsuleInput = capsuleRow.querySelector('input');
  capsuleRow.querySelector('[data-action="set"]').onclick = async () => {
    const exp = Number(capsuleInput.value);
    if (!Number.isSafeInteger(exp) || exp < 0)
      return toast('请输入非负整数', true);
    try {
      const result = await post(`/api/accounts/${accountId}/growth-capsule`, { exp });
      renderAccountProgress(accountId, result.progress);
      toast(`能量胶囊经验已覆写为 ${Number(result.progress.growthCapsule.totalExp).toLocaleString()}`);
    } catch (e) {
      toast(e.message, true);
    }
  };
  capsuleRow.querySelector('[data-action="max"]').onclick = async () => {
    try {
      const result = await post(`/api/accounts/${accountId}/growth-capsule/max`);
      renderAccountProgress(accountId, result.progress);
      toast('能量胶囊经验已满');
    } catch (e) {
      toast(e.message, true);
    }
  };
  capsuleBox.replaceChildren(capsuleRow);
}

async function loadCharacters(accountId, expectedRuntimeEpoch = runtimeSourceEpoch) {
  const epoch = Number.isInteger(expectedRuntimeEpoch) ? expectedRuntimeEpoch : runtimeSourceEpoch;
  try {
    if (accountId == null)
      accountId = parseInt($('#account-select').value, 10);
    const data = await api('/api/characters?accountId=' + accountId);
    if (epoch !== runtimeSourceEpoch) return;
    const list = $('#char-list');
    list.innerHTML = '';
    $('#char-count').textContent = data.characters.length + ' 个';
    for (const c of data.characters) {
      const li = document.createElement('li');
      li.dataset.characterId = c.characterId;
      li.innerHTML = `<div class="char-line"><span class="char-name">${escapeHtml(c.name)}</span>
          <span class="char-lv">Lv.${c.level}</span></div>
        <div class="char-meta">${escapeHtml(c.jobName)} · #${c.characterId}</div>`;
      li.onclick = () => selectCharacter(c.characterId, li);
      list.appendChild(li);
    }
    if (currentChar) {
      const activeLi = [...list.children].find((el) =>
        el.dataset.characterId === String(currentChar.characterId));
      if (activeLi) activeLi.classList.add('active');
    }
  } catch (e) {
    toast(e.message, true);
  }
}

async function selectCharacter(id, li) {
  if (typeof closeCharacterClonePanel === 'function') closeCharacterClonePanel();
  const epoch = ++selectEpoch;
  document.querySelectorAll('#char-list li').forEach((el) => el.classList.remove('active'));
  if (li) li.classList.add('active');
  for (const sel of INTERACTIVE_TBODY_SELECTORS) {
    const el = $(sel);
    if (el) el.innerHTML = '<tr><td colspan="7" class="hint">加载中…</td></tr>';
  }
  try {
    const c = await api('/api/characters/' + id);
    if (epoch !== selectEpoch) return; // 期间又切了别的角色, 本次结果作废
    currentChar = c;
    if (typeof updateCharacterMailboxButton === 'function') updateCharacterMailboxButton();
    clearGiveConfiguration();
    $('#account-panel').classList.add('hidden');
    $('#detail').classList.remove('hidden');
    $('#char-header').innerHTML =
      `<b>${escapeHtml(c.name)}</b> Lv.${c.level} ${escapeHtml(c.jobName)}` +
      `<span class="wallet">金币 ${c.wallet.gold.toLocaleString()} · 点券 ${c.wallet.cera.toLocaleString()}` +
      ` · SP+${c.bonusSp} TP+${c.bonusTp}</span>`;
    $('#level-input').value = c.level;
    $('#level-now').textContent = `当前 Lv.${c.level}, 经验 ${Number(c.exp).toLocaleString()}`;
    updateExtraEquipmentSlotButton();
    resetGiveUsableJobToCurrentCharacter();
    loadStats();
    loadGoldLimit();
    loadSpTp();
    loadGrowOptions();
    loadItems();
    loadQuests();
    loadAllVisibleQuests();
    loadMainQuests();
    loadAchieveQuests();
    loadClearedQuests();
    if (document.querySelector('.quest-tab[data-quest-tab="lib"]')?.classList.contains('active'))
      searchQuestLib();
    searchItems(0);
  } catch (e) {
    toast(e.message, true);
  }
}

function refreshHeader() {
  if (currentChar) selectCharacter(currentChar.characterId,
    document.querySelector('#char-list li.active'));
}
