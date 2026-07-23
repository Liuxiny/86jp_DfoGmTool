// ---- 背包 ----

// 分类按容器分组展示
const CATEGORY_GROUPS = [
  { title: '常用', cats: ['货币', '快捷栏'] },
  { title: '角色背包', cats: ['装备', '消耗品', '材料', '任务品', '副职业材料', '特殊材料', '其他'] },
  { title: '穿戴', cats: ['穿戴装备', '时装', '徽章'] },
  { title: '宠物', cats: ['宠物', '宠物装备', '宠物用品'] },
  { title: '仓库', cats: ['个人仓库', '账号金库', '账号晶块'] },
];

// 名称按品级着色(与发放页同一套 rarity-N 样式); 品级未知(-1)不着色
const rarityName = (i) => i.rarity >= 0 && i.rarity <= 6
  ? `<span class="rarity-${i.rarity}">${esc(i.name)}</span>`
  : esc(i.name);

// 每类的表格模板: 列头 + 行渲染
const CATEGORY_TEMPLATES = {
  equip: {
    cols: ['槽位', 'ID', '名称', '耐久', '品质', '期限', ''],
    row: (i) => [i.slot, i.templateId, rarityName(i), i.durability,
      qualityLabel(i.instanceValue), inventoryExpirationLabel(i), null],
  },
  stack: {
    cols: ['槽位', 'ID', '名称', '数量', '期限', ''],
    row: (i) => [i.slot, i.templateId, rarityName(i), (i.count ?? 0).toLocaleString(), inventoryExpirationLabel(i), null],
  },
  avatar: {
    cols: ['槽位', 'ID', '名称', '期限', ''],
    row: (i) => [i.slot, i.templateId, rarityName(i), inventoryExpirationLabel(i), null],
  },
  pet: {
    cols: ['槽位', 'ID', '名称', '序列号', '期限', ''],
    row: (i) => [i.slot, i.templateId, rarityName(i), i.serial ?? '', inventoryExpirationLabel(i), null],
  },
  currency: {
    cols: ['槽位', '名称', '当前值', '覆写为', ''],
    custom: 'wallet',
  },
  mixed: {
    cols: ['分类', '槽位', 'ID', '名称', '数量', '耐久', '期限', ''],
    row: (i) => [esc(i.category), i.slot, i.templateId, rarityName(i),
      i.kind === 'equipment' ? '-' : (i.count ?? 0).toLocaleString(),
      i.kind === 'equipment' ? i.durability : '-', inventoryExpirationLabel(i), null],
  },
};

function templateFor(category) {
  switch (category) {
    case '装备': return CATEGORY_TEMPLATES.equip;
    case '穿戴装备': case '时装': return CATEGORY_TEMPLATES.avatar;
    case '宠物': return CATEGORY_TEMPLATES.pet;
    case '货币': return CATEGORY_TEMPLATES.currency;
    case '全部': case '个人仓库': case '账号金库': return CATEGORY_TEMPLATES.mixed;
    default: return CATEGORY_TEMPLATES.stack; // 消耗品/材料/任务品/副职业/徽章/特殊材料/快捷栏/宠物装备/宠物用品/其他
  }
}

function qualityLabel(seed) {
  if (seed === 999999998) return '最上级';
  return seed != null ? String(seed) : '';
}

const esc = (v) => escapeHtml(v || '');

let inventoryItems = [];
let activeCategory = '全部';
let inventoryPageSize = ItemPageSize.get();
let invPage = 0; // 切分类归零; 数据刷新后越界自动回退末页
let inventoryConfiguration = null;
let inventoryConfigurationEpoch = 0;

function clearInventoryConfiguration() {
  inventoryConfigurationEpoch++;
  inventoryConfiguration = null;
  const card = $('#inventory-config-card');
  if (card) {
    card.innerHTML = '';
    FloatingConfigPanel.hide(card);
  }
  document.querySelectorAll('#item-table tr.config-selected')
    .forEach((row) => row.classList.remove('config-selected'));
}

async function loadItems() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/items`);
    if (epoch !== selectEpoch) return;
    inventoryItems = data.items;
    renderCategoryNav();
    renderItemTable();
  } catch (e) {
    toast(e.message, true);
  }
}

function renderCategoryNav() {
  const counts = new Map();
  for (const item of inventoryItems)
    counts.set(item.category, (counts.get(item.category) || 0) + 1);

  if (activeCategory !== '全部' && !counts.has(activeCategory))
    activeCategory = '全部';

  const nav = $('#category-nav');
  nav.innerHTML = '';

  const all = document.createElement('div');
  all.className = 'cat' + (activeCategory === '全部' ? ' active' : '');
  all.innerHTML = `<span>全部</span><span class="cnt">${inventoryItems.length}</span>`;
  all.onclick = () => { activeCategory = '全部'; invPage = 0; clearInventoryConfiguration(); renderCategoryNav(); renderItemTable(); };
  nav.appendChild(all);

  for (const group of CATEGORY_GROUPS) {
    const present = group.cats.filter((cat) => counts.has(cat));
    if (present.length === 0) continue;

    const title = document.createElement('div');
    title.className = 'group-title';
    title.textContent = group.title;
    nav.appendChild(title);

    for (const category of present) {
      const el = document.createElement('div');
      el.className = 'cat' + (activeCategory === category ? ' active' : '');
      el.innerHTML = `<span>${escapeHtml(category)}</span><span class="cnt">${counts.get(category)}</span>`;
      el.onclick = () => { activeCategory = category; invPage = 0; clearInventoryConfiguration(); renderCategoryNav(); renderItemTable(); };
      nav.appendChild(el);
    }
  }

  updateClearButton();
}

function updateClearButton() {
  const btn = $('#btn-clear-category');
  if (activeCategory === '全部') {
    btn.disabled = true;
    btn.textContent = '清空分类(先选分类)';
    return;
  }
  const deletable = inventoryItems.filter((i) => i.category === activeCategory && i.deletable).length;
  btn.disabled = deletable === 0;
  btn.textContent = `清空「${activeCategory}」(${deletable}件)`;
}

// 货币行(金币/复活币/技能点)行内覆写
const WALLET_TYPES = { 0: 'gold', 1: 'revive', 2: 'sp' };

function renderWalletRows(tbody, items) {
  for (const item of items) {
    const type = WALLET_TYPES[item.slot];
    const goldLimit = type === 'gold' && goldLimitStatus ? goldLimitStatus.goldCarryLimit : null;
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${item.slot}</td><td>${esc(item.name)}</td>
      <td>${(item.count ?? 0).toLocaleString()}</td>
      <td><input type="number" min="0"${goldLimit ? ` max="${goldLimit}"` : ''} class="val-input" value="${item.count ?? 0}"></td>
      <td>${type ? '<button class="mini">覆写</button>' : ''}</td>`;
    const btn = tr.querySelector('button');
    if (btn) btn.onclick = async () => {
      const value = parseInt(tr.querySelector('input').value, 10);
      if (isNaN(value) || value < 0) return toast('请输入非负整数', true);
      try {
        const result = await post(`/api/characters/${currentChar.characterId}/wallet`, { type, value });
        toast(type === 'gold' ? `金币已覆写为 ${Number(result.value).toLocaleString()}` : '已覆写');
        loadItems();
        refreshHeader();
      } catch (e) {
        toast(e.message, true);
      }
    };
    tbody.appendChild(tr);
  }
  if (items.length === 0)
    tbody.innerHTML = '<tr><td colspan="5" class="hint">没有货币行</td></tr>';
}

async function clearCurrentCategory() {
  if (!currentChar || activeCategory === '全部') return;
  const targets = inventoryItems.filter((item) => item.category === activeCategory && item.deletable);
  if (targets.length === 0)
    return toast('该分类下没有可删除的物品', true);
  if (!confirm(`清空「${activeCategory}」共 ${targets.length} 件物品？此操作不可撤销。`))
    return;
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/items/batch-delete`,
      { items: targets.map((item) => ({ listType: item.listType, slot: item.slot })) });
    toast(`「${activeCategory}」已清空 ${r.deleted} 件` + (r.failedCount ? `, 失败 ${r.failedCount}` : ''), r.failedCount > 0);
    loadItems();
  } catch (e) {
    toast(e.message, true);
  }
}

function renderInventoryActionCell(item) {
  const buttons = [];
  if (needsInventoryConfiguration(item))
    buttons.push('<button class="mini" data-act="config">配置</button>');
  if (item.deletable)
    buttons.push('<button class="mini danger" data-act="delete">删除</button>');
  return `<td>${buttons.join(' ')}</td>`;
}

function hasConfigurableInventoryExpiration(item) {
  if (!item) return false;
  if (item.templateExpiration && item.templateExpiration.dailyDeleteItem === true) return false;
  if (inventoryExpirationState(item).kind === 'none') return false;
  if (item.expirationConfigurable === true) return true;
  if (positiveEpochSeconds(item.expireTime) > 0) return true;
  const expiry = item.templateExpiration;
  return !!(expiry && expiry.known === true && !expiry.invalid
    && (positiveEpochSeconds(expiry.absoluteExpireTime) > 0 || positiveDays(expiry.usablePeriodDays) > 0));
}

function needsInventoryConfiguration(item) {
  return !!(item && (item.configurable || hasConfigurableInventoryExpiration(item)));
}

async function configureInventoryItem(item, row) {
  if (!currentChar) { toast('请先选择角色', true); return; }
  const epoch = ++inventoryConfigurationEpoch;
  try {
    const capability = await api(`/api/characters/${currentChar.characterId}/items/config-options?listType=${item.listType}&slot=${item.slot}`);
    if (epoch !== inventoryConfigurationEpoch) return;
    inventoryConfiguration = { item, capability };
    document.querySelectorAll('#item-table tr.config-selected')
      .forEach((candidate) => candidate.classList.remove('config-selected'));
    if (row) row.classList.add('config-selected');
    renderInventoryConfiguration();
  } catch (e) {
    toast(e.message, true);
  }
}

function renderInventoryConfiguration() {
  const card = $('#inventory-config-card');
  if (!inventoryConfiguration || !card) {
    clearInventoryConfiguration();
    return;
  }

  const { item, capability } = inventoryConfiguration;
  const fields = [];
  let submitDisabled = false;
  if (capability.type === 'avatar' && capability.avatar) {
    const avatar = capability.avatar;
    fields.push(`<div class="give-config-field"><span>装扮部位</span><div class="give-config-value">${escapeHtml(equipmentTypeLabel(avatar.part))}</div></div>`);
    if (!avatar.options || avatar.options.length === 0) {
      fields.push('<div class="give-config-field"><span>可选属性</span><div class="give-config-value">无可用属性</div></div>');
      submitDisabled = true;
    } else {
      fields.push(`<label class="give-config-field"><span>可选属性</span>${avatarOptionControlHtml('inventory-config-avatar-option', avatar.options, avatar.currentOptionValue)}</label>`);
    }
  } else if (capability.type === 'equipment' && capability.equipment) {
    const equipment = capability.equipment;
    fields.push(`<label class="give-config-field"><span>装备品级</span><select id="inventory-config-quality">${optionHtml(equipment.qualityOptions, equipment.currentQualityMode)}</select></label>`);
    if (equipment.canUpgrade || equipment.canAmplify) {
      fields.push(`<label class="give-config-field"><span>强化 / 增幅</span><input id="inventory-config-upgrade" type="number" min="0" max="${equipment.maxUpgradeLevel}" value="${equipment.currentUpgradeLevel || 0}"></label>`);
      fields.push(`<label class="give-config-field"><span>红字属性</span><select id="inventory-config-amplify" ${equipment.canAmplify ? '' : 'disabled'}>${optionHtml(equipment.amplifyTypes, equipment.currentAmplifyType || 0)}</select></label>`);
    }
    if (equipment.canForge)
      fields.push(`<label class="give-config-field"><span>锻造</span><input id="inventory-config-forging" type="number" min="0" max="${equipment.maxForgingLevel}" value="${equipment.currentForgingLevel || 0}"></label>`);
  }

  if (capability.expiration && capability.expiration.canOverride && inventoryExpirationState(item).kind !== 'none') {
    const expiration = capability.expiration;
    submitDisabled = false;
    fields.push(`<div class="give-config-field"><span>当前期限</span><div class="give-config-value">${inventoryExpirationLabel(item)}</div></div>`);
    if (expiration.durations && expiration.durations.length > 0) {
      const durationOptions = expiration.durations.map((value) => ({ value: value.days, label: value.label }));
      const defaultDays = expiration.defaultDays != null ? expiration.defaultDays : durationOptions[0]?.value;
      fields.push(`<label class="give-config-field"><span>期限修改</span><select id="inventory-config-expiration-mode"><option value="keep" selected>保持当前期限</option><option value="change">改为指定期限</option></select></label>`);
      fields.push(`<label id="inventory-config-expiration-days-field" class="give-config-field hidden"><span>使用期限</span><select id="inventory-config-expiration-days">${optionHtml(durationOptions, defaultDays)}</select></label>`);
    } else {
      const remainingDays = expiration.currentRemainingDays || 30;
      fields.push(`<label class="give-config-field"><span>期限修改</span><select id="inventory-config-expiration-mode"><option value="keep" selected>保持当前期限</option><option value="change">改为自定义天数</option></select></label>`);
      fields.push(`<label id="inventory-config-expiration-days-field" class="give-config-field hidden"><span>期限天数</span><input id="inventory-config-expiration-days" type="number" min="1" max="${expiration.maxDays || 3650}" value="${remainingDays}"></label>`);
    }
  }

  if (fields.length === 0) {
    fields.push('<div class="give-config-field"><span>配置</span><div class="give-config-value">该物品没有可配置项</div></div>');
    submitDisabled = true;
  }

  card.innerHTML = `<div class="give-config-head"><div class="give-config-title rarity-${item.rarity >= 0 && item.rarity <= 6 ? item.rarity : 0}">${escapeHtml(item.name)}</div><div class="give-config-meta">ID ${item.templateId} · 槽位 ${item.slot}</div></div>` +
    `<div class="give-config-grid">${fields.join('')}</div>` +
    `<div class="give-config-actions"><button id="inventory-config-cancel" type="button">取消</button><button id="inventory-config-submit" type="button" ${submitDisabled ? 'disabled' : ''}>保存配置</button></div>`;
  FloatingConfigPanel.show(card, {
    avoidSelector: '#item-table thead th:last-child',
  });

  $('#inventory-config-cancel').onclick = clearInventoryConfiguration;
  bindAvatarOptionSearch('inventory-config-avatar-option');
  const expirationMode = $('#inventory-config-expiration-mode');
  if (expirationMode) {
    expirationMode.onchange = () => {
      $('#inventory-config-expiration-days-field')?.classList.toggle('hidden', expirationMode.value !== 'change');
      FloatingConfigPanel.refresh(card);
    };
  }
  $('#inventory-config-submit').onclick = submitInventoryConfiguration;
}

async function submitInventoryConfiguration() {
  if (!inventoryConfiguration || !currentChar) return;
  const { item, capability } = inventoryConfiguration;
  const options = {};
  if (capability.type === 'avatar') {
    const avatarOption = $('#inventory-config-avatar-option');
    if (avatarOption) {
      const avatarValue = readAvatarOptionValue('inventory-config-avatar-option');
      if (!avatarValue.ok) return toast(avatarValue.error, true);
      options.avatarOptionValue = avatarValue.value;
    }
  } else if (capability.type === 'equipment') {
    options.qualityMode = parseInt($('#inventory-config-quality')?.value || '1', 10);
    options.upgradeLevel = parseInt($('#inventory-config-upgrade')?.value || '0', 10);
    options.amplifyType = parseInt($('#inventory-config-amplify')?.value || '0', 10);
    options.forgingLevel = parseInt($('#inventory-config-forging')?.value || '0', 10);
  }
  const expirationMode = $('#inventory-config-expiration-mode');
  if (expirationMode && expirationMode.value === 'change') {
    const days = parseInt($('#inventory-config-expiration-days')?.value || '0', 10);
    if (!Number.isFinite(days) || days < 0) return toast('期限设置无效', true);
    options.expirationDays = days;
  }
  if (Object.keys(options).length === 0) {
    return toast('该物品没有可配置项', true);
  }

  try {
    await post(`/api/characters/${currentChar.characterId}/items/configure`, {
      listType: item.listType,
      slot: item.slot,
      options,
    });
    toast('配置已保存');
    clearInventoryConfiguration();
    loadItems();
  } catch (e) {
    toast(e.message, true);
  }
}

function renderItemTable() {
  updateClearButton();
  const template = templateFor(activeCategory);
  const categoryItems = activeCategory === '全部'
    ? inventoryItems
    : inventoryItems.filter((item) => item.category === activeCategory);
  const expirationFilter = $('#inventory-expiration').value;
  const filtered = template.custom === 'wallet'
    ? categoryItems
    : categoryItems.filter((item) => inventoryExpirationMatchesFilter(item, expirationFilter));

  const thead = $('#item-table thead');
  thead.innerHTML = '<tr>' + template.cols.map((col) => `<th>${col}</th>`).join('') + '</tr>';

  const tbody = $('#item-table tbody');
  tbody.innerHTML = '';

  const pager = $('#inv-pager');
  pager.innerHTML = '';

  if (template.custom === 'wallet') {
    renderWalletRows(tbody, filtered);
    return;
  }

  // 每页 10 条; 删除后数据变少时越界页自动回退末页
  const pageCount = Math.max(1, Math.ceil(filtered.length / inventoryPageSize));
  if (invPage >= pageCount) invPage = pageCount - 1;
  const pageItems = filtered.slice(invPage * inventoryPageSize, (invPage + 1) * inventoryPageSize);

  for (const item of pageItems) {
    const cells = template.row(item);
    const tr = document.createElement('tr');
    // null 单元格 = 操作列, 按 configurable/deletable 渲染按钮
    tr.innerHTML = cells.map((cell) => cell === null
      ? renderInventoryActionCell(item)
      : `<td>${cell}</td>`).join('');
    const configBtn = tr.querySelector('button[data-act="config"]');
    if (configBtn) configBtn.onclick = (event) => {
      event.stopPropagation();
      configureInventoryItem(item, tr);
    };
    const deleteBtn = tr.querySelector('button[data-act="delete"]');
    if (deleteBtn) deleteBtn.onclick = async (event) => {
      event.stopPropagation();
      try {
        // count=0 整删, 单件删除直接生效
        await post(`/api/characters/${currentChar.characterId}/items/delete-at`,
          { listType: item.listType, slot: item.slot, count: 0 });
        toast('已删除');
        clearInventoryConfiguration();
        loadItems();
      } catch (e) {
        toast(e.message, true);
      }
    };
    tr.onclick = () => needsInventoryConfiguration(item) ? configureInventoryItem(item, tr) : clearInventoryConfiguration();
    tbody.appendChild(tr);
  }

  if (filtered.length === 0)
    tbody.innerHTML = `<tr><td colspan="${template.cols.length}" class="hint">当前筛选下没有物品</td></tr>`;

  if (filtered.length > inventoryPageSize) {
    const prev = document.createElement('button');
    prev.className = 'mini';
    prev.textContent = '上一页';
    prev.disabled = invPage === 0;
    prev.onclick = () => { invPage--; renderItemTable(); };
    const next = document.createElement('button');
    next.className = 'mini';
    next.textContent = '下一页';
    next.disabled = invPage >= pageCount - 1;
    next.onclick = () => { invPage++; renderItemTable(); };
    const info = document.createElement('span');
    info.className = 'hint';
    info.textContent = `共 ${filtered.length} 件 · 第 ${invPage + 1} / ${pageCount} 页`;
    pager.append(prev, info, next);
  }
}

function bindInventoryPageSize() {
  const select = $('#inventory-page-size');
  if (!select) return;
  select.value = String(inventoryPageSize);
  select.onchange = () => {
    ItemPageSize.set(select.value);
  };
  ItemPageSize.subscribe((value) => {
    if (value === inventoryPageSize && select.value === String(value)) return;
    inventoryPageSize = value;
    select.value = String(value);
    invPage = 0;
    renderItemTable();
  });
}
