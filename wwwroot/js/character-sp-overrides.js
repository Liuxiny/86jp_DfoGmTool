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

    const zeroBtn = $('#btn-zero-sptp');
    if (zeroBtn) {
      zeroBtn.disabled = !((d.remainingSp > 0 || d.remainingTp > 0)
        && d.remainingSp <= d.bonusSp
        && d.remainingTp <= d.bonusTp);
      zeroBtn.title = zeroBtn.disabled ? '只有剩余 SP/TP 不大于附加 SP/TP 时才能归零' : '';
    }
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
    toast('剩余 SP/TP 已归 0');
    $('#sp-input').value = 0;
    $('#tp-input').value = 0;
    loadSpTp();
  } catch (e) {
    toast(e.message, true);
  }
}
