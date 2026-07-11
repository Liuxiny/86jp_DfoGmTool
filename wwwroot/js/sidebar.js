// ---- 状态与角色列表 ----

let accounts = [];

async function loadAccounts() {
  const data = await api('/api/accounts');
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
}

async function loadCharacters(accountId) {
  try {
    if (accountId == null)
      accountId = parseInt($('#account-select').value, 10);
    const data = await api('/api/characters?accountId=' + accountId);
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
    $('#account-panel').classList.add('hidden');
    $('#detail').classList.remove('hidden');
    $('#char-header').innerHTML =
      `<b>${escapeHtml(c.name)}</b> Lv.${c.level} ${escapeHtml(c.jobName)}` +
      `<span class="wallet">金币 ${c.wallet.gold.toLocaleString()} · 点券 ${c.wallet.cera.toLocaleString()}` +
      ` · SP+${c.bonusSp} TP+${c.bonusTp}</span>`;
    $('#level-input').value = c.level;
    $('#level-now').textContent = `当前 Lv.${c.level}, 经验 ${Number(c.exp).toLocaleString()}`;
    loadStats();
    loadSpTp();
    loadGrowOptions();
    loadItems();
    loadQuests();
    loadMainQuests();
    loadAchieveQuests();
    loadClearedQuests();
  } catch (e) {
    toast(e.message, true);
  }
}

function refreshHeader() {
  if (currentChar) selectCharacter(currentChar.characterId,
    document.querySelector('#char-list li.active'));
}
