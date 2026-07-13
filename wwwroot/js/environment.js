let runtimeReady = false;
let runtimeStatus = null;
let runtimeSourceEpoch = 0;
let runtimePollTimer = 0;
let runtimeConfiguring = false;

function clearRuntimePoll() {
  if (runtimePollTimer) {
    clearTimeout(runtimePollTimer);
    runtimePollTimer = 0;
  }
}

function setRuntimeSourceState(text, isError) {
  const state = $('#runtime-source-state');
  state.textContent = text || '';
  state.className = isError ? 'hint err' : 'hint';
}

function updateRuntimeSourceInputs(status, force) {
  if (!status) return;
  const database = $('#runtime-database-path');
  const pvf = $('#runtime-pvf-path');
  if (force || !database.value) database.value = status.database || '';
  if (force || !pvf.value) pvf.value = status.pvf || '';
}

function showRuntimeSourcePanel(forceValues) {
  updateRuntimeSourceInputs(runtimeStatus, forceValues);
  $('#runtime-source-panel').classList.remove('hidden');
  $('#btn-close-runtime-source').classList.toggle('hidden', !runtimeReady || runtimeConfiguring);
}

function hideRuntimeSourcePanel() {
  $('#runtime-source-panel').classList.add('hidden');
}

function resetRuntimeWorkspace() {
  if (typeof resetAccountWorkspace === 'function') resetAccountWorkspace();
  giveCategory = null;
  giveNavExpanded.clear();
  $('#give-category-nav').innerHTML = '';
  $('#search-results tbody').innerHTML = '';
  $('#give-total').textContent = '';
  $('#workspace').classList.add('hidden');
  $('#runtime-notice').classList.add('hidden');
}

function startRuntimeWorkspace() {
  const epoch = runtimeSourceEpoch;
  $('#workspace').classList.remove('hidden');
  $('#runtime-notice').classList.remove('hidden');
  hideRuntimeSourcePanel();
  loadGiveCategories(epoch).catch((e) => toast(e.message, true));
  loadAccounts(epoch).catch((e) => toast(e.message, true));
}

function applyRuntimeStatus(status) {
  runtimeStatus = status;
  renderRuntimeStatus(status);

  if (status && status.ready) {
    clearRuntimePoll();
    if (!runtimeReady) {
      runtimeReady = true;
      startRuntimeWorkspace();
    }
    return;
  }

  if (runtimeReady) {
    runtimeReady = false;
    runtimeSourceEpoch++;
    resetRuntimeWorkspace();
  }

  if (status && status.error)
    setRuntimeSourceState(status.error, true);
  else if (status && status.loading)
    setRuntimeSourceState('PVF 索引构建中…', false);
  else
    setRuntimeSourceState('', false);

  showRuntimeSourcePanel(!status || !status.configured);
  clearRuntimePoll();
  if (status && status.loading)
    runtimePollTimer = setTimeout(refreshRuntimeEnvironment, 1000);
}

async function refreshRuntimeEnvironment() {
  try {
    const status = await api('/api/status');
    applyRuntimeStatus(status);
  } catch (e) {
    runtimeReady = false;
    resetRuntimeWorkspace();
    renderRuntimeStatus(null);
    setRuntimeSourceState('后端无响应', true);
    showRuntimeSourcePanel(false);
  }
}

async function configureRuntimeEnvironment() {
  if (runtimeConfiguring) return;

  const databasePath = $('#runtime-database-path').value.trim();
  const pvfPath = $('#runtime-pvf-path').value.trim();
  if (!databasePath || !pvfPath) {
    setRuntimeSourceState('请填写数据库和 PVF 路径', true);
    return;
  }

  setRuntimeSourceState('正在加载…', false);
  runtimeConfiguring = true;
  $('#btn-load-runtime-source').disabled = true;
  $('#btn-close-runtime-source').classList.add('hidden');
  try {
    const result = await post('/api/environment', { databasePath, pvfPath });
    runtimeReady = false;
    runtimeSourceEpoch++;
    resetRuntimeWorkspace();
    applyRuntimeStatus(result.status);
  } catch (e) {
    setRuntimeSourceState(e.message, true);
  } finally {
    runtimeConfiguring = false;
    $('#btn-load-runtime-source').disabled = false;
    if (runtimeReady) $('#btn-close-runtime-source').classList.remove('hidden');
  }
}

function bindRuntimeEnvironment() {
  $('#btn-runtime-source').onclick = () => showRuntimeSourcePanel(true);
  $('#btn-close-runtime-source').onclick = () => {
    if (runtimeReady && !runtimeConfiguring) hideRuntimeSourcePanel();
  };
  $('#runtime-source-form').onsubmit = (event) => {
    event.preventDefault();
    configureRuntimeEnvironment();
  };
}

function initializeRuntimeEnvironment() {
  return refreshRuntimeEnvironment();
}
