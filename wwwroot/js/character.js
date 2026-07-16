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

async function deleteCurrentCharacter() {
  if (!currentChar) return;
  const character = currentChar;
  const message = `将彻底删除角色 ${character.name} (#${character.characterId})，并删除背包、任务等关联数据。此操作不可恢复。是否继续？`;
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
    currentChar = null;
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

async function openCharacterClonePanel() {
  if (!currentChar) return;
  $('#character-clone-panel').classList.remove('hidden');
  $('#clone-character-state').textContent = '正在加载复制设置...';
  $('#clone-character-name').value = `${currentChar.name}_copy`;
  cloneNameAvailable = false;
  $('#clone-name-state').textContent = '';
  try {
    characterClonePlan = await api(`/api/characters/${currentChar.characterId}/clone-plan`);
    renderCharacterClonePlan();
    $('#clone-character-state').textContent = '';
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
    label.innerHTML = `<input type="checkbox" value="${escapeHtml(option.key)}" ${option.defaultChecked ? 'checked' : ''}> ${escapeHtml(option.label)}`;
    const input = label.querySelector('input');
    if (option.key === 'basic') {
      input.checked = true;
      input.disabled = true;
    }
    box.appendChild(label);
  }
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
  btn.disabled = true;
  $('#clone-character-state').textContent = '正在复制...';
  try {
    const result = await post(`/api/characters/${currentChar.characterId}/clone`, { targetAccountId, newName, options });
    $('#clone-character-state').textContent = `复制完成，新角色 #${result.characterId}`;
    toast(`角色已复制为 ${result.name} (#${result.characterId})`);
    cloneNameAvailable = false;
    await loadAccounts();
    $('#account-select').value = String(result.targetAccountId);
    await loadCharacters(result.targetAccountId);
  } catch (e) {
    $('#clone-character-state').textContent = e.message;
    toast(e.message, true);
  } finally {
    btn.disabled = false;
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
    for (const job of jobs)
      jobSel.innerHTML += `<option value="${job.value}">${escapeHtml(job.label || `job ${job.value}`)}</option>`;
    jobSel.value = String(growOptions.job);
  }

  const firstSel = $('#grow-first');
  firstSel.innerHTML = `<option value="0">${escapeHtml(growOptions.options.baseName || '未转职')}</option>`;
  for (const g of growOptions.options.growTypes)
    firstSel.innerHTML += `<option value="${g.value}">${escapeHtml(g.label)}</option>`;
  firstSel.value = String(growOptions.first);
  renderSecondOptions();
  $('#grow-second').value = String(growOptions.second);
}

function renderSecondOptions() {
  const first = parseInt($('#grow-first').value, 10);
  const secondSel = $('#grow-second');
  secondSel.innerHTML = '<option value="0">未觉醒</option>';
  const grow = growOptions?.options.growTypes.find((g) => g.value === first);
  if (grow) {
    grow.awakenings.forEach((name, i) => {
      secondSel.innerHTML += `<option value="${i + 1}">${escapeHtml(name)}</option>`;
    });
  }
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

async function loadStats() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/stats`);
    if (epoch !== selectEpoch) return;
    $('#stats-meta').textContent = `Lv.${data.level} job=${data.job} growType=${data.growType}`;
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
