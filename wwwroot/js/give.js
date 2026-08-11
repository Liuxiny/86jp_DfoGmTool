// ---- 发放物品 ----

// 类型标签中文名(鼠标悬浮可见原始标签); 含义未经实物确认的不硬翻, 显示原始标签
const TAG_LABELS = {
  // 装备部位
  'weapon': '武器', 'coat': '上衣', 'shoulder': '头肩', 'pants': '下装', 'shoes': '鞋',
  'waist': '腰带', 'amulet': '项链', 'wrist': '手镯', 'ring': '戒指', 'support': '辅助装备',
  'magic stone': '魔法石', 'support weapon': '副武器',
  'title name': '称号', 'name tag': '名称装饰卡',
  'creature': '宠物', 'artifact red': '宠物装备·红', 'artifact blue': '宠物装备·蓝',
  'artifact green': '宠物装备·绿',
  // 装扮部位
  'hat avatar': '帽子装扮', 'hair avatar': '头发装扮', 'face avatar': '脸部装扮',
  'coat avatar': '上衣装扮', 'breast avatar': '胸部装扮', 'waist avatar': '腰部装扮',
  'pants avatar': '下装装扮', 'shoes avatar': '鞋装扮', 'skin avatar': '皮肤装扮',
  'aurora avatar': '光环装扮', 'weapon avatar': '武器装扮',
  // 堆叠物类型(仅列实物确认过的: 附魔宝珠/福包/名称装饰卡等均抽样核对)
  'material': '材料', 'quest': '任务品', 'material expert job': '副职业材料',
  'avatar emblem': '徽章', 'recipe': '设计图', 'dye': '染色剂', 'throw': '投掷物',
  'enchant waste': '附魔宝珠', 'cera package': '点券礼包', 'usable cera package': '点券礼包',
  'cera booster': '福包', 'booster': '礼盒', 'booster selection': '自选礼盒',
  'town and dungeon': '城镇副本道具', 'teleport potion': '传送药剂', 'etc': '其他',
};
// 品级体系依客户端串表(dstr 35103-35105): 勇者=红色仅出自异界(狂龙套=5),
// 镇魂/释魂/杰诺灵魂剑=6=传说。5不是传说。
const RARITY_LABELS = ['普通', '高级', '稀有', '神器', '史诗', '勇者', '传说'];
// 品质细分(数据标记均经实物验证): 传承=[item category] legacy,
// 领主神器=[item category] boss drop, 魔法封印=[random option]
const SPECIAL_LABELS = { sealed: '魔法封印', legacy: '传承', boss: '领主神器' };

function normalizeEquipmentTag(tag) {
  return String(tag || '')
    .replace(/[\[\]`]/g, '')
    .replace(/_/g, ' ')
    .trim()
    .toLowerCase()
    .replace(/\s+\d+$/, '');
}

const tagLabel = (tag) => TAG_LABELS[normalizeEquipmentTag(tag)] || tag || '(无标签)';
const equipmentTypeLabel = (tag) => TAG_LABELS[normalizeEquipmentTag(tag)] || normalizeEquipmentTag(tag) || '(无标签)';

const CONFIG_OPTION_LABELS = {
  'EQUIPMENT PHYSICAL DEFENSE': '物理防御',
  'EQUIPMENT MAGICAL DEFENSE': '魔法防御',
  'PHYSICAL DEFENSE': '物理防御',
  'MAGICAL DEFENSE': '魔法防御',
  'INTELLIGENCE': '智力',
  'SPIRIT': '精神',
  'STRENGTH': '力量',
  'VITALITY': '体力',
  'CAST SPEED': '施放速度',
  'ATTACK SPEED': '攻击速度',
  'MOVE SPEED': '移动速度',
  'HIT RECOVERY': '硬直',
  'ABNORMAL STATUS RESISTANCE': '异常状态抗性',
  'FIRE ELEMENTAL RESISTANCE': '火属性抗性',
  'WATER ELEMENTAL RESISTANCE': '冰属性抗性',
  'ICE ELEMENTAL RESISTANCE': '冰属性抗性',
  'DARK ELEMENTAL RESISTANCE': '暗属性抗性',
  'LIGHT ELEMENTAL RESISTANCE': '光属性抗性',
  'EVASION': '回避率',
  'EQUIPMENT WEIGHT': '负重上限',
  'INVENTORY LIMIT': '负重上限',
  'JUMP': '跳跃力',
  'PHYSICAL DAMAGE REDUCE': '物理伤害减免',
  'MAGICAL DAMAGE REDUCE': '魔法伤害减免',
  'ALL DAMAGE REDUCE': '所有伤害减免',
  'ALL STAT': '所有基础属性',
  'ALL STATS': '所有基础属性',
};

function localizeConfigOptionLabel(label) {
  const raw = String(label || '');
  if (!raw) return raw;
  if (/^(HP|MP)\b/i.test(raw.trim())) return raw;
  const match = raw.match(/^(.+?)(\s+[+\-]\s*\d.*)?$/);
  const body = (match ? match[1] : raw)
    .trim()
    .replace(/^[`\[]+|[`]+$/g, '')
    .replace(/\]$/g, '')
    .replace(/_/g, ' ');
  const suffix = match && match[2] ? match[2].replace(/\s+/g, '') : '';
  const key = body.toUpperCase();
  return CONFIG_OPTION_LABELS[key] ? CONFIG_OPTION_LABELS[key] + suffix : raw;
}

// 装备侧栏分组: 固定顺序, 未列出的标签落入"其他"
const EQUIP_GROUPS = [
  { title: '装备', tags: ['weapon', 'coat', 'shoulder', 'pants', 'shoes', 'waist',
    'amulet', 'wrist', 'ring', 'support', 'magic stone', 'support weapon',
    'title name', 'name tag'] },
  { title: '宠物', tags: ['creature', 'artifact red', 'artifact blue', 'artifact green'] },
  { title: '装扮', tags: ['hat avatar', 'hair avatar', 'face avatar', 'coat avatar',
    'breast avatar', 'waist avatar', 'pants avatar', 'shoes avatar',
    'skin avatar', 'aurora avatar', 'weapon avatar'] },
];
// 堆叠物侧栏 = 背包同款六段(与服务端入格语义一致), 固定顺序
const STACK_SEGMENTS = ['消耗品', '材料', '任务品', '副职业材料', '徽章', '特殊材料'];

let giveCategory = null; // {kind:'equipment', tag/tags} 或 {kind:'stackable', segment/segments}
let giveJobLabelByValue = new Map();

const USABLE_JOB_TOKEN_TO_JOB = {
  swordman: 0,
  fighter: 1,
  gunner: 2,
  mage: 3,
  priest: 4,
  'at gunner': 5,
  thief: 6,
  'at fighter': 7,
  'at mage': 8,
  demonicswordman: 9,
  'demonic swordman': 9,
  creatormage: 10,
  'creator mage': 10,
  'at swordman': 11,
  atswordman: 11,
  knight: 12,
};

const USABLE_JOB_FALLBACK_LABELS = {
  0: '鬼剑士',
  1: '格斗家',
  2: '神枪手',
  3: '魔法师',
  4: '圣职者',
  5: '女神枪手',
  6: '暗夜使者',
  7: '男格斗家',
  8: '男魔法师',
  9: '黑暗武士',
  10: '缔造者',
  11: '女鬼剑士',
  12: '守护者',
};

function giveCategoryMatches(left, right) {
  return JSON.stringify(left || null) === JSON.stringify(right || null);
}

function pipeValues(values) {
  return (values || []).filter(Boolean).join('|');
}

function usableJobChipsHtml(item) {
  const labels = usableJobDisplayLabels(item);
  const cleanLabels = labels.map((label) => String(label || '').trim()).filter(Boolean);
  const allLabels = cleanLabels.length ? cleanLabels : ['无限制'];
  const visible = allLabels.length > 4 ? allLabels.slice(0, 3) : allLabels.slice(0, 4);
  const hiddenCount = allLabels.length - visible.length;
  const chips = visible.map((label) =>
    `<span class="usable-job-chip" title="${escapeHtml(label)}">${escapeHtml(label)}</span>`);
  if (hiddenCount > 0)
    chips.push(`<span class="usable-job-chip more" title="${escapeHtml(allLabels.join(' / '))}">+${hiddenCount}</span>`);
  return `<div class="usable-job-chips" title="${escapeHtml(allLabels.join(' / '))}">${chips.join('')}</div>`;
}

function usableJobDisplayLabels(item) {
  const raw = String(item?.usableJob || '').trim();
  if (!raw || /\[all\]/i.test(raw)) return ['无限制'];

  const parsed = [];
  for (const match of raw.matchAll(/\[([^\]]+)\]/g)) {
    const token = normalizeUsableJobToken(match[1]);
    if (!token || token === 'all') continue;
    const job = USABLE_JOB_TOKEN_TO_JOB[token];
    const label = job != null ? giveJobLabelByValue.get(String(job)) : null;
    parsed.push(label || usableJobFallbackLabel(job, token));
  }

  const unique = parsed.filter((label, index) => label && parsed.indexOf(label) === index);
  if (unique.length) return unique;
  if (Array.isArray(item?.usableJobLabels) && item.usableJobLabels.length) return item.usableJobLabels;
  return [item?.usableJobLabel || '无限制'];
}

function normalizeUsableJobToken(token) {
  return String(token || '')
    .replace(/_/g, ' ')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ');
}

function usableJobTokenFallbackLabel(token) {
  return token || '无限制';
}

function usableJobFallbackLabel(job, token) {
  if (job != null && USABLE_JOB_FALLBACK_LABELS[job])
    return giveJobLabelWithGender(job, USABLE_JOB_FALLBACK_LABELS[job]);
  return usableJobTokenFallbackLabel(token);
}

function giveCatEl(label, count, isActive, rawTitle, onClick) {
  const el = document.createElement('div');
  el.className = 'cat' + (isActive ? ' active' : '');
  if (rawTitle) el.title = rawTitle;
  el.innerHTML = `<span>${escapeHtml(label)}</span>` +
    (count != null ? `<span class="cnt">${count}</span>` : '');
  el.onclick = onClick;
  return el;
}

// 展开状态跨重渲染保留; 默认全收起, 只显示组头
const giveNavExpanded = new Set();

async function loadGiveCategories(expectedRuntimeEpoch) {
  try {
    const data = await api('/api/items/categories');
    if (expectedRuntimeEpoch != null && expectedRuntimeEpoch !== runtimeSourceEpoch) return;
    const nav = $('#give-category-nav');
    nav.innerHTML = '';
    if (!data.ready) {
      nav.innerHTML = '<div class="group-title">索引构建中…</div>';
      return;
    }

    const pick = (cat) => { giveCategory = cat; loadGiveCategories(); searchItems(); };
    nav.appendChild(giveCatEl('全部', null, giveCategory === null, null, () => pick(null)));

    const equipCounts = new Map(data.equipment.map((c) => [c.tag, c.count]));
    const segCounts = new Map(data.stackable.map((c) => [c.segment, c.count]));
    const listed = new Set();

    syncGiveUsableJobOptions(data.jobs || []);

    // entries: [{label, rawTitle, count, active, cat}]
    const addGroup = (title, entries, groupCat) => {
      const present = entries.filter((e) => e.count != null);
      if (present.length === 0) return;
      const total = present.reduce((sum, e) => sum + e.count, 0);
      const expanded = giveNavExpanded.has(title);
      const head = document.createElement('div');
      head.className = 'group-title group-toggle' + (giveCategoryMatches(giveCategory, groupCat) ? ' active' : '');
      head.innerHTML = `<span><span class="toggle" role="button" title="展开/收起">${expanded ? '▾' : '▸'}</span><span class="group-label">${escapeHtml(title)}</span></span><span class="cnt">${total}</span>`;
      head.onclick = (event) => {
        if (event.target.classList.contains('toggle')) {
          if (giveNavExpanded.has(title)) giveNavExpanded.delete(title);
          else giveNavExpanded.add(title);
          loadGiveCategories();
          return;
        }
        pick(groupCat);
      };
      nav.appendChild(head);
      if (!expanded) return;
      for (const e of present)
        nav.appendChild(giveCatEl(e.label, e.count, e.active, e.rawTitle, () => pick(e.cat)));
    };

    const equipEntry = (tag) => {
      listed.add(tag);
      return {
        label: tagLabel(tag),
        rawTitle: tag,
        count: equipCounts.get(tag),
        active: !!(giveCategory && giveCategory.kind === 'equipment' && giveCategory.tag === tag),
        cat: { kind: 'equipment', tag },
      };
    };

    for (const group of EQUIP_GROUPS)
      addGroup(group.title, group.tags.map(equipEntry), { kind: 'equipment', tags: group.tags.slice() });

    addGroup('消耗品 / 材料', STACK_SEGMENTS.map((seg) => ({
      label: seg,
      rawTitle: '与背包入格分类同语义',
      count: segCounts.get(seg),
      active: !!(giveCategory && giveCategory.kind === 'stackable' && giveCategory.segment === seg),
      cat: { kind: 'stackable', segment: seg },
    })), { kind: 'stackable', segments: STACK_SEGMENTS.slice() });

    const leftovers = data.equipment.filter((c) => !listed.has(c.tag))
      .sort((a, b) => b.count - a.count);
    addGroup('其他', leftovers.map((c) => equipEntry(c.tag)), { kind: 'equipment', tags: leftovers.map((c) => c.tag) });
  } catch (e) {
    toast(e.message, true);
  }
}

function syncGiveUsableJobOptions(jobs) {
  const select = $('#give-usable-job');
  if (!select) return;
  const previous = select.value;
  const defaultValue = currentChar ? String(currentChar.job) : '-1';
  select.innerHTML = '<option value="-1">全部职业</option><option value="-2">无限制</option>';
  giveJobLabelByValue = new Map();
  for (const job of jobs || []) {
    const option = document.createElement('option');
    option.value = job.value;
    option.textContent = giveJobLabelWithGender(job.value, job.label || `job ${job.value}`);
    giveJobLabelByValue.set(String(job.value), option.textContent);
    select.appendChild(option);
  }
  select.value = previous && [...select.options].some((option) => option.value === previous)
    ? previous
    : defaultValue;
}

function giveJobLabelWithGender(job, label) {
  if (typeof jobLabelWithGender === 'function')
    return jobLabelWithGender(job, label);
  return label;
}

function giveJobGenderSuffix(job) {
  return '';
}

function resetGiveUsableJobToCurrentCharacter() {
  const select = $('#give-usable-job');
  if (!select || !currentChar) return;
  if ([...select.options].some((option) => option.value === String(currentChar.job)))
    select.value = String(currentChar.job);
}

let givePageSize = ItemPageSize.get();
let givePage = 0; // 从 0 计; 换筛选条件时归零
let giveConfiguration = null;
let giveConfigurationEpoch = 0;
let giveSearchSignature = '';
let clearingCharacterMailbox = false;
const GIVE_DELIVERY_MODE_STORAGE_KEY = 'dfogm-give-delivery-mode';
let giveDeliveryMode = 'mail';
let giveRequestInFlight = false;

function readGiveDeliveryMode() {
  try {
    const value = String(window.localStorage.getItem(GIVE_DELIVERY_MODE_STORAGE_KEY) || '');
    return value === 'inventory' || value === 'mail' ? value : 'mail';
  } catch (_) {
    return 'mail';
  }
}

function rememberGiveDeliveryMode() {
  try {
    window.localStorage.setItem(GIVE_DELIVERY_MODE_STORAGE_KEY, giveDeliveryMode);
    return true;
  } catch (_) {
    // Storage can be unavailable in private or restricted browsing modes.
    return false;
  }
}

function giveDeliveryLabel(mode = giveDeliveryMode) {
  return mode === 'inventory' ? '背包发放' : '邮件发放';
}

function updateGiveDeliveryPresentation() {
  const control = $('#give-delivery-mode');
  if (control) {
    control.dataset.mode = giveDeliveryMode;
    control.setAttribute('aria-checked', giveDeliveryMode === 'inventory' ? 'true' : 'false');
    control.setAttribute('aria-label', `发放方式：${giveDeliveryLabel()}`);
    control.disabled = giveRequestInFlight;
    control.title = giveRequestInFlight ? '发放进行中，暂不能切换发放方式' : '';
  }

  document.querySelectorAll('#search-results .give-mode-hint').forEach((cell) => {
    cell.textContent = `配置后${giveDeliveryLabel()}`;
  });
  document.querySelectorAll('#search-results button[data-give-action="grant"]').forEach((button) => {
    button.textContent = giveDeliveryLabel();
    button.disabled = giveRequestInFlight;
  });
  const submit = $('#give-config-submit');
  if (submit) {
    submit.textContent = giveDeliveryLabel();
    submit.disabled = giveRequestInFlight || submit.dataset.baseDisabled === 'true';
  }
}

function setGiveRequestBusy(busy) {
  giveRequestInFlight = !!busy;
  updateGiveDeliveryPresentation();
  document.querySelectorAll('#search-results button[data-give-action="configure"]').forEach((button) => {
    button.disabled = giveRequestInFlight;
  });
}

function bindGiveDeliveryMode() {
  const control = $('#give-delivery-mode');
  if (!control || control._giveDeliveryModeBound) return;
  control._giveDeliveryModeBound = true;
  giveDeliveryMode = readGiveDeliveryMode();
  control.onclick = () => {
    const next = giveDeliveryMode === 'inventory' ? 'mail' : 'inventory';
    if (giveRequestInFlight) return;
    if (next === 'inventory'
        && !confirm('背包发放会直接写入新版背包，确认切换到背包发放吗？')) return;
    giveDeliveryMode = next;
    if (!rememberGiveDeliveryMode())
      toast('当前发放方式已切换，但无法跨刷新记忆。', true);
    updateGiveDeliveryPresentation();
    if (giveConfiguration)
      renderGrantConfiguration();
  };
  updateGiveDeliveryPresentation();
}

function updateCharacterMailboxButton() {
  const button = $('#btn-clear-character-mailbox');
  if (!button) return;
  button.disabled = !currentChar || clearingCharacterMailbox;
  button.textContent = clearingCharacterMailbox ? '清空中…' : '清空邮箱';
}

function bindClearCharacterMailbox() {
  const button = $('#btn-clear-character-mailbox');
  if (!button || button._mailboxClearBound) {
    updateCharacterMailboxButton();
    return;
  }
  button._mailboxClearBound = true;
  button.onclick = clearCurrentCharacterMailbox;
  updateCharacterMailboxButton();
}

async function clearCurrentCharacterMailbox() {
  if (clearingCharacterMailbox || !currentChar) return;
  const snapshot = {
    characterId: Number(currentChar.characterId),
    name: String(currentChar.name ?? ''),
  };
  if (!Number.isSafeInteger(snapshot.characterId) || snapshot.characterId <= 0) {
    toast('当前角色无有效 ID', true);
    return;
  }
  if (!confirm(`是否确认清空当前角色 ${snapshot.name} 的邮箱？`)) return;

  clearingCharacterMailbox = true;
  updateCharacterMailboxButton();
  try {
    const result = await post(`/api/characters/${encodeURIComponent(String(snapshot.characterId))}/mailbox/clear`);
    const recipientCount = Number(result.recipientCount || 0);
    const messageCount = Number(result.messageCount || 0);
    const attachmentCount = Number(result.attachmentCount || 0);
    const auditCount = Number(result.auditCount || 0);
    toast(`已清空角色 ${snapshot.name} 的邮箱：收件人 ${recipientCount}、邮件 ${messageCount}、附件 ${attachmentCount}、审计 ${auditCount}`);
  } catch (e) {
    toast(`清空角色 ${snapshot.name} 的邮箱失败：${e.message}`, true);
  } finally {
    clearingCharacterMailbox = false;
    updateCharacterMailboxButton();
  }
}

const giveConfigMemory = {
  qualityMode: 1,
  upgradeLevel: 0,
  amplifyType: 0,
  forgingLevel: 0,
  manualGrantType: '',
  expirationMode: 'default',
  expirationDays: 30,
  avatarOptionByPart: Object.create(null),
  avatarDurationByPart: Object.create(null),
};

function legalRememberedValue(options, value, fallback) {
  return (options || []).some((option) => String(option.value) === String(value)) ? value : fallback;
}

function rememberGiveConfiguration() {
  if (!giveConfiguration) return;
  const part = String(giveConfiguration.capability?.avatar?.part || '');
  const readInt = (selector, fallback) => {
    const element = $(selector);
    const value = element ? parseInt(element.value, 10) : NaN;
    return Number.isFinite(value) ? value : fallback;
  };
  giveConfigMemory.qualityMode = readInt('#give-config-quality', giveConfigMemory.qualityMode);
  giveConfigMemory.upgradeLevel = readInt('#give-config-upgrade', giveConfigMemory.upgradeLevel);
  giveConfigMemory.amplifyType = readInt('#give-config-amplify', giveConfigMemory.amplifyType);
  giveConfigMemory.forgingLevel = readInt('#give-config-forging', giveConfigMemory.forgingLevel);
  giveConfigMemory.expirationDays = readInt('#give-config-expiration-days', giveConfigMemory.expirationDays);
  const manual = $('#give-config-manual-type');
  if (manual) giveConfigMemory.manualGrantType = manual.value;
  const expirationMode = $('#give-config-expiration-mode');
  if (expirationMode) giveConfigMemory.expirationMode = expirationMode.value;
  const avatarOption = $('#give-config-avatar-option');
  if (avatarOption && part) giveConfigMemory.avatarOptionByPart[part] = parseInt(avatarOption.value, 10);
  const avatarDuration = $('#give-config-avatar-duration');
  if (avatarDuration && part) giveConfigMemory.avatarDurationByPart[part] = parseInt(avatarDuration.value, 10);
}

function clearGiveConfiguration() {
  rememberGiveConfiguration();
  giveConfigurationEpoch++;
  giveConfiguration = null;
  const card = $('#give-config-card');
  if (card) {
    card.innerHTML = '';
    FloatingConfigPanel.hide(card);
  }
  document.querySelectorAll('#search-results tr.config-selected')
    .forEach((row) => row.classList.remove('config-selected'));
}

function isLimitedTemplate(item) {
  const expiry = item && item.templateExpiration;
  return !!(expiry && (expiry.absoluteExpireTime > 0 || expiry.usablePeriodDays > 0));
}

function needsGrantConfiguration(item) {
  if (!item) return false;
  if (typeof item.requiresConfiguration === 'boolean') return item.requiresConfiguration;
  return item.kind === 'equipment' || item.requiresManualGrantType || isLimitedTemplate(item);
}

function optionHtml(options, selectedValue) {
  return (options || []).map((option) => {
    const selected = String(option.value) === String(selectedValue) ? ' selected' : '';
    return `<option value="${escapeHtml(String(option.value))}"${selected}>${escapeHtml(localizeConfigOptionLabel(option.label))}</option>`;
  }).join('');
}

function avatarOptionControlHtml(id, options, selectedValue) {
  const list = options || [];
  const select = `<select id="${id}"${list.some((option) => option.isSkill) ? ' class="hidden"' : ''}>${optionHtml(list, selectedValue)}</select>`;
  if (!list.some((option) => option.isSkill))
    return select;

  const selected = list.find((option) => String(option.value) === String(selectedValue)) || list[0];
  const selectedLabel = selected ? localizeConfigOptionLabel(selected.label) : '';
  const datalistId = `${id}-list`;
  const datalist = list.map((option) =>
    `<option value="${escapeHtml(localizeConfigOptionLabel(option.label))}"></option>`).join('');
  return `<input id="${id}-search" type="text" list="${datalistId}" value="${escapeHtml(selectedLabel)}" autocomplete="off" spellcheck="false" placeholder="输入技能名快速定位">` +
    `<datalist id="${datalistId}">${datalist}</datalist>${select}`;
}

function bindAvatarOptionSearch(id, onValidChange) {
  const input = $(`#${id}-search`);
  const select = $(`#${id}`);
  if (!input || !select) return;
  const sync = () => {
    const match = Array.from(select.options).find((option) => option.textContent === input.value);
    select.value = match ? match.value : '';
    if (match && onValidChange) onValidChange();
  };
  input.addEventListener('input', sync);
  input.addEventListener('change', sync);
}

function readAvatarOptionValue(id) {
  const select = $(`#${id}`);
  if (!select) return { ok: false, error: '没有可保存的时装属性' };
  const input = $(`#${id}-search`);
  if (input) {
    const match = Array.from(select.options).find((option) => option.textContent === input.value);
    if (!match)
      return { ok: false, error: '请选择列表中的合法技能名，不支持自定义输入' };
    return { ok: true, value: parseInt(match.value, 10) };
  }
  return { ok: true, value: parseInt(select.value, 10) };
}

async function configureGrantItem(item, row) {
  if (giveRequestInFlight) return;
  if (!currentChar) { toast('请先选择角色', true); return; }
  rememberGiveConfiguration();
  const epoch = ++giveConfigurationEpoch;
  try {
    const capability = await api(`/api/characters/${currentChar.characterId}/items/${item.itemId}/grant-options`);
    if (epoch !== giveConfigurationEpoch) return;
    giveConfiguration = { item, capability };
    document.querySelectorAll('#search-results tr.config-selected')
      .forEach((candidate) => candidate.classList.remove('config-selected'));
    if (row) row.classList.add('config-selected');
    renderGrantConfiguration();
  } catch (e) {
    toast(e.message, true);
  }
}

function renderGrantConfiguration() {
  const card = $('#give-config-card');
  if (!giveConfiguration || !card) {
    clearGiveConfiguration();
    return;
  }

  const { item, capability } = giveConfiguration;
  const fields = [];
  fields.push(`<label class="give-config-field"><span>数量</span><input id="give-config-count" type="number" min="1" value="1"></label>`);

  if (capability.equipment) {
    const equipment = capability.equipment;
    if (equipment.supportsQuality) {
      const quality = legalRememberedValue(equipment.qualityOptions, giveConfigMemory.qualityMode, 1);
      fields.push(`<label class="give-config-field"><span>装备品级</span><select id="give-config-quality">${optionHtml(equipment.qualityOptions, quality)}</select></label>`);
    }
    if (equipment.canUpgrade || equipment.canAmplify) {
      fields.push(`<label class="give-config-field"><span>强化 / 增幅</span><input id="give-config-upgrade" type="number" min="0" max="${equipment.maxUpgradeLevel}" value="${giveConfigMemory.upgradeLevel}"></label>`);
      const amplify = legalRememberedValue(equipment.amplifyTypes, giveConfigMemory.amplifyType, 0);
      fields.push(`<label class="give-config-field"><span>红字属性</span><select id="give-config-amplify" ${equipment.canAmplify ? '' : 'disabled'}>${optionHtml(equipment.amplifyTypes, amplify)}</select></label>`);
    }
    if (equipment.canForge)
      fields.push(`<label class="give-config-field"><span>锻造</span><input id="give-config-forging" type="number" min="0" max="${equipment.maxForgingLevel}" value="${giveConfigMemory.forgingLevel}"></label>`);
  }

  if (capability.manual && capability.manual.required) {
    const selected = legalRememberedValue(capability.manual.choices, giveConfigMemory.manualGrantType, capability.manual.choices[0]?.value || '');
    fields.push(`<label class="give-config-field"><span>手动分类</span><select id="give-config-manual-type">${optionHtml(capability.manual.choices, selected)}</select></label>`);
  }

  let grantDisabled = false;
  if (capability.avatar) {
    const avatar = capability.avatar;
    fields.push(`<div class="give-config-field"><span>装扮部位</span><div class="give-config-value">${escapeHtml(equipmentTypeLabel(avatar.part))}</div></div>`);
    if (!avatar.compatible || !avatar.options || avatar.options.length === 0) {
      fields.push('<div class="give-config-field"><span>可选属性</span><div class="give-config-value">当前职业不可用</div></div>');
      grantDisabled = true;
    } else {
      const selected = legalRememberedValue(avatar.options, giveConfigMemory.avatarOptionByPart[avatar.part], avatar.options[0].value);
      fields.push(`<label class="give-config-field"><span>可选属性</span>${avatarOptionControlHtml('give-config-avatar-option', avatar.options, selected)}</label>`);
    }
    if (avatar.durations && avatar.durations.length > 0) {
      const permanent = avatar.durations.find((value) => value.days === 0);
      const defaultDays = permanent ? 0 : avatar.durations[0].days;
      const durationOptions = avatar.durations.map((value) => ({ value: value.days, label: value.label }));
      const selectedDays = legalRememberedValue(durationOptions, giveConfigMemory.avatarDurationByPart[avatar.part], defaultDays);
      fields.push(`<label class="give-config-field"><span>使用期限</span><select id="give-config-avatar-duration">${optionHtml(durationOptions, selectedDays)}</select></label>`);
    }
  } else if (capability.expiration && capability.expiration.canOverride) {
      const expired = capability.expiration.expired === true;
      const mode = expired ? 'custom' : giveConfigMemory.expirationMode;
      const modeOptions = expired
        ? '<option value="custom" selected>自定义有效期</option>'
        : `<option value="default"${mode === 'default' ? ' selected' : ''}>PVF 默认期限</option><option value="custom"${mode === 'custom' ? ' selected' : ''}>自定义天数</option>`;
      fields.push(`<label class="give-config-field"><span>期限方式</span><select id="give-config-expiration-mode">${modeOptions}</select></label>`);
      fields.push(`<label id="give-config-expiration-days-field" class="give-config-field${mode === 'custom' ? '' : ' hidden'}"><span>期限天数</span><input id="give-config-expiration-days" type="number" min="1" max="${capability.expiration.maxDays}" value="${giveConfigMemory.expirationDays || 30}"></label>`);
  }

  card.innerHTML = `<div class="give-config-head"><div class="give-config-title rarity-${item.rarity >= 0 && item.rarity <= 6 ? item.rarity : 0}">${escapeHtml(item.name)}</div><div class="give-config-meta">ID ${item.itemId} · ${escapeHtml(tagLabel(item.tag))} · 当前${escapeHtml(giveDeliveryLabel())}</div></div>` +
    `<div class="give-config-grid">${fields.join('')}</div>` +
    `<div class="give-config-actions"><button id="give-config-cancel" type="button">取消</button><button id="give-config-submit" type="button" data-base-disabled="${grantDisabled ? 'true' : 'false'}" ${grantDisabled ? 'disabled' : ''}>${escapeHtml(giveDeliveryLabel())}</button></div>`;
  FloatingConfigPanel.show(card, {
    avoidSelector: '#search-results thead th:nth-last-child(2)',
  });

  $('#give-config-cancel').onclick = clearGiveConfiguration;
  bindAvatarOptionSearch('give-config-avatar-option', rememberGiveConfiguration);
  const expirationMode = $('#give-config-expiration-mode');
  if (expirationMode) {
    expirationMode.onchange = () => {
      $('#give-config-expiration-days-field').classList.toggle('hidden', expirationMode.value !== 'custom');
      rememberGiveConfiguration();
      FloatingConfigPanel.refresh(card);
    };
  }
  card.querySelectorAll('input:not(#give-config-count), select').forEach((element) => {
    if (element !== expirationMode)
      element.addEventListener(element.tagName === 'INPUT' ? 'input' : 'change', rememberGiveConfiguration);
  });
  $('#give-config-submit').onclick = submitConfiguredGrant;
  updateGiveDeliveryPresentation();
}

async function submitConfiguredGrant() {
  if (!giveConfiguration || giveRequestInFlight) return;
  const { item, capability } = giveConfiguration;
  rememberGiveConfiguration();
  const count = Math.max(1, parseInt($('#give-config-count').value, 10) || 1);
  const options = {
    qualityMode: parseInt($('#give-config-quality')?.value || '1', 10),
    upgradeLevel: parseInt($('#give-config-upgrade')?.value || '0', 10),
    amplifyType: parseInt($('#give-config-amplify')?.value || '0', 10),
    forgingLevel: parseInt($('#give-config-forging')?.value || '0', 10),
  };
  const avatarOption = $('#give-config-avatar-option');
  if (avatarOption) {
    const avatarValue = readAvatarOptionValue('give-config-avatar-option');
    if (!avatarValue.ok) return toast(avatarValue.error, true);
    options.avatarOptionValue = avatarValue.value;
  }
  const avatarDuration = $('#give-config-avatar-duration');
  if (avatarDuration) options.expirationDays = parseInt(avatarDuration.value, 10);
  const manualType = $('#give-config-manual-type');
  if (manualType) options.manualGrantType = manualType.value;
  const expirationMode = $('#give-config-expiration-mode');
  if (expirationMode && expirationMode.value === 'custom')
    options.expirationDays = parseInt($('#give-config-expiration-days').value, 10);

  await giveItem(item.itemId, count, options);
}

async function searchItems(page) {
  givePage = page || 0;
  const q = $('#search-input').value.trim();
  const minLv = parseInt($('#give-minlv').value, 10) || 0;
  const maxLv = parseInt($('#give-maxlv').value, 10) || 0;
  const raritySel = $('#give-rarity').value;
  const expiration = $('#give-expiration').value;
  const usableJob = parseInt($('#give-usable-job')?.value || '-1', 10);
  const special = SPECIAL_LABELS[raritySel] ? raritySel : '';
  const rarity = special ? -1 : parseInt(raritySel, 10);
  const signature = JSON.stringify({ q, minLv, maxLv, raritySel, expiration, usableJob, giveCategory, characterId: currentChar?.characterId });
  if (signature !== giveSearchSignature) {
    giveSearchSignature = signature;
    clearGiveConfiguration();
  }
  if (!q && !giveCategory && minLv === 0 && maxLv === 0 && rarity < 0 && !special && expiration === 'all') {
    $('#search-results tbody').innerHTML =
      '<tr><td colspan="9" class="hint">选择左侧分类或输入关键词开始浏览</td></tr>';
    $('#give-total').textContent = '';
    $('#give-pager').innerHTML = '';
    return;
  }
  try {
    let url = `/api/items/browse?limit=${givePageSize}&offset=${givePage * givePageSize}` +
      `&q=${encodeURIComponent(q)}&minLevel=${minLv}&maxLevel=${maxLv}&rarity=${rarity}` +
      `&expiration=${encodeURIComponent(expiration)}&usableJob=${usableJob}`;
    if (special) url += `&special=${special}`;
    if (giveCategory) {
      url += `&kind=${encodeURIComponent(giveCategory.kind)}`;
      if (giveCategory.tag) url += `&tag=${encodeURIComponent(giveCategory.tag)}`;
      if (giveCategory.tags) url += `&tag=${encodeURIComponent(pipeValues(giveCategory.tags))}`;
      if (giveCategory.segment) url += `&segment=${encodeURIComponent(giveCategory.segment)}`;
      if (giveCategory.segments) url += `&segment=${encodeURIComponent(pipeValues(giveCategory.segments))}`;
    }
    const data = await api(url);
    const pageCount = Math.max(1, Math.ceil(data.total / givePageSize));
    // 条件变化后可能停留在越界页, 自动回退到末页
    if (givePage >= pageCount && data.total > 0) {
      searchItems(pageCount - 1);
      return;
    }
    $('#give-total').textContent = `共 ${data.total} 个匹配`;
    const tbody = $('#search-results tbody');
    tbody.innerHTML = '';
    for (const r of data.results) {
      const tr = document.createElement('tr');
      const configurable = needsGrantConfiguration(r);
      tr.innerHTML = `<td>${r.itemId}</td>
        <td class="rarity-${r.rarity >= 0 && r.rarity <= 6 ? r.rarity : 0}">${escapeHtml(r.name)}</td>
        <td>${r.minLevel || ''}</td>
        <td>${r.special ? (SPECIAL_LABELS[r.special] || escapeHtml(r.special)) : (RARITY_LABELS[r.rarity] || r.rarity)}</td>
        <td title="${escapeHtml(r.tag || '')}">${escapeHtml(tagLabel(r.tag))}</td>
        <td>${escapeHtml(r.usableJobLabel || '无限制')}</td>
        <td>${templateExpirationLabel(r)}</td>` +
        (configurable
          ? `<td class="hint give-mode-hint">配置后${escapeHtml(giveDeliveryLabel())}</td><td><button class="mini" data-give-action="configure">配置</button></td>`
          : `<td><input type="number" value="1" min="1"></td><td><button class="mini" data-give-action="grant">${escapeHtml(giveDeliveryLabel())}</button></td>`);
      const usableJobCell = tr.children[5];
      if (usableJobCell) {
        usableJobCell.className = 'usable-job-cell';
        usableJobCell.innerHTML = usableJobChipsHtml(r);
      }
      const button = tr.querySelector('button');
      button.onclick = (event) => {
        event.stopPropagation();
        if (configurable)
          configureGrantItem(r, tr);
        else
          giveItem(r.itemId, parseInt(tr.querySelector('input').value, 10) || 1);
      };
      tr.onclick = () => configurable ? configureGrantItem(r, tr) : clearGiveConfiguration();
      tbody.appendChild(tr);
    }
    if (data.results.length === 0)
      tbody.innerHTML = '<tr><td colspan="9" class="hint">没有匹配的物品</td></tr>';
    updateGiveDeliveryPresentation();

    const pager = $('#give-pager');
    pager.innerHTML = '';
    if (data.total > givePageSize) {
      const prev = document.createElement('button');
      prev.className = 'mini';
      prev.textContent = '上一页';
      prev.disabled = givePage === 0;
      prev.onclick = () => searchItems(givePage - 1);
      const next = document.createElement('button');
      next.className = 'mini';
      next.textContent = '下一页';
      next.disabled = givePage >= pageCount - 1;
      next.onclick = () => searchItems(givePage + 1);
      const info = document.createElement('span');
      info.className = 'hint';
      info.textContent = `第 ${givePage + 1} / ${pageCount} 页`;
      pager.append(prev, info, next);
    }
  } catch (e) {
    toast(e.message, true);
  }
}

function bindGivePageSize() {
  const select = $('#give-page-size');
  if (!select) return;
  select.value = String(givePageSize);
  select.onchange = () => {
    ItemPageSize.set(select.value);
  };
  ItemPageSize.subscribe((value) => {
    if (value === givePageSize && select.value === String(value)) return;
    givePageSize = value;
    select.value = String(value);
    searchItems(0);
  });
}

async function giveItem(templateId, count, options) {
  if (giveRequestInFlight) return;
  if (!currentChar) { toast('请先选择角色', true); return; }
  const characterSnapshot = {
    characterId: Number(currentChar.characterId),
    name: String(currentChar.name || ''),
  };
  if (!Number.isSafeInteger(characterSnapshot.characterId) || characterSnapshot.characterId <= 0) {
    toast('当前角色无有效 ID', true);
    return;
  }
  const deliveryModeSnapshot = giveDeliveryMode === 'inventory' ? 'inventory' : 'mail';
  setGiveRequestBusy(true);
  try {
    const requestId = typeof globalThis.crypto?.randomUUID === 'function'
      ? globalThis.crypto.randomUUID()
      : `${Date.now()}-${Math.random().toString(16).slice(2)}-${Math.random().toString(16).slice(2)}`;
    const body = { templateId, count, requestId, deliveryMode: deliveryModeSnapshot };
    if (options) body.options = options;
    const r = await post(`/api/characters/${encodeURIComponent(String(characterSnapshot.characterId))}/items`, body);
    const itemLabel = `${r.name || r.itemTemplateId || templateId} x${r.count ?? count}`;
    const deliveryHint = String(r.deliveryHint || '').trim();
    let summary;
    let fallbackHint = '';
    switch (r.delivery) {
      case 'mail': {
        const attachmentCount = Number(r.attachmentCount || 0);
        const messageCount = Number(r.messageCount || (r.messageId ? 1 : 0));
        summary = messageCount > 0
          ? `邮件发放成功（${messageCount} 封邮件、${attachmentCount} 个附件）`
          : '邮件发放成功';
        fallbackHint = '重新打开邮箱即可，无需重新选择角色。';
        break;
      }
      case 'inventory': {
        const grantedCount = Number(r.grantedCount || r.count || count || 0);
        const slots = Array.isArray(r.slots) ? r.slots.filter((slot) => Number.isFinite(Number(slot))) : [];
        const slotText = slots.length ? `，槽位 ${slots.join('、')}` : '';
        summary = `背包发放成功（${grantedCount} 件${slotText}）`;
        fallbackHint = '请返回角色选择界面后重新进入以刷新显示。';
        break;
      }
      case 'direct_cube':
        summary = '晶块发放成功';
        fallbackHint = '请返回角色选择界面后重新进入以刷新显示。';
        break;
      case 'direct_revive':
        summary = '复活币发放成功';
        fallbackHint = '请返回角色选择界面后重新进入以刷新显示。';
        break;
      case 'direct_name_tag':
        summary = '名称装饰卡发放成功';
        fallbackHint = '请返回角色选择界面后重新进入以刷新显示。';
        break;
      case 'direct_premium':
        summary = '账号契约发放成功';
        fallbackHint = '请返回角色选择界面后重新进入以刷新显示。';
        break;
      default:
        summary = '发放成功';
        break;
    }
    toast(`${itemLabel}：${summary}${deliveryHint || fallbackHint ? `。${deliveryHint || fallbackHint}` : ''}`);
    if (typeof loadItems === 'function'
        && currentChar
        && Number(currentChar.characterId) === characterSnapshot.characterId)
      loadItems();
    if (options
        && currentChar
        && Number(currentChar.characterId) === characterSnapshot.characterId
        && giveDeliveryMode === deliveryModeSnapshot)
      clearGiveConfiguration();
  } catch (e) {
    toast(e.message, true);
  } finally {
    setGiveRequestBusy(false);
  }
}
