// ---- 角色属性 ----

async function setLevel() {
  if (!currentChar) return;
  const level = parseInt($('#level-input').value, 10);
  try {
    await post(`/api/characters/${currentChar.characterId}/level`, { level });
    toast('等级已设置为 ' + level + '，技能已清空待重建（下次进入角色时服务端重发技能，SP/TP 自动回满）');
    refreshHeader();
    loadCharacters();
    loadStats();
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}

// ---- 转职 / 觉醒 ----

let growOptions = null;

async function loadGrowOptions() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const fetched = await api(`/api/characters/${currentChar.characterId}/growoptions`);
    if (epoch !== selectEpoch) return;
    growOptions = fetched;
    const firstSel = $('#grow-first');
    firstSel.innerHTML = `<option value="0">${escapeHtml(growOptions.options.baseName || '未转职')}</option>`;
    for (const g of growOptions.options.growTypes)
      firstSel.innerHTML += `<option value="${g.value}">${escapeHtml(g.label)}</option>`;
    firstSel.value = String(growOptions.first);
    renderSecondOptions();
    $('#grow-second').value = String(growOptions.second);
  } catch (e) {
    toast(e.message, true);
  }
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
  const first = parseInt($('#grow-first').value, 10);
  const second = parseInt($('#grow-second').value, 10);
  try {
    await post(`/api/characters/${currentChar.characterId}/growtype`, { first, second });
    toast('转职/觉醒已覆写，技能已清空待重建（下次进入角色时服务端按新方向重发技能，SP/TP 自动回满）');
    refreshHeader();
    loadCharacters();
    loadStats();
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}

// ---- 基础属性表 ----

async function loadStats() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/stats`);
    if (epoch !== selectEpoch) return;
    $('#stats-meta').textContent = `Lv.${data.level} growType=${data.growType}`;
    const tbody = $('#stats-table tbody');
    tbody.innerHTML = '';
    const cell = (s) => s
      ? `<td${s.zeroBlock ? ' class="dim"' : ''}>${s.label}</td><td${s.zeroBlock ? ' class="dim"' : ''}>${Number(s.value).toLocaleString()}</td>`
      : '<td></td><td></td>';
    // 两列布局: 每行放两个属性; 异常抗性段(本版本恒0)淡显
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
      `&nbsp;&nbsp;(其中附加 SP ${d.bonusSp} / TP ${d.bonusTp})` +
      (d.remainingSpPvp !== undefined
        ? `<br>PVP 页: 剩余 SP ${d.remainingSpPvp.toLocaleString()} / TP ${d.remainingTpPvp}`
        : '');
    $('#sp-now').textContent = `当前附加 SP ${d.bonusSp} / TP ${d.bonusTp}`;
  } catch (e) {
    $('#sptp-view').textContent = e.message;
  }
}

async function adjustSp() {
  if (!currentChar) return;
  const sp = parseInt($('#sp-input').value, 10) || 0;
  const tp = parseInt($('#tp-input').value, 10) || 0;
  if (!sp && !tp) return toast('SP/TP 至少填一个非零值', true);
  try {
    await post(`/api/characters/${currentChar.characterId}/sp`, { sp, tp });
    toast('附加点已调整');
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}
