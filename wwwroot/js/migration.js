let a12A21Busy = false;
let a12A21Preview = null;

function a12A21Payload(value) {
  if (!value || typeof value !== 'object') return {};
  const preview = value.preview && typeof value.preview === 'object' ? value.preview : {};
  const report = value.report && typeof value.report === 'object' ? value.report : {};
  return { ...value, ...preview, ...report };
}

function a12A21Number(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number.toLocaleString() : '';
}

function a12A21Summary(value) {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'number') return a12A21Number(value);
  if (Array.isArray(value)) {
    const details = value.slice(0, 20).map((item) => {
      const text = typeof item === 'object' ? JSON.stringify(item) : item;
      return `<li>${escapeHtml(text)}</li>`;
    }).join('');
    return `${a12A21Number(value.length)} 项${details ? `<ul>${details}</ul>` : ''}`;
  }
  if (typeof value !== 'object') return escapeHtml(value);

  const count = value.count ?? value.total ?? value.rows;
  if (count !== undefined) return a12A21Number(count);
  return Object.entries(value)
    .map(([key, item]) => `${escapeHtml(key)}：${a12A21Summary(item)}`)
    .join('；') || '—';
}

function a12A21Field(value, fallback) {
  return value === null || value === undefined || value === '' ? fallback : value;
}

function a12A21SourceInfo(data) {
  return data.source && typeof data.source === 'object' ? data.source : {};
}

function a12A21PvfExclusions(data) {
  const issues = Array.isArray(data.issues)
    ? data.issues.filter((issue) => issue && String(issue.code || '').toLowerCase() === 'pvf_missing')
    : [];
  if (!issues.length) return '';
  const visible = issues.map((issue) => {
    const table = escapeHtml(issue.table || '未知表');
    const sourceId = escapeHtml(issue.sourceId ?? '—');
    const itemId = escapeHtml(issue.itemId ?? '—');
    const message = escapeHtml(issue.message || '当前 A21 PVF 不包含该物品。');
    return `<li>表 ${table}；源记录 ${sourceId}；物品 ${itemId}：${message}</li>`;
  }).join('');
  return `<div class="migration-pvf-exclusions"><b>PVF 排除清单（不会迁移，不影响升级）</b><ul>${visible}</ul></div>`;
}

function renderA12A21Summary(result, title) {
  const data = a12A21Payload(result);
  const source = a12A21SourceInfo(data);
  const sourcePath = a12A21Field(source.path || source.databasePath || data.databasePath, '未返回');
  const sourceSchema = a12A21Field(source.schemaVersion ?? data.sourceSchemaVersion, '未识别（仅诊断）');
  const sourceBaseline = a12A21Field(source.baselineId || source.baseline, '未识别（仅诊断）');
  const sourceHash = a12A21Field(source.sha256 || source.sourceSha256 || data.sourceSha256, '未返回');
  const counts = data.counts || data.dataCounts || data.sourceCounts || data.data || {};
  const migratable = data.migratable ?? data.migratableCount ?? data.migratableRows ?? data.migrated ?? {};
  const skipped = data.skipped ?? data.skippedCount ?? data.skippedRows ?? {};
  const recognized = source.recognized ?? source.valid ?? data.sourceRecognized;
  const state = recognized === false ? '不识别' : recognized === true ? '已识别' : '已检查';

  return `<div class="migration-report-title">${escapeHtml(title)}</div>` +
    `<div><b>源识别：</b>${state}；路径 ${escapeHtml(sourcePath)}；版本标识 ${escapeHtml(sourceSchema)}；基线 ${escapeHtml(sourceBaseline)}</div>` +
    `<div><b>源 SHA-256：</b>${escapeHtml(sourceHash)}</div>` +
    `<div><b>数据量：</b>${a12A21Summary(counts)}</div>` +
    `<div><b>可迁移：</b>${a12A21Summary(migratable)}</div>` +
    `<div><b>跳过：</b>${a12A21Summary(skipped)}</div>` +
    a12A21PvfExclusions(data);
}

function setA12A21State(text, isError) {
  const state = $('#a12-a21-migration-state');
  state.textContent = text || '';
  state.className = isError ? 'hint err' : 'hint';
}

function setA12A21Buttons() {
  const previewButton = $('#btn-preview-a12-a21');
  const executeButton = $('#btn-execute-a12-a21');
  if (!previewButton || !executeButton) return;
  previewButton.disabled = a12A21Busy;
  const data = a12A21Payload(a12A21Preview);
  executeButton.disabled = a12A21Busy || !a12A21Preview
    || data.success === false || data.canExecute === false || !$('#a12-a21-user-backed-up').checked;
}

function invalidateA12A21Preview() {
  a12A21Preview = null;
  $('#a12-a21-migration-preview').classList.add('hidden');
  $('#a12-a21-migration-report').classList.add('hidden');
  setA12A21Buttons();
}

function validateA12A21Input() {
  const databasePath = $('#runtime-database-path').value.trim();
  const pvfPath = $('#runtime-pvf-path').value.trim();
  if (!databasePath || !pvfPath) throw new Error('请先填写数据库和 A21 PVF 路径');
  return { databasePath, pvfPath };
}

function renderA12A21Preview(result) {
  const panel = $('#a12-a21-migration-preview');
  panel.innerHTML = renderA12A21Summary(result, '升级预览（未写入任何文件）');
  panel.classList.remove('hidden');
}

function renderA12A21Report(result) {
  const panel = $('#a12-a21-migration-report');
  panel.innerHTML = renderA12A21Summary(result, '升级执行报告') +
    `<div><b>原文件：</b>已在同一路径完成 A21 schema-v5 替换</div>` +
    `<div class="hint">请按报告确认结果后重启服务端并让玩家重新登录。</div>`;
  panel.classList.remove('hidden');
}

function showA12A21MigrationRequired(preview) {
  a12A21Preview = preview || null;
  if (a12A21Preview) renderA12A21Preview(a12A21Preview);
  setA12A21State(a12A21Preview && a12A21Preview.success === false
    ? `迁移预览被阻止：${a12A21Preview.error || '请先处理数据库 sidecar。'}`
    : '已识别可迁移旧库，数据库已释放，请预览/升级。',
    Boolean(a12A21Preview && a12A21Preview.success === false));
  setA12A21Buttons();
}

function showA12A21DatabaseUnusable(message) {
  invalidateA12A21Preview();
  setA12A21State(message || '数据库不可用；请移除该文件等待服务端自动生成，或选择正确数据库。', true);
}

async function previewA12ToA21() {
  if (a12A21Busy) return;
  let input;
  try { input = validateA12A21Input(); } catch (error) { setA12A21State(error.message, true); return; }

  invalidateA12A21Preview();
  a12A21Busy = true;
  setA12A21State('正在预览，预览不会写入任何文件…', false);
  setA12A21Buttons();
  try {
    const result = await post('/api/migrations/a12-to-a21/preview', {
      databasePath: input.databasePath,
      pvfPath: input.pvfPath,
      userBackedUp: $('#a12-a21-user-backed-up').checked,
    });
    a12A21Preview = result;
    renderA12A21Preview(result);
    setA12A21State('预览完成，请核对报告并确认已自行备份后执行。', false);
  } catch (error) {
    setA12A21State(`预览失败：${error.message}`, true);
  } finally {
    a12A21Busy = false;
    setA12A21Buttons();
  }
}

async function executeA12ToA21() {
  if (a12A21Busy || !a12A21Preview) return;
  let input;
  try { input = validateA12A21Input(); } catch (error) { setA12A21State(error.message, true); return; }
  if (!$('#a12-a21-user-backed-up').checked) {
    setA12A21State('请先勾选“我已自行备份数据库”。', true);
    return;
  }

  const confirmation = prompt('执行前请再次确认。请输入升级确认词 update');
  if (confirmation === null) return;

  a12A21Busy = true;
  setA12A21State('正在升级并替换原文件，请等待事务完成…', false);
  setA12A21Buttons();
  try {
    const result = await post('/api/migrations/a12-to-a21/execute', {
      databasePath: input.databasePath,
      pvfPath: input.pvfPath,
      userBackedUp: true,
      confirmText: confirmation,
    });
    renderA12A21Report(result);
    a12A21Preview = null;
    setA12A21State('升级完成，正在用同一路径和 PVF 重新加载 GM…', false);
    await configureRuntimeEnvironment();
    if (runtimeStatus && runtimeStatus.ready && canChangeRuntimeSource())
      showRuntimeSourcePanel(false);
    setA12A21State('升级完成，GM 已尝试重新加载同一路径和 PVF；请重启服务端。', false);
    toast('S4A12 → S4A21 升级完成');
  } catch (error) {
    setA12A21State(`执行失败，原文件应保持不变：${error.message}`, true);
    toast(`升级失败：${error.message}`, true);
  } finally {
    a12A21Busy = false;
    setA12A21Buttons();
  }
}

function updateA12A21MigrationEnvironment(status) {
  if (!status || !status.ready) {
    if (!a12A21Busy)
      setA12A21State(status && status.loading ? '可直接预览当前库；请等待其他环境状态更新。' : '可直接预览当前库，无需先通过 A21 结构门禁。', false);
  }
  setA12A21Buttons();
}

function bindA12A21Migration() {
  $('#runtime-database-path').addEventListener('input', invalidateA12A21Preview);
  $('#runtime-pvf-path').addEventListener('input', invalidateA12A21Preview);
  $('#a12-a21-user-backed-up').onchange = setA12A21Buttons;
  $('#btn-preview-a12-a21').onclick = previewA12ToA21;
  $('#btn-execute-a12-a21').onclick = executeA12ToA21;
  updateA12A21MigrationEnvironment(runtimeStatus);
}

bindA12A21Migration();
