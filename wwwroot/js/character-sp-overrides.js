async function loadSpTp() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const d = await api(`/api/characters/${currentChar.characterId}/sptp`);
    if (epoch !== selectEpoch) return;

    const currentPage = d.currentSkillPage === 1 ? 1 : 0;
    const page0Sp = d.remainingSpPage0 ?? d.remainingSp;
    const page0Tp = d.remainingTpPage0 ?? d.remainingTp;
    const page1Sp = d.remainingSpPage1 ?? 0;
    const page1Tp = d.remainingTpPage1 ?? 0;
    const page0Current = d.skillTreeUnlocked !== false && currentPage === 0;
    const page1Current = d.skillTreeUnlocked !== false && currentPage === 1;
    const page1Locked = d.skillTreeUnlocked === false;
    const targetSp = page1Locked ? page0Sp : Math.min(page0Sp, page1Sp);
    const targetTp = page1Locked ? page0Tp : Math.min(page0Tp, page1Tp);

    $('#sptp-view').innerHTML =
      `<div class="sptp-note">总 SP/TP 是角色共用点数；技能类型一、技能类型二分别计算已学习技能后的剩余点。</div>` +
      `<div class="sptp-grid">` +
        sptpMetricCard('总点数', [
          ['总 SP', d.totalSp],
          ['总 TP', d.totalTp],
        ]) +
        sptpMetricCard('附加点', [
          ['附加 SP', d.bonusSp],
          ['附加 TP', d.bonusTp],
        ]) +
        sptpMetricCard('技能类型一', [
          ['剩余 SP', page0Sp],
          ['剩余 TP', page0Tp],
        ], page0Current ? '当前使用' : '') +
        sptpMetricCard('技能类型二', [
          ['剩余 SP', page1Sp],
          ['剩余 TP', page1Tp],
        ], page1Locked ? '未解锁' : (page1Current ? '当前使用' : '')) +
      `</div>`;
    $('#sp-now').textContent = `当前附加 SP ${d.bonusSp} / TP ${d.bonusTp}`;

    const zeroBtn = $('#btn-zero-sptp');
    if (zeroBtn) {
      zeroBtn.disabled = targetSp === 0 && targetTp === 0;
      zeroBtn.title = zeroBtn.disabled
        ? '全局占用最多的技能方案已经没有可归零的剩余 SP/TP'
        : `按两页中占用最多的方案扣减：SP -${targetSp.toLocaleString()} / TP -${targetTp}`;
    }
  } catch (e) {
    $('#sptp-view').textContent = e.message;
  }
}

function sptpMetricCard(title, rows, badge) {
  const badgeHtml = badge ? `<span class="sptp-badge">${escapeHtml(badge)}</span>` : '';
  return `<div class="sptp-card"><div class="sptp-card-title">${escapeHtml(title)}${badgeHtml}</div>` +
    rows.map(([label, value]) =>
      `<div class="sptp-line"><span>${escapeHtml(label)}</span><b>${Number(value || 0).toLocaleString()}</b></div>`
    ).join('') +
    `</div>`;
}

async function adjustSp() {
  if (!currentChar) return;
  const sp = parseInt($('#sp-input').value, 10) || 0;
  const tp = parseInt($('#tp-input').value, 10) || 0;
  if (!sp && !tp) return toast('SP/TP 至少填写一个非零值', true);
  try {
    await post(`/api/characters/${currentChar.characterId}/sp`, { sp, tp });
    toast('附加点已调整');
    $('#sp-input').value = 0;
    $('#tp-input').value = 0;
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}

async function zeroRemainingSpTp() {
  if (!currentChar) return;
  try {
    await post(`/api/characters/${currentChar.characterId}/sp/zero-remaining`);
    toast('全局占用最多的技能方案剩余 SP/TP 已归 0');
    $('#sp-input').value = 0;
    $('#tp-input').value = 0;
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}
