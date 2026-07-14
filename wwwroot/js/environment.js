let runtimeReady = false;
let runtimeStatus = null;
let runtimeSourceEpoch = 0;
let runtimePollTimer = 0;
let runtimeConfiguring = false;
let runtimeLoggingIn = false;
const RUNTIME_SOURCE_STORAGE_KEY = 'dfo-gm-runtime-source';

function canChangeRuntimeSource() {
  return Boolean(runtimeStatus && runtimeStatus.canChangeSource);
}

function readStoredRuntimeSource() {
  try {
    const value = JSON.parse(localStorage.getItem(RUNTIME_SOURCE_STORAGE_KEY));
    if (!value || typeof value.databasePath !== 'string' || typeof value.pvfPath !== 'string') return null;

    const databasePath = value.databasePath.trim();
    const pvfPath = value.pvfPath.trim();
    return databasePath && pvfPath ? { databasePath, pvfPath } : null;
  } catch (_) {
    return null;
  }
}

function saveRuntimeSource(databasePath, pvfPath) {
  try {
    localStorage.setItem(RUNTIME_SOURCE_STORAGE_KEY, JSON.stringify({ databasePath, pvfPath }));
  } catch (_) {
    // Source selection still works when browser storage is unavailable.
  }
}

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

function setLoginState(text, isError) {
  const state = $('#login-state');
  state.textContent = text || '';
  state.className = isError ? 'hint err' : 'hint';
}

function updateRuntimeActionButtons(status) {
  $('#btn-runtime-source').classList.toggle('hidden', !(status && status.canChangeSource));
  $('#btn-logout').classList.toggle('hidden', !(status && status.authenticationRequired && status.authenticated));
}

function updateRuntimeSourceInputs(status, force) {
  if (!status) return;
  const database = $('#runtime-database-path');
  const pvf = $('#runtime-pvf-path');
  if (force || !database.value) database.value = status.database || '';
  if (force || !pvf.value) pvf.value = status.pvf || '';
}

function showLoginPanel() {
  hideRuntimeSourcePanel();
  $('#login-panel').classList.remove('hidden');
  setTimeout(() => $('#login-password').focus(), 0);
}

function hideLoginPanel() {
  $('#login-panel').classList.add('hidden');
}

function showRuntimeSourcePanel(forceValues) {
  if (!canChangeRuntimeSource()) return;
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

function stopRuntimeWorkspace() {
  if (!runtimeReady) return;
  runtimeReady = false;
  runtimeSourceEpoch++;
  resetRuntimeWorkspace();
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
  const authenticationRequired = Boolean(status && status.authenticationRequired);
  const authenticated = !authenticationRequired || Boolean(status && status.authenticated);
  renderRuntimeStatus(status);
  updateRuntimeActionButtons(status);

  if (authenticationRequired && !authenticated) {
    clearRuntimePoll();
    stopRuntimeWorkspace();
    hideRuntimeSourcePanel();
    showLoginPanel();
    return;
  }

  hideLoginPanel();
  if (status && status.ready) {
    clearRuntimePoll();
    if (!runtimeReady) {
      runtimeReady = true;
      startRuntimeWorkspace();
    }
    return;
  }

  stopRuntimeWorkspace();
  if (status && status.error)
    setRuntimeSourceState(status.error, true);
  else if (status && status.hasError)
    setRuntimeSourceState('数据源加载失败', true);
  else if (status && status.loading)
    setRuntimeSourceState('PVF 索引构建中…', false);
  else
    setRuntimeSourceState('', false);

  if (canChangeRuntimeSource())
    showRuntimeSourcePanel(!status || !status.configured);
  else
    hideRuntimeSourcePanel();

  clearRuntimePoll();
  if (status && status.loading)
    runtimePollTimer = setTimeout(refreshRuntimeEnvironment, 1000);
}

function handleAuthenticationRequired() {
  if (!(runtimeStatus && runtimeStatus.authenticationRequired)) return;

  applyRuntimeStatus({
    configured: Boolean(runtimeStatus && runtimeStatus.configured),
    ready: false,
    loading: false,
    indexReady: false,
    authenticationRequired: true,
    authenticated: false,
    canChangeSource: false,
  });
}

async function refreshRuntimeEnvironment() {
  try {
    const status = await api('/api/status');
    applyRuntimeStatus(status);
    return status;
  } catch (e) {
    clearRuntimePoll();
    stopRuntimeWorkspace();
    runtimeStatus = null;
    renderRuntimeStatus(null);
    updateRuntimeActionButtons(null);
    hideRuntimeSourcePanel();
    hideLoginPanel();
    return null;
  }
}

async function configureRuntimeEnvironment() {
  if (runtimeConfiguring || !canChangeRuntimeSource()) return;

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
    saveRuntimeSource(result.status.database || databasePath, result.status.pvf || pvfPath);
    runtimeReady = false;
    runtimeSourceEpoch++;
    resetRuntimeWorkspace();
    applyRuntimeStatus({
      ...result.status,
      authenticationRequired: false,
      authenticated: true,
      canChangeSource: true,
    });
  } catch (e) {
    setRuntimeSourceState(e.message, true);
  } finally {
    runtimeConfiguring = false;
    $('#btn-load-runtime-source').disabled = false;
    if (runtimeReady) $('#btn-close-runtime-source').classList.remove('hidden');
  }
}

async function loginRuntime() {
  if (runtimeLoggingIn) return;

  const password = $('#login-password').value;
  if (!password) {
    setLoginState('请输入密码', true);
    return;
  }

  runtimeLoggingIn = true;
  $('#btn-login').disabled = true;
  setLoginState('正在登录…', false);
  try {
    await post('/api/auth/login', { password });
    $('#login-password').value = '';
    setLoginState('', false);
    const status = await refreshRuntimeEnvironment();
    if (!status) {
      setLoginState('后端无响应', true);
      showLoginPanel();
    }
  } catch (e) {
    setLoginState(e.message, true);
  } finally {
    runtimeLoggingIn = false;
    $('#btn-login').disabled = false;
  }
}

async function logoutRuntime() {
  try {
    await post('/api/auth/logout');
    handleAuthenticationRequired();
    await refreshRuntimeEnvironment();
  } catch (e) {
    toast(e.message, true);
  }
}

function bindRuntimeEnvironment() {
  $('#btn-runtime-source').onclick = () => showRuntimeSourcePanel(true);
  $('#btn-logout').onclick = logoutRuntime;
  $('#btn-close-runtime-source').onclick = () => {
    if (runtimeReady && !runtimeConfiguring) hideRuntimeSourcePanel();
  };
  $('#runtime-source-form').onsubmit = (event) => {
    event.preventDefault();
    configureRuntimeEnvironment();
  };
  $('#login-form').onsubmit = (event) => {
    event.preventDefault();
    loginRuntime();
  };
}

async function initializeRuntimeEnvironment() {
  const status = await refreshRuntimeEnvironment();
  if (!status || status.authenticationRequired || status.configured || !status.canChangeSource) return;

  const source = readStoredRuntimeSource();
  if (!source) return;

  $('#runtime-database-path').value = source.databasePath;
  $('#runtime-pvf-path').value = source.pvfPath;
  return configureRuntimeEnvironment();
}
