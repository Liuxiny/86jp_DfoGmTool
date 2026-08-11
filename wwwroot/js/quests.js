// ---- 任务 ----

const QUEST_PAGE_SIZE = 20;
let clearedQuests = [];
let clearedPage = 0;
let clearedCharacterId = null;
let extraEquipmentSlotQuestStatus = null;

async function loadQuests() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  let data;
  try {
    data = await api(`/api/characters/${currentChar.characterId}/quests`);
  } catch (e) {
    toast(e.message, true);
    return;
  }
  if (epoch !== selectEpoch) return;
  const tbody = $('#quest-table tbody');
  tbody.innerHTML = '';
  for (const quest of data.quests) {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${quest.slot}</td><td>${quest.questId}</td>
      <td>${escapeHtml(quest.name || '')}</td><td>${quest.triggerValue}</td>
      <td><button class="mini primary">标记可交</button> <button class="mini primary">强制完成</button></td>`;
    const [readyBtn, completeBtn] = tr.querySelectorAll('button');
    readyBtn.onclick = () => questAction(quest.questId, 'ready', '已标记可交', quest.activationId);
    completeBtn.onclick = () => questAction(quest.questId, 'complete', '已强制完成(未发奖励)');
    tbody.appendChild(tr);
  }
  if (data.quests.length === 0)
    tbody.innerHTML = '<tr><td colspan="5" class="hint">没有进行中的任务</td></tr>';
}

function refreshQuestViews() {
  // 完成类操作可能连带改转职/称号/属性(觉醒成就 jcq=2), 直接整页刷新,
  // 头部职业名/转职卡/属性表一并更新
  if (currentChar) {
    refreshHeader();
    return;
  }
  loadQuests();
  loadAllVisibleQuests();
  loadMainQuests();
  loadAchieveQuests();
  loadClearedQuests();
}

async function loadClearedQuests() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  const characterId = currentChar.characterId;
  if (clearedCharacterId !== characterId) {
    clearedCharacterId = characterId;
    clearedPage = 0;
  }
  let data;
  try {
    data = await api(`/api/characters/${characterId}/quests/cleared`);
  } catch (e) {
    toast(e.message, true);
    return;
  }
  if (epoch !== selectEpoch) return;
  $('#cleared-count').textContent = `共 ${data.count} 个已完成任务`;
  clearedQuests = data.quests;
  renderClearedQuests();
}

function renderClearedQuests() {
  const tbody = $('#cleared-table tbody');
  tbody.innerHTML = '';
  const pageCount = Math.max(1, Math.ceil(clearedQuests.length / QUEST_PAGE_SIZE));
  clearedPage = Math.min(Math.max(clearedPage, 0), pageCount - 1);
  const pageItems = clearedQuests.slice(
    clearedPage * QUEST_PAGE_SIZE,
    (clearedPage + 1) * QUEST_PAGE_SIZE,
  );

  for (const quest of pageItems) {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${escapeHtml(quest.gradeLabel || '?')}</td>
      <td>${escapeHtml(quest.regionLabel || '')}</td><td>${quest.minLevel || ''}</td>
      <td>${quest.questId}</td><td>${escapeHtml(quest.name || '')}</td>
      <td><button class="mini danger">取消完成</button></td>`;
    tr.querySelector('button').onclick = () => questAction(quest.questId, 'unclear', '已取消完成标记');
    tbody.appendChild(tr);
  }
  if (clearedQuests.length === 0)
    tbody.innerHTML = '<tr><td colspan="6" class="hint">没有已完成任务</td></tr>';

  renderTaskPager($('#cleared-pager'), clearedPage, pageCount, clearedQuests.length, (page) => {
    clearedPage = page;
    renderClearedQuests();
  });
}

async function questAction(questId, action, message, activationId = null) {
  try {
    const activationQuery = action === 'ready'
      ? `?activationId=${encodeURIComponent(activationId || '')}`
      : '';
    await post(`/api/characters/${currentChar.characterId}/quests/${questId}/${action}${activationQuery}`);
    toast(message);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  }
}

// ---- 主线/成就总览(共用区域侧栏+链树组件) ----

const questViews = {
  all: {
    endpoint: 'all-visible', navSel: '#all-quest-region-nav', tableSel: '#all-quest-table',
    pagerSel: '#all-quest-pager', countSel: '#all-quest-count', data: null, activeRegion: null, page: 0,
    characterId: null,
  },
  main: { endpoint: 'main', navSel: '#main-region-nav', tableSel: '#main-quest-table', data: null, activeRegion: null },
  achieve: {
    endpoint: 'achievement', navSel: '#achieve-region-nav', tableSel: '#achieve-quest-table',
    pagerSel: '#achieve-quest-pager', data: null, activeRegion: null, page: 0,
    characterId: null,
  },
};

async function loadQuestView(key) {
  if (!currentChar) return;
  const epoch = selectEpoch;
  const view = questViews[key];
  const characterId = currentChar.characterId;
  if (view.pagerSel && view.characterId !== characterId) {
    view.characterId = characterId;
    view.activeRegion = null;
    view.page = 0;
    if (key === 'all')
      extraEquipmentSlotQuestStatus = null;
  }
  try {
    const data = await api(`/api/characters/${characterId}/quests/${view.endpoint}`);
    if (epoch !== selectEpoch) return;
    view.data = data;
    if (view.countSel) {
      const total = (data.regions || []).reduce((sum, region) => sum + (region.total || 0), 0);
      $(view.countSel).textContent = `共 ${total} 个当前可见任务`;
    }
    renderRegionNav(key);
    renderQuestTree(key);
    if (key === 'all') loadExtraEquipmentSlotQuestStatus();
  } catch (e) {
    toast(e.message, true);
  }
}

const loadAllVisibleQuests = () => loadQuestView('all');
const loadMainQuests = () => loadQuestView('main');
const loadAchieveQuests = () => loadQuestView('achieve');

const ALL_QUEST_REGION = '__all__';
const DAILY_QUEST_REGION = '__daily__';
const DAILY_QUEST_GRADES = new Set(['daily', 'daily random', 'special daily']);
let allQuestDisplayMode = 'region';

function isCompletedQuest(quest) {
  return quest && quest.status === '已完成';
}

function isDailyQuest(quest) {
  return quest && DAILY_QUEST_GRADES.has(quest.grade || '');
}

function sortQuestsForAllList(quests) {
  return quests.slice().sort((a, b) => (a.minLevel - b.minLevel) || (a.questId - b.questId));
}

function buildVirtualAllQuestRegions(view) {
  const sourceRegions = view.data && view.data.regions ? view.data.regions : [];
  if (view !== questViews.all) return sourceRegions;

  const allQuests = sortQuestsForAllList(sourceRegions.flatMap((region) => region.quests || []));
  const dailyQuests = allQuests.filter(isDailyQuest);
  const regions = [];
  if (allQuests.length > 0) {
    regions.push({
      region: ALL_QUEST_REGION,
      regionLabel: '全部',
      group: '全部任务',
      minLevel: allQuests.reduce((min, q) => Math.min(min, q.minLevel || 0), 99),
      total: allQuests.length,
      completed: allQuests.filter(isCompletedQuest).length,
      quests: allQuests,
    });
  }
  if (dailyQuests.length > 0) {
    regions.push({
      region: DAILY_QUEST_REGION,
      regionLabel: '每日任务',
      group: '全部任务',
      minLevel: dailyQuests.reduce((min, q) => Math.min(min, q.minLevel || 0), 99),
      total: dailyQuests.length,
      completed: dailyQuests.filter((q) => q.status !== '未完成').length,
      quests: dailyQuests,
    });
  }

  if (allQuestDisplayMode === 'type') {
    const byType = new Map();
    for (const quest of allQuests) {
      if (isDailyQuest(quest)) continue;
      const key = quest.grade || '';
      if (!byType.has(key)) byType.set(key, []);
      byType.get(key).push(quest);
    }
    const typeRegions = [...byType.entries()]
      .sort((a, b) => (gradeSortKey(a[0]) - gradeSortKey(b[0])) || String(a[0]).localeCompare(String(b[0]), 'zh-CN'));
    for (const [grade, quests] of typeRegions) {
      regions.push({
        region: `__type__:${grade}`,
        regionLabel: questGradeLabel(grade),
        group: '按类型',
        minLevel: quests.reduce((min, q) => Math.min(min, q.minLevel || 0), 99),
        total: quests.length,
        completed: quests.filter(isCompletedQuest).length,
        quests: sortQuestsForAllList(quests),
      });
    }
    return regions;
  }

  for (const region of sourceRegions) {
    const quests = (region.quests || []).filter((quest) => !isDailyQuest(quest));
    if (quests.length === 0) continue;
    regions.push({
      ...region,
      total: quests.length,
      completed: quests.filter(isCompletedQuest).length,
      quests,
    });
  }
  return regions;
}

function questGradeLabel(grade) {
  const labels = {
    epic: '主线',
    side: '外传',
    normal: '普通',
    sub: '支线',
    training: '训练',
    achievement: '成就',
    daily: '每日',
    'daily random': '随机每日',
    'special daily': '特殊每日',
    'normaly repeat': '重复',
    'common unique': '职业',
    system: '系统',
    '': '?',
  };
  return labels[grade || ''] || grade || '?';
}

function gradeSortKey(grade) {
  const order = {
    epic: 10,
    side: 20,
    normal: 30,
    sub: 35,
    training: 40,
    achievement: 50,
    daily: 60,
    'daily random': 61,
    'special daily': 62,
    'normaly repeat': 70,
    'common unique': 80,
    system: 90,
  };
  return order[grade || ''] || 999;
}

function setAllQuestDisplayMode() {
  const select = $('#all-quest-display-mode');
  allQuestDisplayMode = select ? select.value : 'region';
  const view = questViews.all;
  if (view) {
    view.activeRegion = null;
    view.page = 0;
  }
  renderRegionNav('all');
  renderQuestTree('all');
}

function activeQuestRegion(key) {
  const view = questViews[key];
  return buildVirtualAllQuestRegions(view).find((r) => r.region === view.activeRegion) || null;
}

function updateAllQuestActionButtons(region) {
  const groupButtons = [
    $('#btn-complete-current-main-all'),
    $('#btn-complete-current-side-all'),
    $('#btn-complete-current-system'),
    $('#btn-complete-current-achievement-no-item'),
    $('#btn-complete-profession-quests'),
    $('#btn-complete-equipment-slot-quests'),
  ].filter(Boolean);
  const resetBtn = $('#btn-reset-daily-quests');
  if (!resetBtn) return;
  const isDailyList = region && region.region === DAILY_QUEST_REGION;
  for (const btn of groupButtons) btn.classList.toggle('hidden', isDailyList);
  resetBtn.classList.toggle('hidden', !isDailyList);
  for (const btn of groupButtons) btn.disabled = !currentChar || isDailyList;
  const professionBtn = $('#btn-complete-profession-quests');
  if (professionBtn && currentChar)
    professionBtn.disabled = isDailyList || currentChar.level < 15 || currentChar.job === 9 || currentChar.job === 10;
  const slotBtn = $('#btn-complete-equipment-slot-quests');
  if (slotBtn) {
    const canCompleteSlots = !!extraEquipmentSlotQuestStatus?.canComplete;
    slotBtn.disabled = isDailyList || !canCompleteSlots;
    slotBtn.title = canCompleteSlots
      ? `剩余 ${extraEquipmentSlotQuestStatus.residualCount || 0} 个左右槽任务`
      : '只有已开启左右槽且相关任务有残留时可用';
  }
  resetBtn.disabled = !region || !isDailyList || region.quests.length === 0;
}

async function loadExtraEquipmentSlotQuestStatus() {
  const btn = $('#btn-complete-equipment-slot-quests');
  if (!currentChar || !btn) return;
  const epoch = selectEpoch;
  try {
    const status = await api(`/api/characters/${currentChar.characterId}/quests/equipment-slots/status`);
    if (epoch !== selectEpoch) return;
    extraEquipmentSlotQuestStatus = status;
    updateAllQuestActionButtons(activeQuestRegion('all'));
  } catch (e) {
    extraEquipmentSlotQuestStatus = null;
    updateAllQuestActionButtons(activeQuestRegion('all'));
  }
}

function renderRegionNav(key) {
  const view = questViews[key];
  const nav = $(view.navSel);
  nav.innerHTML = '';
  if (!view.data) return;
  const regions = buildVirtualAllQuestRegions(view);

  if (view.activeRegion && !regions.some((r) => r.region === view.activeRegion)) {
    view.activeRegion = null;
    if (view.pagerSel) view.page = 0;
  }
  if (!view.activeRegion && regions.length > 0) {
    view.activeRegion = regions[0].region;
    if (view.pagerSel) view.page = 0;
  }

  let lastGroup = null;
  for (const region of regions) {
    const group = region.group || '区域 (按等级排序)';
    if (group !== lastGroup) {
      const title = document.createElement('div');
      title.className = 'group-title';
      title.textContent = group;
      nav.appendChild(title);
      lastGroup = group;
    }

    const el = document.createElement('div');
    el.className = 'cat' + (view.activeRegion === region.region ? ' active' : '');
    el.innerHTML = `<span>${escapeHtml(region.regionLabel)}</span>
      <span class="cnt">${region.completed}/${region.total}</span>`;
    el.title = `Lv.${region.minLevel}+`;
    el.onclick = () => {
      view.activeRegion = region.region;
      if (view.pagerSel) view.page = 0;
      renderRegionNav(key);
      renderQuestTree(key);
    };
    nav.appendChild(el);
  }
  if (key === 'all') updateAllQuestActionButtons(activeQuestRegion(key));
}

// 展开状态按 "区域:链头ID" 记忆, 刷新数据后保持
const expandedChains = new Set();

// 区域内按前置关系组链: 父节点 = 第一个同区域内的前置任务; 无区域内前置的是链头
function buildQuestChains(quests) {
  const byId = new Map(quests.map((q) => [q.questId, q]));
  const children = new Map();
  const roots = [];
  for (const quest of quests) {
    const parentId = quest.preRequired.map((p) => p.questId).find((pid) => byId.has(pid));
    if (parentId != null) {
      if (!children.has(parentId)) children.set(parentId, []);
      children.get(parentId).push(quest);
    } else {
      roots.push(quest);
    }
  }
  const order = (a, b) => (a.minLevel - b.minLevel) || (a.questId - b.questId);
  roots.sort(order);
  for (const list of children.values()) list.sort(order);
  return { roots, children };
}

function chainStats(root, children, visited) {
  let total = 0, done = 0;
  const stack = [root];
  while (stack.length > 0) {
    const quest = stack.pop();
    if (visited.has(quest.questId)) continue;
    visited.add(quest.questId);
    total++;
    if (isCompletedQuest(quest)) done++;
    for (const child of children.get(quest.questId) || []) stack.push(child);
  }
  return { total, done };
}

// 分页按任务数量装入完整任务链。链不跨页，单条超长链允许独占一页。
function paginateQuestRoots(roots, children) {
  const pages = [];
  let page = [];
  let taskCount = 0;

  for (const root of roots) {
    const chainSize = chainStats(root, children, new Set()).total;
    if (page.length > 0 && taskCount + chainSize > QUEST_PAGE_SIZE) {
      pages.push(page);
      page = [];
      taskCount = 0;
    }
    page.push(root);
    taskCount += chainSize;
    if (taskCount >= QUEST_PAGE_SIZE) {
      pages.push(page);
      page = [];
      taskCount = 0;
    }
  }

  if (page.length > 0) pages.push(page);
  return pages.length > 0 ? pages : [[]];
}

function renderQuestTree(viewKey) {
  const view = questViews[viewKey];
  const tbody = $(view.tableSel + ' tbody');
  const pager = view.pagerSel ? $(view.pagerSel) : null;
  tbody.innerHTML = '';
  if (pager) pager.innerHTML = '';
  if (!view.data) return;
  const region = activeQuestRegion(viewKey);
  if (viewKey === 'all') updateAllQuestActionButtons(region);
  if (!region) return;

  const { roots, children } = buildQuestChains(region.quests);
  let pageRoots = roots;
  let pageCount = 1;
  if (pager) {
    const pages = paginateQuestRoots(roots, children);
    pageCount = pages.length;
    view.page = Math.min(Math.max(view.page, 0), pageCount - 1);
    pageRoots = pages[view.page];
  }

  for (const root of pageRoots) {
    const key = viewKey + ':' + region.region + ':' + root.questId;
    const stats = chainStats(root, children, new Set());
    const hasChain = stats.total > 1;
    const expanded = expandedChains.has(key);
    const pending = collectSubtree(root, children).filter((q) => !isCompletedQuest(q) && !isDailyQuest(q));
    emitQuestRow(tbody, viewKey, root, 0, { key, hasChain, expanded, stats, pending });
    if (expanded)
      emitChildren(tbody, viewKey, root, children, 1, new Set([root.questId]));
  }

  if (pager) {
    renderTaskPager(pager, view.page, pageCount, region.quests.length, (page) => {
      view.page = page;
      renderQuestTree(viewKey);
    });
  }
}

function renderTaskPager(pager, page, pageCount, total, onPageChange) {
  pager.innerHTML = '';
  if (pageCount <= 1) return;
  const prev = document.createElement('button');
  prev.className = 'mini';
  prev.textContent = '上一页';
  prev.disabled = page === 0;
  prev.onclick = () => onPageChange(page - 1);

  const next = document.createElement('button');
  next.className = 'mini';
  next.textContent = '下一页';
  next.disabled = page >= pageCount - 1;
  next.onclick = () => onPageChange(page + 1);

  const info = document.createElement('span');
  info.className = 'hint';
  info.textContent = `共 ${total} 个任务 · 第 ${page + 1} / ${pageCount} 页`;
  pager.append(prev, info, next);
}

// 链子树按展示顺序展平(根在前 = 合法的完成顺序)
function collectSubtree(root, children) {
  const list = [];
  const visited = new Set();
  (function walk(quest) {
    if (visited.has(quest.questId)) return;
    visited.add(quest.questId);
    list.push(quest);
    for (const child of children.get(quest.questId) || []) walk(child);
  })(root);
  return list;
}

// 完成整链: 根任务走 complete-chain 覆盖链外前置, 其余子树按顺序批量完成
async function completeWholeChain(root, pending) {
  if (!confirm(`完成整链共 ${pending.length} 个未完成任务(含链外前置), 继续?`)) return;
  try {
    let count = 0;
    if (pending.some((q) => q.questId === root.questId)) {
      const r = await post(`/api/characters/${currentChar.characterId}/quests/${root.questId}/complete-chain`);
      count += r.completedCount;
    }
    const rest = pending.filter((q) => q.questId !== root.questId).map((q) => q.questId);
    if (rest.length > 0) {
      const r = await post(`/api/characters/${currentChar.characterId}/quests/complete-batch`, { questIds: rest });
      count += r.completedCount;
    }
    toast(`整链完成: 共标记 ${count} 个任务`);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  }
}

async function completeCurrentLevelMainQuests() {
  return completeCurrentLevelQuestGroup('main', '主线', '#btn-complete-current-main');
}

async function completeCurrentLevelSideQuests() {
  return completeCurrentLevelQuestGroup('side', '外传', '#btn-complete-current-side');
}

async function completeCurrentLevelMainQuestsFromAll() {
  return completeCurrentLevelQuestGroup('main', '主线', '#btn-complete-current-main-all');
}

async function completeCurrentLevelSideQuestsFromAll() {
  return completeCurrentLevelQuestGroup('side', '外传', '#btn-complete-current-side-all');
}

async function completeCurrentLevelSystemQuests() {
  return completeCurrentLevelQuestGroup('system', '系统', '#btn-complete-current-system');
}

async function completeCurrentLevelNoItemAchievementQuests() {
  return completeCurrentLevelQuestGroup('achievement-no-item', '成就（无道具）', '#btn-complete-current-achievement-no-item');
}

async function completeCurrentLevelQuestGroup(groupKey, label, buttonSelector) {
  if (!currentChar) { toast('请先选择角色', true); return; }
  if (!confirm(`一键完成 ${currentChar.name} 当前等级可用的${label}任务，不发奖励，继续？`)) return;
  const btn = $(buttonSelector);
  btn.disabled = true;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/${groupKey}/complete-current-level`);
    toast(`已完成 ${r.completedCount || 0} 个当前等级${label}任务`);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  } finally {
    btn.disabled = false;
  }
}

async function completeProfessionQuests(first) {
  if (!currentChar) { toast('请先选择角色', true); return; }
  const body = {};
  if (first) body.first = first;
  const btn = $('#btn-complete-profession-quests');
  if (btn) btn.disabled = true;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/profession/complete`, body);
    toast(`已完成 ${r.completedCount || 0} 个转职/觉醒相关任务`);
    closeProfessionQuestPanel();
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
    const state = $('#profession-quest-state');
    if (state) state.textContent = e.message;
  } finally {
    if (btn) btn.disabled = false;
  }
}

async function completeProfessionQuestsButton() {
  if (!currentChar) { toast('请先选择角色', true); return; }
  if (currentChar.level < 15) return toast('角色达到 15 级后才能完成转职任务', true);
  if (currentChar.job === 9 || currentChar.job === 10) return toast('当前职业没有转职/觉醒分支', true);
  const currentFirst = (currentChar.growType || 0) & 0xF;
  if (currentFirst > 0) {
    if (!confirm(`按 ${currentChar.name} 当前职业和等级完成转职/觉醒任务，继续？`)) return;
    await completeProfessionQuests(null);
    return;
  }
  await openProfessionQuestPanel();
}

async function openProfessionQuestPanel() {
  const panel = $('#profession-quest-panel');
  const select = $('#profession-quest-first');
  const state = $('#profession-quest-state');
  if (!panel || !select) return;
  state.textContent = '正在加载...';
  select.innerHTML = '';
  panel.classList.remove('hidden');
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/growoptions`);
    const growTypes = data.options?.growTypes || [];
    for (const g of growTypes) {
      const option = document.createElement('option');
      option.value = g.value;
      option.textContent = g.label;
      select.appendChild(option);
    }
    state.textContent = growTypes.length ? '' : '当前职业没有可用转职';
    $('#btn-confirm-profession-quest').disabled = growTypes.length === 0;
  } catch (e) {
    state.textContent = e.message;
    toast(e.message, true);
  }
}

function closeProfessionQuestPanel() {
  const panel = $('#profession-quest-panel');
  if (panel) panel.classList.add('hidden');
}

async function confirmProfessionQuestChoice() {
  const first = parseInt($('#profession-quest-first').value, 10);
  if (!Number.isSafeInteger(first) || first <= 0)
    return toast('请选择目标转职', true);
  if (!confirm(`按所选转职完成当前等级对应的转职/觉醒任务，继续？`)) return;
  await completeProfessionQuests(first);
}

async function completeEquipmentSlotQuests() {
  if (!currentChar) { toast('请先选择角色', true); return; }
  if (!extraEquipmentSlotQuestStatus?.canComplete)
    return toast('只有已开启左右槽且相关任务有残留时可用', true);
  if (!confirm(`补完 ${extraEquipmentSlotQuestStatus.residualCount || 0} 个左右槽相关任务，继续？`)) return;
  const btn = $('#btn-complete-equipment-slot-quests');
  btn.disabled = true;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/equipment-slots/complete`);
    toast(`已完成 ${r.completedCount || 0} 个左右槽相关任务`);
    extraEquipmentSlotQuestStatus = null;
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  } finally {
    await loadExtraEquipmentSlotQuestStatus();
  }
}

// 缩进编码分叉结构而非顺序: 线性延伸保持同层(顺序由行序表达),
// 只有一个任务有多个后续(真分叉)时, 各分支才多缩进一层
function emitChildren(tbody, viewKey, parent, children, depth, visited) {
  const list = children.get(parent.questId) || [];
  const branching = list.length > 1;
  for (const quest of list) {
    if (visited.has(quest.questId)) continue;
    visited.add(quest.questId);
    const childDepth = branching ? depth + 1 : depth;
    emitQuestRow(tbody, viewKey, quest, childDepth, null);
    emitChildren(tbody, viewKey, quest, children, childDepth, visited);
  }
}

function emitQuestRow(tbody, viewKey, quest, depth, chainHead) {
  const tr = document.createElement('tr');
  const isDaily = isDailyQuest(quest);
  const pre = quest.preRequired.length === 0 ? '<span class="hint">无</span>'
    : quest.preRequired.map((p) => {
        // 名字解析不到 = 该编号不在本版本 quest.lst 里(残留引用);
        // "连前置完成"会直接给它写完成标记, 以通过服务端的前置检查
        if (!p.name)
          return `<span class="pre-phantom" title="编号 ${p.questId} 在本版本任务表中不存在(残留引用), 连前置完成可直接写标记解锁">#${p.questId}失效${p.done ? '✓' : ''}</span>`;
        return `<span class="${p.done ? 'pre-done' : 'pre-missing'}" title="${p.questId}">${escapeHtml(p.name)}${p.done ? '✓' : '✗'}</span>`;
      }).join('、');
  const statusClass = isCompletedQuest(quest) ? 'pre-done' : quest.status === '进行中' ? '' : 'hint';
  // 链头行: 行级按钮只留一个, 链级动作"完成整链"占另一个位; 非链头行保持原样
  const isChainHead = !isDaily && chainHead && chainHead.hasChain;
  const ownAction = isDaily
    ? `<button class="mini primary" data-act="daily-ready">${quest.status === '进行中' ? '标记可交' : '接取并可交'}</button>`
    : isCompletedQuest(quest)
      ? '<button class="mini danger" data-act="unclear">取消完成</button>'
      : isChainHead
        ? '<button class="mini primary" data-act="complete">标记完成</button>'
        : '<button class="mini primary" data-act="complete">标记完成</button> <button class="mini primary" data-act="chain">连前置完成</button>';
  const chainAction = isChainHead && chainHead.pending.length > 0
    ? '<button class="mini primary" data-act="whole-chain">完成整链</button> '
    : '';
  const action = chainAction + ownAction;

  let nameCell;
  const indent = depth > 0 ? `style="padding-left:${depth * 18 + 10}px"` : '';
  if (isChainHead) {
    const arrow = chainHead.expanded ? '▾' : '▸';
    const suffix = chainHead.expanded ? '' :
      ` <span class="hint">(链 ${chainHead.stats.done}/${chainHead.stats.total})</span>`;
    nameCell = `<td class="chain-toggle" ${indent}><span class="toggle">${arrow}</span> ${escapeHtml(quest.name || '')}${suffix}</td>`;
  } else {
    nameCell = `<td ${indent}>${depth > 0 ? '<span class="hint">· </span>' : ''}${escapeHtml(quest.name || '')}</td>`;
  }

  tr.innerHTML = `<td class="${statusClass}">${quest.status}</td><td>${quest.minLevel}</td>
    <td>${quest.questId}</td>${nameCell}
    <td>${pre}</td><td>${action}</td>`;

  if (chainHead && chainHead.hasChain) {
    tr.querySelector('.chain-toggle').onclick = (e) => {
      if (e.target.tagName === 'BUTTON') return;
      if (expandedChains.has(chainHead.key)) expandedChains.delete(chainHead.key);
      else expandedChains.add(chainHead.key);
      renderQuestTree(viewKey);
    };
  }

  tr.querySelectorAll('button').forEach((btn) => {
    btn.onclick = async (e) => {
      e.stopPropagation();
      const act = btn.dataset.act;
      if (act === 'whole-chain') {
        btn.disabled = true; // 批量在飞行中, 防双击双发
        completeWholeChain(quest, chainHead.pending).finally(() => { btn.disabled = false; });
        return;
      }
      try {
        if (act === 'chain') {
          const r = await post(`/api/characters/${currentChar.characterId}/quests/${quest.questId}/complete-chain`);
          toast(`已完成 ${r.completedCount} 个任务(链共 ${r.chainSize} 个)`);
        } else if (act === 'daily-ready') {
          await post(`/api/characters/${currentChar.characterId}/quests/${quest.questId}/daily-ready`);
          toast('每日任务已接取并标记可交');
        } else {
          await post(`/api/characters/${currentChar.characterId}/quests/${quest.questId}/${act}`);
          toast(act === 'unclear' ? '已取消完成标记' : '已标记完成');
        }
        refreshQuestViews();
      } catch (e2) {
        toast(e2.message, true);
      }
    };
  });
  tbody.appendChild(tr);
}

async function resetDailyQuests() {
  if (!currentChar) { toast('请先选择角色', true); return; }
  if (!confirm('将当前角色每日任务还原为未完成，并移出进行中任务，继续？')) return;
  const btn = $('#btn-reset-daily-quests');
  btn.disabled = true;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/daily/reset`);
    toast(`已还原 ${r.resetCount || 0} 个每日任务`);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  } finally {
    btn.disabled = false;
  }
}

async function searchQuestLib() {
  if (!currentChar) return;
  const q = $('#quest-search-input').value.trim();
  const gradeFilter = $('#quest-grade-filter').value;
  const regionFilter = $('#quest-region-filter').value;
  try {
    const params = new URLSearchParams({
      q,
      grade: gradeFilter,
      region: regionFilter,
      limit: '500',
    });
    const data = await api(`/api/characters/${currentChar.characterId}/quests/search?${params.toString()}`);
    syncQuestLibraryFilters(data);
    const results = data.results || [];
    const table = $('#quest-lib-table');
    const tbody = table.querySelector('tbody');
    tbody.innerHTML = '';
    $('#quest-lib-count').textContent = `显示 ${data.count || 0} / ${data.totalCandidates || 0}`;
    for (const r of results) {
      const tr = document.createElement('tr');
      const action = r.status === '已完成'
        ? '<button class="mini danger">取消完成</button>'
        : '<button class="mini primary">标记完成</button>';
      tr.innerHTML = `<td>${escapeHtml(r.gradeLabel || '?')}</td><td>${escapeHtml(r.regionLabel || '')}</td>
        <td>${r.minLevel || ''}</td><td>${r.questId}</td><td>${escapeHtml(r.name || '')}</td>
        <td>${r.status}</td><td>${action}</td>`;
      const btn = tr.querySelector('button');
      btn.onclick = async () => {
        const act = r.status === '已完成' ? 'unclear' : 'complete';
        await questAction(r.questId, act, r.status === '已完成' ? '已取消完成标记' : '已标记完成');
        searchQuestLib();
      };
      tbody.appendChild(tr);
    }
    table.classList.remove('hidden');
    if (results.length === 0) tbody.innerHTML = '<tr><td colspan="7" class="hint">没有匹配的任务</td></tr>';
  } catch (e) {
    toast(e.message, true);
  }
}

function syncQuestLibraryFilters(data) {
  const filters = data?.filters || {};
  syncQuestLibrarySelect($('#quest-grade-filter'), filters.grades || [], '全部类型', data.grade || '');
  syncQuestLibrarySelect($('#quest-region-filter'), filters.regions || [], '全部区域', data.region || '');
}

function syncQuestLibrarySelect(select, options, allLabel, selectedValue) {
  if (!select) return;
  const current = selectedValue ?? select.value;
  select.innerHTML = '';
  const all = document.createElement('option');
  all.value = '';
  all.textContent = allLabel;
  select.appendChild(all);
  for (const optionData of options) {
    const option = document.createElement('option');
    option.value = optionData.value || '';
    option.textContent = `${optionData.label || optionData.value || '?'} (${optionData.count || 0})`;
    select.appendChild(option);
  }
  if ([...select.options].some((option) => option.value === current))
    select.value = current;
  else
    select.value = '';
}

// 一键称号簿: 把"称号"集合(称号簿五页)里所有未完成的成就全部完成,
// 每个都走单任务完成的全套逻辑, 称号自动送进称号簿。
// 定义必须在下方绑定语句之前(若改成 const 箭头函数, 靠声明提升的绑定会 TDZ 崩掉绑定链)
async function completeAllTitleBook() {
  if (!currentChar) { toast('请先选择角色', true); return; }
  if (!confirm('一键同步称号簿和对应成就任务，继续？')) return;
  const syncBtn = $('#btn-titlebook-all');
  syncBtn.disabled = true;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/titlebook/complete-all`);
    if (r.skipped)
      toast(r.message || '称号簿和对应任务已同步');
    else
      toast(`已同步 ${r.completedCount || 0} 个称号簿槽位`);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  } finally {
    syncBtn.disabled = false;
  }
  return;
  if (!currentChar) { toast('请先选择角色', true); return; }
  // 总是重新拉当前角色的数据: view.data 可能还是上一个角色的(切角色不清缓存)
  await loadQuestView('achieve');
  const view = questViews.achieve;
  if (!view.data) return; // 加载失败已由 loadQuestView 弹错
  const pending = [];
  for (const region of view.data.regions) {
    if (region.group !== '称号') continue;
    for (const quest of region.quests)
      if (quest.status !== '已完成') pending.push(quest.questId);
  }
  if (pending.length === 0) {
    toast('称号簿成就已全部完成');
    return;
  }
  if (!confirm(`一键完成称号簿全部 5 页共 ${pending.length} 个未完成成就, 称号将全部入簿, 继续?`)) return;
  const btn = $('#btn-titlebook-all');
  btn.disabled = true; // 批量在飞行中, 防双击双发
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/complete-batch`, { questIds: pending });
    toast(`已完成 ${r.completedCount} 个成就, 称号已入簿`);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  } finally {
    btn.disabled = false;
  }
}

async function unclearCurrentTitleBookPage() {
  if (!currentChar) { toast('请先选择角色', true); return; }
  await loadQuestView('achieve');
  const view = questViews.achieve;
  if (!view.data) return;
  const region = view.data.regions.find((r) => r.region === view.activeRegion);
  if (!region || region.group !== '称号') {
    toast('请先在成就页选择一个称号簿分类', true);
    return;
  }

  const questIds = region.quests.map((quest) => quest.questId);
  if (questIds.length === 0) {
    toast('当前称号簿页没有可取消的任务');
    return;
  }
  if (!confirm(`将当前称号簿页「${region.regionLabel}」的 ${questIds.length} 个任务标记为未完成，并移除对应称号簿记录，继续？`)) return;

  const btn = $('#btn-titlebook-unclear-page');
  btn.disabled = true;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/quests/unclear-batch`, { questIds });
    toast(`已取消 ${r.clearedCount || 0} 个称号簿任务`);
    refreshQuestViews();
  } catch (e) {
    toast(e.message, true);
  } finally {
    btn.disabled = false;
  }
}
