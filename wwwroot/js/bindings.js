// ==== 全部事件绑定与启动调用集中在此, 且必须最后加载 ====
// 任何一行抛异常会杀死其后所有绑定(历史上 btn-cera 悬空绑定事故),
// 新增绑定一律放这里, 不要散落到各功能文件。

if (window.DfoTheme) window.DfoTheme.bind();
bindRuntimeEnvironment();
bindGivePageSize();
bindInventoryPageSize();
bindClearCharacterMailbox();
bindGiveDeliveryMode();
bindInventoryAnomalies();

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
$('#give-usable-job').onchange = () => searchItems(0);
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
$('#btn-max-personal-cargo').onclick = maxPersonalCargo;
$('#btn-unlock-equipment-slots').onclick = unlockExtraEquipmentSlots;
$('#btn-unlock-dungeon-permissions').onclick = unlockDungeonPermissions;
$('#btn-delete-character').onclick = deleteCurrentCharacter;
$('#btn-jump-character-currency').onclick = jumpToCharacterCurrency;
$('#btn-jump-account-currency').onclick = jumpToAccountCurrency;
$('#btn-open-character-clone').onclick = openCharacterClonePanel;
$('#btn-cancel-character-clone').onclick = closeCharacterClonePanel;
$('#clone-target-account').onchange = updateCloneAccountLimit;
$('#btn-check-clone-name').onclick = checkCloneCharacterName;
$('#clone-character-name').addEventListener('input', () => {
  cloneNameAvailable = false;
  $('#clone-name-state').textContent = '';
  updateCloneButtonState();
});
$('#btn-run-character-clone').onclick = runCharacterClone;
$('#btn-open-clone-account').onclick = openCloneAccountPanel;
$('#btn-close-clone-account').onclick = closeCloneAccountPanel;
$('#clone-account-form').onsubmit = createCloneAccount;
$('#btn-toggle-clone-password').onclick = () => togglePasswordInput('#clone-account-password', '#btn-toggle-clone-password');
$('#btn-toggle-clone-password-confirm').onclick = () => togglePasswordInput('#clone-account-password-confirm', '#btn-toggle-clone-password-confirm');
$('#btn-sp').onclick = adjustSp;
$('#btn-zero-sptp').onclick = zeroRemainingSpTp;
$('#btn-inventory-limit-999').onclick = setInventoryLimitTo999;
$('#btn-inventory-limit-restore').onclick = restoreNormalInventoryLimit;
$('#btn-gold-limit-max').onclick = setMaximumGoldLimit;
$('#grow-job').onchange = loadGrowOptionsForJob;
$('#grow-first').onchange = renderSecondOptions;
$('#btn-grow').onclick = setGrowType;

document.querySelectorAll('.quest-tab').forEach((tab) => {
  tab.onclick = () => {
    document.querySelectorAll('.quest-tab').forEach((t) => t.classList.remove('active'));
    document.querySelectorAll('.quest-tab-page').forEach((p) => p.classList.add('hidden'));
    tab.classList.add('active');
    $('#quest-tab-' + tab.dataset.questTab).classList.remove('hidden');
    if (tab.dataset.questTab === 'lib') searchQuestLib();
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
$('#btn-refresh-all-quests').onclick = loadAllVisibleQuests;
$('#all-quest-display-mode').onchange = setAllQuestDisplayMode;
$('#btn-complete-current-main-all').onclick = completeCurrentLevelMainQuestsFromAll;
$('#btn-complete-current-side-all').onclick = completeCurrentLevelSideQuestsFromAll;
$('#btn-complete-current-system').onclick = completeCurrentLevelSystemQuests;
$('#btn-complete-current-achievement-no-item').onclick = completeCurrentLevelNoItemAchievementQuests;
$('#btn-complete-profession-quests').onclick = completeProfessionQuestsButton;
$('#btn-complete-equipment-slot-quests').onclick = completeEquipmentSlotQuests;
$('#btn-cancel-profession-quest').onclick = closeProfessionQuestPanel;
$('#btn-confirm-profession-quest').onclick = confirmProfessionQuestChoice;
$('#btn-reset-daily-quests').onclick = resetDailyQuests;
$('#btn-refresh-main').onclick = loadMainQuests;
$('#btn-complete-current-main').onclick = completeCurrentLevelMainQuests;
$('#btn-complete-current-side').onclick = completeCurrentLevelSideQuests;
$('#btn-refresh-achieve').onclick = loadAchieveQuests;
$('#btn-titlebook-all').onclick = completeAllTitleBook;
$('#btn-titlebook-unclear-page').onclick = unclearCurrentTitleBookPage;
$('#btn-refresh-cleared').onclick = loadClearedQuests;
$('#btn-quest-search').onclick = searchQuestLib;
$('#quest-grade-filter').onchange = searchQuestLib;
$('#quest-region-filter').onchange = searchQuestLib;
$('#quest-search-input').addEventListener('keydown', (e) => { if (e.key === 'Enter') searchQuestLib(); });

initializeRuntimeEnvironment().catch((e) => toast(e.message, true));
