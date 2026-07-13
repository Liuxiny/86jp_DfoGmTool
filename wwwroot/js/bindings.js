// ==== 全部事件绑定与启动调用集中在此, 且必须最后加载 ====
// 任何一行抛异常会杀死其后所有绑定(历史上 btn-cera 悬空绑定事故),
// 新增绑定一律放这里, 不要散落到各功能文件。

if (window.DfoTheme) window.DfoTheme.bind();
bindRuntimeEnvironment();

document.querySelectorAll('.tab[data-tab]').forEach((tab) => {
  tab.onclick = () => {
    document.querySelectorAll('.tab[data-tab]').forEach((t) => t.classList.remove('active'));
    document.querySelectorAll('.tab-page').forEach((p) => p.classList.add('hidden'));
    tab.classList.add('active');
    $('#tab-' + tab.dataset.tab).classList.remove('hidden');
  };
});

$('#btn-refresh-chars').onclick = loadAccounts;
$('#account-select').onchange = onAccountChanged;
$('#account-search').addEventListener('input', () => {
  const before = $('#account-select').value;
  renderAccountOptions();
  if ($('#account-select').value !== before) onAccountChanged();
});
$('#btn-search').onclick = () => searchItems(0);
$('#search-input').addEventListener('keydown', (e) => { if (e.key === 'Enter') searchItems(0); });
$('#give-rarity').onchange = () => searchItems(0);
$('#give-expiration').onchange = () => searchItems(0);
// 等级区间与品质下拉行为一致: 改完即生效, 回车也生效
for (const sel of ['#give-minlv', '#give-maxlv']) {
  $(sel).addEventListener('change', () => searchItems(0));
  $(sel).addEventListener('keydown', (e) => { if (e.key === 'Enter') searchItems(0); });
}
$('#btn-refresh-items').onclick = loadItems;
$('#btn-clear-category').onclick = clearCurrentCategory;
$('#inventory-expiration').onchange = () => { invPage = 0; renderItemTable(); };
$('#btn-account-panel').onclick = showAccountPanel;
$('#btn-set-level').onclick = setLevel;
$('#btn-sp').onclick = adjustSp;
$('#grow-first').onchange = renderSecondOptions;
$('#btn-grow').onclick = setGrowType;

document.querySelectorAll('.quest-tab').forEach((tab) => {
  tab.onclick = () => {
    document.querySelectorAll('.quest-tab').forEach((t) => t.classList.remove('active'));
    document.querySelectorAll('.quest-tab-page').forEach((p) => p.classList.add('hidden'));
    tab.classList.add('active');
    $('#quest-tab-' + tab.dataset.questTab).classList.remove('hidden');
  };
});

document.querySelectorAll('.char-tab').forEach((tab) => {
  tab.onclick = () => {
    document.querySelectorAll('.char-tab').forEach((t) => t.classList.remove('active'));
    document.querySelectorAll('.char-tab-page').forEach((p) => p.classList.add('hidden'));
    tab.classList.add('active');
    $('#char-tab-' + tab.dataset.charTab).classList.remove('hidden');
  };
});

document.querySelectorAll('.acc-tab').forEach((tab) => {
  tab.onclick = () => {
    document.querySelectorAll('.acc-tab').forEach((t) => t.classList.remove('active'));
    document.querySelectorAll('.acc-tab-page').forEach((p) => p.classList.add('hidden'));
    tab.classList.add('active');
    $('#acc-tab-' + tab.dataset.accTab).classList.remove('hidden');
  };
});

$('#btn-refresh-quests').onclick = loadQuests;
$('#btn-refresh-main').onclick = loadMainQuests;
$('#btn-refresh-achieve').onclick = loadAchieveQuests;
$('#btn-titlebook-all').onclick = completeAllTitleBook;
$('#btn-refresh-cleared').onclick = loadClearedQuests;
$('#btn-quest-search').onclick = searchQuestLib;
$('#quest-search-input').addEventListener('keydown', (e) => { if (e.key === 'Enter') searchQuestLib(); });

initializeRuntimeEnvironment().catch((e) => toast(e.message, true));
