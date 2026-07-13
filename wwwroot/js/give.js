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

async function loadGiveCategories() {
  try {
    const data = await api('/api/items/categories');
    const nav = $('#give-category-nav');
    nav.innerHTML = '';
    if (!data.ready) {
      nav.innerHTML = '<div class="group-title">索引构建中…</div>';
      setTimeout(loadGiveCategories, 2500);
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

async function searchItems(page) {
  givePage = page || 0;
  const q = $('#search-input').value.trim();
  const minLv = parseInt($('#give-minlv').value, 10) || 0;
  const maxLv = parseInt($('#give-maxlv').value, 10) || 0;
  const raritySel = $('#give-rarity').value;
  const expiration = $('#give-expiration').value;
  const special = SPECIAL_LABELS[raritySel] ? raritySel : '';
  const rarity = special ? -1 : parseInt(raritySel, 10);
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
      tr.innerHTML = `<td>${r.itemId}</td>
        <td class="rarity-${r.rarity >= 0 && r.rarity <= 6 ? r.rarity : 0}">${escapeHtml(r.name)}</td>
        <td>${r.minLevel || ''}</td>
        <td>${r.special ? (SPECIAL_LABELS[r.special] || escapeHtml(r.special)) : (RARITY_LABELS[r.rarity] || r.rarity)}</td>
        <td title="${escapeHtml(r.tag || '')}">${escapeHtml(tagLabel(r.tag))}</td>
        <td>${templateExpirationLabel(r)}</td>
        <td><input type="number" value="1" min="1"></td><td><button class="mini">发放</button></td>`;
      tr.querySelector('button').onclick = () =>
        giveItem(r.itemId, parseInt(tr.querySelector('input').value, 10) || 1);
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

async function giveItem(templateId, count) {
  if (!currentChar) { toast('请先选择角色', true); return; }
  try {
    const r = await post(`/api/characters/${currentChar.characterId}/items`, { templateId, count });
    toast(`已发放 ${r.name || r.itemTemplateId} x${r.count} → 槽位 ${r.slot}`);
    loadItems();
  } catch (e) {
    toast(e.message, true);
  }
}
