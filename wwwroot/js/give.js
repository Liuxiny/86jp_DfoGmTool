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

const tagLabel = (tag) => TAG_LABELS[tag] || tag || '(无标签)';

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

let giveCategory = null; // {kind:'equipment', tag} 或 {kind:'stackable', segment}

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
      setTimeout(() => loadGiveCategories(expectedRuntimeEpoch), 2500);
      return;
    }

    const pick = (cat) => { giveCategory = cat; loadGiveCategories(); searchItems(); };
    nav.appendChild(giveCatEl('全部', null, giveCategory === null, null, () => pick(null)));

    const equipCounts = new Map(data.equipment.map((c) => [c.tag, c.count]));
    const segCounts = new Map(data.stackable.map((c) => [c.segment, c.count]));
    const listed = new Set();

    // entries: [{label, rawTitle, count, active, cat}]
    const addGroup = (title, entries) => {
      const present = entries.filter((e) => e.count != null);
      if (present.length === 0) return;
      const total = present.reduce((sum, e) => sum + e.count, 0);
      const expanded = giveNavExpanded.has(title);
      const head = document.createElement('div');
      head.className = 'group-title group-toggle';
      head.innerHTML = `<span><span class="toggle">${expanded ? '▾' : '▸'}</span>${escapeHtml(title)}</span><span class="cnt">${total}</span>`;
      head.onclick = () => {
        if (giveNavExpanded.has(title)) giveNavExpanded.delete(title);
        else giveNavExpanded.add(title);
        loadGiveCategories();
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
      addGroup(group.title, group.tags.map(equipEntry));

    addGroup('消耗品 / 材料', STACK_SEGMENTS.map((seg) => ({
      label: seg,
      rawTitle: '与背包入格分类同语义',
      count: segCounts.get(seg),
      active: !!(giveCategory && giveCategory.kind === 'stackable' && giveCategory.segment === seg),
      cat: { kind: 'stackable', segment: seg },
    })));

    const leftovers = data.equipment.filter((c) => !listed.has(c.tag))
      .sort((a, b) => b.count - a.count);
    addGroup('其他', leftovers.map((c) => equipEntry(c.tag)));
  } catch (e) {
    toast(e.message, true);
  }
}

const GIVE_PAGE_SIZE = 10;
let givePage = 0; // 从 0 计; 换筛选条件时归零
let giveConfiguration = null;
let giveConfigurationEpoch = 0;
let giveSearchSignature = '';

function clearGiveConfiguration() {
  giveConfigurationEpoch++;
  giveConfiguration = null;
  const card = $('#give-config-card');
  if (card) {
    card.innerHTML = '';
    card.classList.add('hidden');
  }
  document.querySelectorAll('#search-results tr.config-selected')
    .forEach((row) => row.classList.remove('config-selected'));
}

function isLimitedTemplate(item) {
  const expiry = item && item.templateExpiration;
  return !!(expiry && (expiry.absoluteExpireTime > 0 || expiry.usablePeriodDays > 0 || expiry.dailyDeleteItem));
}

function needsGrantConfiguration(item) {
  return item && (item.kind === 'equipment' || item.requiresManualGrantType || isLimitedTemplate(item));
}

function optionHtml(options, selectedValue) {
  return (options || []).map((option) => {
    const selected = String(option.value) === String(selectedValue) ? ' selected' : '';
    return `<option value="${escapeHtml(String(option.value))}"${selected}>${escapeHtml(localizeConfigOptionLabel(option.label))}</option>`;
  }).join('');
}

async function configureGrantItem(item, row) {
  if (!currentChar) { toast('请先选择角色', true); return; }
  const epoch = ++giveConfigurationEpoch;
  try {
    const capability = await api(`/api/characters/${currentChar.characterId}/items/${item.itemId}/grant-options`);
    if (epoch !== giveConfigurationEpoch) return;
    giveConfiguration = { item, capability };
    document.querySelectorAll('#search-results tr.config-selected')
      .forEach((candidate) => candidate.classList.remove('config-selected'));
    if (row) row.classList.add('config-selected');
    renderGrantConfiguration();
    $('#give-config-card').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
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
    const supportsEquipmentAttributes = equipment.canUpgrade || equipment.canAmplify || equipment.canForge;
    if (supportsEquipmentAttributes) {
      fields.push(`<label class="give-config-field"><span>装备品级</span><select id="give-config-quality">${optionHtml(equipment.qualityOptions, 1)}</select></label>`);
      fields.push(`<label class="give-config-field"><span>强化 / 增幅</span><input id="give-config-upgrade" type="number" min="0" max="${equipment.maxUpgradeLevel}" value="0" ${equipment.canUpgrade || equipment.canAmplify ? '' : 'disabled'}></label>`);
      fields.push(`<label class="give-config-field"><span>红字属性</span><select id="give-config-amplify" ${equipment.canAmplify ? '' : 'disabled'}>${optionHtml(equipment.amplifyTypes, 0)}</select></label>`);
    }
    if (equipment.canForge)
      fields.push(`<label class="give-config-field"><span>锻造</span><input id="give-config-forging" type="number" min="0" max="${equipment.maxForgingLevel}" value="0"></label>`);
  }

  if (capability.manual && capability.manual.required) {
    fields.push(`<label class="give-config-field"><span>手动分类</span><select id="give-config-manual-type">${optionHtml(capability.manual.choices, capability.manual.choices[0]?.value || '')}</select></label>`);
  }

  let grantDisabled = false;
  if (capability.avatar) {
    const avatar = capability.avatar;
    fields.push(`<div class="give-config-field"><span>装扮部位</span><div class="give-config-value">${escapeHtml(tagLabel((avatar.part || '').replace(/[\[\]`]/g, '').trim()))}</div></div>`);
    if (!avatar.compatible || !avatar.options || avatar.options.length === 0) {
      fields.push('<div class="give-config-field"><span>可选属性</span><div class="give-config-value">当前职业不可用</div></div>');
      grantDisabled = true;
    } else {
      fields.push(`<label class="give-config-field"><span>可选属性</span><select id="give-config-avatar-option">${optionHtml(avatar.options, avatar.options[0].value)}</select></label>`);
    }
    if (avatar.durations && avatar.durations.length > 0) {
      const permanent = avatar.durations.find((value) => value.days === 0);
      const selectedDays = permanent ? 0 : avatar.durations[0].days;
      const durationOptions = avatar.durations.map((value) => ({ value: value.days, label: value.label }));
      fields.push(`<label class="give-config-field"><span>使用期限</span><select id="give-config-avatar-duration">${optionHtml(durationOptions, selectedDays)}</select></label>`);
    }
  } else if (capability.expiration && capability.expiration.limited) {
    if (capability.expiration.canOverride) {
      fields.push('<label class="give-config-field"><span>期限方式</span><select id="give-config-expiration-mode"><option value="default">PVF 默认期限</option><option value="custom">自定义天数</option></select></label>');
      fields.push(`<label id="give-config-expiration-days-field" class="give-config-field hidden"><span>期限天数</span><input id="give-config-expiration-days" type="number" min="1" max="${capability.expiration.maxDays}" value="30"></label>`);
    } else {
      fields.push('<div class="give-config-field"><span>使用期限</span><div class="give-config-value">PVF 固定规则</div></div>');
    }
  }

  card.innerHTML = `<div class="give-config-head"><div class="give-config-title rarity-${item.rarity >= 0 && item.rarity <= 6 ? item.rarity : 0}">${escapeHtml(item.name)}</div><div class="give-config-meta">ID ${item.itemId} · ${escapeHtml(tagLabel(item.tag))}</div></div>` +
    `<div class="give-config-grid">${fields.join('')}</div>` +
    `<div class="give-config-actions"><button id="give-config-cancel" class="mini" type="button">取消</button><button id="give-config-submit" type="button" ${grantDisabled ? 'disabled' : ''}>发放</button></div>`;
  card.classList.remove('hidden');

  $('#give-config-cancel').onclick = clearGiveConfiguration;
  const expirationMode = $('#give-config-expiration-mode');
  if (expirationMode) {
    expirationMode.onchange = () => {
      $('#give-config-expiration-days-field').classList.toggle('hidden', expirationMode.value !== 'custom');
    };
  }
  $('#give-config-submit').onclick = submitConfiguredGrant;
}

async function submitConfiguredGrant() {
  if (!giveConfiguration) return;
  const { item, capability } = giveConfiguration;
  const count = Math.max(1, parseInt($('#give-config-count').value, 10) || 1);
  const options = {
    qualityMode: parseInt($('#give-config-quality')?.value || '1', 10),
    upgradeLevel: parseInt($('#give-config-upgrade')?.value || '0', 10),
    amplifyType: parseInt($('#give-config-amplify')?.value || '0', 10),
    forgingLevel: parseInt($('#give-config-forging')?.value || '0', 10),
  };
  const avatarOption = $('#give-config-avatar-option');
  if (avatarOption) options.avatarOptionValue = parseInt(avatarOption.value, 10);
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
  const special = SPECIAL_LABELS[raritySel] ? raritySel : '';
  const rarity = special ? -1 : parseInt(raritySel, 10);
  const signature = JSON.stringify({ q, minLv, maxLv, raritySel, expiration, giveCategory, characterId: currentChar?.characterId });
  if (signature !== giveSearchSignature) {
    giveSearchSignature = signature;
    clearGiveConfiguration();
  }
  if (!q && !giveCategory && minLv === 0 && maxLv === 0 && rarity < 0 && !special && expiration === 'all') {
    $('#search-results tbody').innerHTML =
      '<tr><td colspan="8" class="hint">选择左侧分类或输入关键词开始浏览</td></tr>';
    $('#give-total').textContent = '';
    $('#give-pager').innerHTML = '';
    return;
  }
  try {
    let url = `/api/items/browse?limit=${GIVE_PAGE_SIZE}&offset=${givePage * GIVE_PAGE_SIZE}` +
      `&q=${encodeURIComponent(q)}&minLevel=${minLv}&maxLevel=${maxLv}&rarity=${rarity}` +
      `&expiration=${encodeURIComponent(expiration)}`;
    if (currentChar) url += `&job=${currentChar.job}`;
    if (special) url += `&special=${special}`;
    if (giveCategory) {
      url += `&kind=${encodeURIComponent(giveCategory.kind)}`;
      if (giveCategory.tag) url += `&tag=${encodeURIComponent(giveCategory.tag)}`;
      if (giveCategory.segment) url += `&segment=${encodeURIComponent(giveCategory.segment)}`;
    }
    const data = await api(url);
    const pageCount = Math.max(1, Math.ceil(data.total / GIVE_PAGE_SIZE));
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
        <td>${templateExpirationLabel(r)}</td>` +
        (configurable
          ? '<td class="hint">配置后发放</td><td><button class="mini">配置</button></td>'
          : '<td><input type="number" value="1" min="1"></td><td><button class="mini">发放</button></td>');
      tr.querySelector('button').onclick = configurable
        ? () => configureGrantItem(r, tr)
        : () => giveItem(r.itemId, parseInt(tr.querySelector('input').value, 10) || 1);
      tbody.appendChild(tr);
    }
    if (data.results.length === 0)
      tbody.innerHTML = '<tr><td colspan="8" class="hint">没有匹配的物品</td></tr>';

    const pager = $('#give-pager');
    pager.innerHTML = '';
    if (data.total > GIVE_PAGE_SIZE) {
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

async function giveItem(templateId, count, options) {
  if (!currentChar) { toast('请先选择角色', true); return; }
  try {
    const body = { templateId, count };
    if (options) body.options = options;
    const r = await post(`/api/characters/${currentChar.characterId}/items`, body);
    toast(`已发放 ${r.name || r.itemTemplateId} x${r.count} → 槽位 ${r.slot}`);
    if (options) clearGiveConfiguration();
    loadItems();
  } catch (e) {
    toast(e.message, true);
  }
}
