using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace Jp86.GmClient;

public partial class MainWindow : Window
{
    private readonly SecureApiClient _api = new();
    private readonly LocalPvfCatalog _localCatalog = new();
    private readonly NpkIconProvider _iconProvider = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<AccountRow> _accounts = new();
    private readonly ObservableCollection<CharacterRow> _allCharacters = new();
    private readonly ObservableCollection<CharacterRow> _visibleCharacters = new();
    private readonly ObservableCollection<ItemRow> _items = new();
    private readonly ObservableCollection<InventoryRow> _allInventory = new();
    private readonly ObservableCollection<InventoryRow> _visibleInventory = new();
    private readonly ObservableCollection<QuestRow> _quests = new();
    private readonly ObservableCollection<StatRow> _stats = new();
    private readonly ObservableCollection<PermissionRow> _permissions = new();
    private readonly ObservableCollection<LogRow> _logs = new();
    private AccountRow? _currentAccount;
    private CharacterRow? _currentCharacter;
    private int _loginAccountId;
    private int _role;
    private string _accountName = "";
    private bool _changingContext;

    public MainWindow()
    {
        InitializeComponent();
        AccountList.ItemsSource = _accounts;
        CharacterList.ItemsSource = _visibleCharacters;
        ItemResults.ItemsSource = _items;
        InventoryGrid.ItemsSource = _visibleInventory;
        QuestGrid.ItemsSource = _quests;
        StatsGrid.ItemsSource = _stats;
        PermissionsGrid.ItemsSource = _permissions;
        LogsGrid.ItemsSource = _logs;
        Loaded += MainWindow_Loaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _api.Dispose();
        base.OnClosed(e);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeLocalCatalogAsync();
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await LoginAsync();
    private async void LoginPassword_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await LoginAsync(); }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginAccount.Text) || LoginPassword.Password.Length == 0)
        {
            LoginStatus.Text = "请输入游戏账号和密码。";
            return;
        }
        LoginButton.IsEnabled = false;
        LoginStatus.Text = "正在验证…";
        try
        {
            using var document = await _api.PostAsync("/api/auth/login", new { accountName = LoginAccount.Text.Trim(), password = LoginPassword.Password });
            var root = document.RootElement;
            EnsureSuccess(root);
            _api.SetToken(ReadString(root, "token"));
            _loginAccountId = ReadInt(root, "accountId");
            _accountName = ReadString(root, "accountName");
            _role = ReadInt(root, "role");
            LoginPassword.Clear();
            ConfigureRole();
            LoginPanel.Visibility = Visibility.Collapsed;
            Shell.Visibility = Visibility.Visible;
            await LoadAccountsAsync();
        }
        catch (Exception ex) { LoginStatus.Text = Friendly(ex); }
        finally { LoginButton.IsEnabled = true; }
    }

    private void ConfigureRole()
    {
        UserText.Text = _accountName;
        RoleText.Text = RoleLabel(_role);
        AccountChooserPanel.Visibility = _role >= 3 ? Visibility.Visible : Visibility.Collapsed;
        ItemAdvancedOptions.Visibility = _role >= 2 ? Visibility.Visible : Visibility.Collapsed;
        InventoryTab.Visibility = _role >= 2 ? Visibility.Visible : Visibility.Collapsed;
        CharacterTab.Visibility = _role >= 2 ? Visibility.Visible : Visibility.Collapsed;
        QuestTab.Visibility = _role >= 2 ? Visibility.Visible : Visibility.Collapsed;
        AccountTab.Visibility = _role >= 2 ? Visibility.Visible : Visibility.Collapsed;
        LogsTab.Visibility = _role >= 3 ? Visibility.Visible : Visibility.Collapsed;
        PermissionsTab.Visibility = _role >= 3 ? Visibility.Visible : Visibility.Collapsed;
        ConfigureItemCategories();
        WorkspaceTabs.SelectedItem = GrantTab;
    }

    private void ConfigureItemCategories()
    {
        var selected = (ItemCategory.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "equipment";
        ItemCategory.Items.Clear();
        ItemCategory.Items.Add(new ComboBoxItem { Content = "装备", Tag = "equipment" });
        if (_role >= 2) ItemCategory.Items.Add(new ComboBoxItem { Content = "消耗品", Tag = "stackable" });
        ItemCategory.SelectedItem = ItemCategory.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.Ordinal))
            ?? ItemCategory.Items[0];
    }

    private async Task LoadAccountsAsync(string query = "")
    {
        SetBusy("正在读取账号与角色…");
        try
        {
            using var document = await _api.GetAsync("/api/accounts");
            EnsureSuccess(document.RootElement);
            var loaded = new List<AccountRow>();
            foreach (var item in document.RootElement.GetProperty("accounts").EnumerateArray())
            {
                var characterNames = item.TryGetProperty("characterNames", out var names)
                    ? string.Join(" ", names.EnumerateArray().Select(n => n.GetString() ?? "")) : "";
                var row = new AccountRow
                {
                    AccountId = ReadInt(item, "accountId"), Name = ReadString(item, "name"),
                    Cera = ReadLong(item, "cera"), TokenCera = ReadLong(item, "tokenCera"),
                    LuckyStar = ReadLong(item, "luckyStar"), CharacterCount = ReadInt(item, "characterCount"),
                };
                if (string.IsNullOrWhiteSpace(query) || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.AccountId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)
                    || characterNames.Contains(query, StringComparison.OrdinalIgnoreCase)) loaded.Add(row);
            }

            _changingContext = true;
            _accounts.Clear();
            foreach (var row in loaded) _accounts.Add(row);
            var selected = _accounts.FirstOrDefault(a => a.AccountId == (_currentAccount?.AccountId ?? _loginAccountId))
                ?? _accounts.FirstOrDefault(a => a.AccountId == _loginAccountId) ?? _accounts.FirstOrDefault();
            AccountList.SelectedItem = selected;
            _changingContext = false;
            if (selected != null) await SelectAccountAsync(selected);
            else SetBusy("没有找到匹配的账号");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task SelectAccountAsync(AccountRow account)
    {
        if (_changingContext) return;
        _currentAccount = account;
        ContextAccountText.Text = account.Name;
        ContextAccountIdText.Text = $"#{account.AccountId}";
        ContextCeraText.Text = Compact(account.Cera);
        ContextTokenText.Text = Compact(account.TokenCera);
        ContextLuckyText.Text = Compact(account.LuckyStar);
        SetBusy($"正在载入 {account.Name} 的角色…");
        try
        {
            var previousCharacterId = _currentCharacter?.AccountId == account.AccountId ? _currentCharacter.CharacterId : 0;
            using var document = await _api.GetAsync("/api/characters?accountId=" + account.AccountId);
            EnsureSuccess(document.RootElement);
            _allCharacters.Clear();
            foreach (var item in document.RootElement.GetProperty("characters").EnumerateArray())
                _allCharacters.Add(new CharacterRow
                {
                    CharacterId = ReadInt(item, "characterId"), AccountId = ReadInt(item, "accountId"),
                    Name = ReadString(item, "name"), Level = ReadInt(item, "level"), JobName = ReadString(item, "jobName"),
                });
            ApplyCharacterFilter();
            CharacterCountText.Text = $"{_allCharacters.Count} 个";
            var selected = _visibleCharacters.FirstOrDefault(c => c.CharacterId == previousCharacterId) ?? _visibleCharacters.FirstOrDefault();
            _changingContext = true;
            CharacterList.SelectedItem = selected;
            if (selected != null) CharacterList.ScrollIntoView(selected);
            _changingContext = false;
            if (selected != null) await SelectCharacterAsync(selected);
            else ClearCharacterContext();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task SelectCharacterAsync(CharacterRow character)
    {
        _currentCharacter = character;
        CurrentCharacterText.Text = character.Name;
        CurrentCharacterMetaText.Text = $"Lv.{character.Level}  ·  {character.JobName}  ·  角色 ID {character.CharacterId}";
        SetBusy($"正在读取 {character.Name} 的数据…");
        await LoadCharacterDetailAsync();
        await LoadCurrentTabAsync();
    }

    private async Task LoadCharacterDetailAsync()
    {
        if (_currentCharacter == null) return;
        try
        {
            using var document = await _api.GetAsync($"/api/characters/{_currentCharacter.CharacterId}");
            var root = document.RootElement;
            EnsureSuccess(root);
            var wallet = root.GetProperty("wallet");
            HeaderGoldText.Text = Compact(ReadLong(wallet, "gold"));
            HeaderCeraText.Text = Compact(ReadLong(wallet, "cera"));
            HeaderTokenText.Text = Compact(ReadLong(wallet, "tokenCera"));
            _currentCharacter.Level = ReadInt(root, "level");
            _currentCharacter.JobName = ReadString(root, "jobName");
            CurrentCharacterMetaText.Text = $"Lv.{_currentCharacter.Level}  ·  {_currentCharacter.JobName}  ·  角色 ID {_currentCharacter.CharacterId}";
            if (_currentAccount != null)
            {
                _currentAccount.Cera = ReadLong(wallet, "cera");
                _currentAccount.TokenCera = ReadLong(wallet, "tokenCera");
                _currentAccount.LuckyStar = ReadLong(wallet, "luckyStar");
                ContextCeraText.Text = Compact(_currentAccount.Cera);
                ContextTokenText.Text = Compact(_currentAccount.TokenCera);
                ContextLuckyText.Text = Compact(_currentAccount.LuckyStar);
            }
            StatusText.Text = $"当前角色：{_currentCharacter.Name}";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ClearCharacterContext()
    {
        _currentCharacter = null;
        CurrentCharacterText.Text = "该账号没有角色";
        CurrentCharacterMetaText.Text = "请先在游戏中创建角色";
        HeaderGoldText.Text = HeaderCeraText.Text = HeaderTokenText.Text = "—";
        _allInventory.Clear(); _visibleInventory.Clear(); _quests.Clear(); _stats.Clear();
    }

    private async void SearchAccounts_Click(object sender, RoutedEventArgs e) => await LoadAccountsAsync(AccountQuery.Text.Trim());
    private async void AccountQuery_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await LoadAccountsAsync(AccountQuery.Text.Trim()); }
    private async void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_changingContext && AccountList.SelectedItem is AccountRow row) await SelectAccountAsync(row); }
    private async void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_changingContext && CharacterList.SelectedItem is CharacterRow row) await SelectCharacterAsync(row); }
    private void CharacterQuery_TextChanged(object sender, TextChangedEventArgs e) => ApplyCharacterFilter();

    private void ApplyCharacterFilter()
    {
        if (CharacterList == null || CharacterQuery == null) return;
        var q = CharacterQuery.Text.Trim();
        var selectedId = _currentCharacter?.CharacterId ?? 0;
        _visibleCharacters.Clear();
        foreach (var row in _allCharacters.Where(c => string.IsNullOrEmpty(q) || c.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase))) _visibleCharacters.Add(row);
        if (selectedId > 0) CharacterList.SelectedItem = _visibleCharacters.FirstOrDefault(c => c.CharacterId == selectedId);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAccount != null) await SelectAccountAsync(_currentAccount); else await LoadAccountsAsync();
    }
    private async void RefreshCharacter_Click(object sender, RoutedEventArgs e) => await LoadCharacterDetailAsync();

    private async void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == WorkspaceTabs) await LoadCurrentTabAsync();
    }

    private async Task LoadCurrentTabAsync()
    {
        if (_currentCharacter == null) return;
        if (WorkspaceTabs.SelectedItem == InventoryTab) await LoadInventoryAsync();
        else if (WorkspaceTabs.SelectedItem == CharacterTab) await LoadCharacterPanelsAsync();
        else if (WorkspaceTabs.SelectedItem == QuestTab && _quests.Count == 0) await SearchQuestsAsync();
        else if (WorkspaceTabs.SelectedItem == LogsTab && _role >= 3) await LoadLogsAsync();
        else if (WorkspaceTabs.SelectedItem == PermissionsTab && _role >= 3) await LoadPermissionsAsync();
    }

    private async void SearchItems_Click(object sender, RoutedEventArgs e) => await SearchItemsAsync();
    private async void ItemQuery_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await SearchItemsAsync(); }
    private async void ItemCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && Shell.Visibility == Visibility.Visible) await SearchItemsAsync();
    }

    private async Task SearchItemsAsync()
    {
        if (!RequireCharacter()) return;
        if (!_localCatalog.IsReady)
        {
            MessageBox.Show(this, "尚未载入本地 PVF。请选择 PVF 所在资源目录。", "本地物品索引", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetBusy("正在搜索本地 PVF 物品…");
        try
        {
            var kind = (ItemCategory.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "equipment";
            if (_role < 2) kind = "equipment";
            var records = _localCatalog.Search(ItemQuery.Text, kind, 300).ToArray();
            _items.Clear();
            foreach (var item in records)
                _items.Add(new ItemRow
                {
                    ItemId = item.ItemId, Name = item.Name, Kind = item.Kind, Category = item.Category,
                    Rarity = item.Rarity, MinLevel = item.MinLevel, IconPath = item.IconPath, IconIndex = item.IconIndex,
                });
            if (_items.Count > 0) ItemResults.SelectedIndex = 0;
            StatusText.Text = $"找到 {_items.Count} 件物品";
            _ = LoadItemIconsAsync(_items.Take(300).ToArray(), _lifetime.Token);
            await Task.CompletedTask;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task LoadItemIconsAsync(ItemRow[] rows, CancellationToken cancellationToken)
    {
        try
        {
            using var gate = new SemaphoreSlim(4, 4);
            var tasks = rows.Where(row => row.IconIndex >= 0 && row.IconPath.Length > 0).Select(async row =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var icon = await _iconProvider.GetIconAsync(row.IconPath, row.IconIndex, cancellationToken).ConfigureAwait(false);
                    if (icon != null) await Dispatcher.InvokeAsync(() => row.Icon = icon, DispatcherPriority.Background, cancellationToken);
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch { /* 图标失败不影响物品搜索和发放。 */ }
    }

    private async Task InitializeLocalCatalogAsync()
    {
        PvfStatusText.Text = "正在检查本地 PVF 与缓存…";
        try
        {
            var ready = await _localCatalog.InitializeAsync(_lifetime.Token);
            _iconProvider.SetDirectory(_localCatalog.Settings.ImagePacks2Directory);
            PvfStatusText.Text = ready
                ? $"本地 PVF 已加载：{_localCatalog.Items.Count:N0} 件物品"
                : "未配置本地 PVF，请选择资源目录";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { PvfStatusText.Text = "PVF 加载失败：" + Friendly(ex); }
    }

    private async void ChoosePvfDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = ChooseFolder("选择包含 Script.pvf 的文件夹", _localCatalog.Settings.PvfDirectory);
        if (folder == null) return;
        RebuildPvfButton.IsEnabled = false;
        PvfStatusText.Text = "正在解析 PVF 并生成本地缓存…";
        try
        {
            await _localCatalog.ConfigurePvfDirectoryAsync(folder, _lifetime.Token);
            _iconProvider.SetDirectory(_localCatalog.Settings.ImagePacks2Directory);
            PvfStatusText.Text = $"本地 PVF 已加载：{_localCatalog.Items.Count:N0} 件物品";
            await SearchItemsAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, Friendly(ex), "资源目录无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { RebuildPvfButton.IsEnabled = true; }
    }

    private void ChooseImagePacksDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = ChooseFolder("选择 ImagePacks2 文件夹", _localCatalog.Settings.ImagePacks2Directory);
        if (folder == null) return;
        try
        {
            _localCatalog.ConfigureImagePacks2Directory(folder);
            _iconProvider.SetDirectory(folder);
            PvfStatusText.Text = $"图标目录已保存；本地 PVF 共 {_localCatalog.Items.Count:N0} 件物品";
            _ = LoadItemIconsAsync(_items.ToArray(), _lifetime.Token);
        }
        catch (Exception ex) { MessageBox.Show(this, Friendly(ex), "图标目录无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void RebuildPvf_Click(object sender, RoutedEventArgs e)
    {
        RebuildPvfButton.IsEnabled = false;
        PvfStatusText.Text = "正在重新解析 PVF…";
        try
        {
            await _localCatalog.RebuildAsync(_lifetime.Token);
            PvfStatusText.Text = $"缓存已重建：{_localCatalog.Items.Count:N0} 件物品";
            await SearchItemsAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, Friendly(ex), "PVF 解析失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { RebuildPvfButton.IsEnabled = true; }
    }

    private static string? ChooseFolder(string description, string initialDirectory)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initialDirectory) ? initialDirectory : "",
            ShowNewFolderButton = false,
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private async void GrantItem_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCharacter() || ItemResults.SelectedItem is not ItemRow item || !PositiveInt(ItemCount.Text, out var count))
        { MessageBox.Show(this, "请选择物品并填写正确数量。", "86JP GM", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var upgrade = 0; var forging = 0; var amplify = 0;
        if (_role >= 2)
        {
            if (!RangedInt(UpgradeLevel.Text, 0, 31, out upgrade) || !RangedInt(ForgingLevel.Text, 0, 8, out forging))
            { MessageBox.Show(this, "强化等级须为 0–31，锻造等级须为 0–8。", "86JP GM"); return; }
            _ = int.TryParse((AmplifyType.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out amplify);
        }
        try
        {
            SetBusy("正在发送物品…");
            using var document = await _api.PostAsync($"/api/characters/{_currentCharacter!.CharacterId}/items", new
            {
                templateId = item.ItemId, count,
                options = new { qualityMode = 1, upgradeLevel = upgrade, amplifyType = amplify, forgingLevel = forging },
                requestId = Guid.NewGuid().ToString("N"), deliveryMode = "mail",
            });
            EnsureSuccess(document.RootElement);
            MessageBox.Show(this, $"{item.Name} × {count} 已发送到 {_currentCharacter.Name}。\n在线角色请重新打开邮箱。", "发放成功", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "物品发放成功";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Cera_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCharacter() || !NonZeroInt(CeraAmount.Text, out var amount)) { ShowValueError(); return; }
        var type = (CeraType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "cera";
        await PostCharacterActionAsync("cera", new { amount, type }, "点券调整成功");
    }

    private async Task LoadInventoryAsync()
    {
        if (!RequireCharacter()) return;
        try
        {
            SetBusy("正在读取背包…");
            using var document = await _api.GetAsync($"/api/characters/{_currentCharacter!.CharacterId}/items");
            EnsureSuccess(document.RootElement);
            _allInventory.Clear();
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
                _allInventory.Add(new InventoryRow
                {
                    Container = ReadString(item, "container"), Category = ReadString(item, "category"), ListType = ReadInt(item, "listType"),
                    Slot = ReadInt(item, "slot"), TemplateId = ReadInt(item, "templateId"), Name = ReadString(item, "name"), Kind = ReadString(item, "kind"),
                    Rarity = ReadInt(item, "rarity"), Count = ReadInt(item, "count"), Durability = ReadInt(item, "durability"),
                    Deletable = ReadBool(item, "deletable"), Configurable = ReadBool(item, "configurable"),
                });
            var currentCategory = (InventoryCategory.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            InventoryCategory.Items.Clear(); InventoryCategory.Items.Add(new ComboBoxItem { Content = "全部分类", Tag = "" });
            foreach (var category in _allInventory.Select(i => i.Category).Distinct().OrderBy(v => v)) InventoryCategory.Items.Add(new ComboBoxItem { Content = category, Tag = category });
            InventoryCategory.SelectedItem = InventoryCategory.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag?.ToString() ?? "") == currentCategory) ?? InventoryCategory.Items[0];
            ApplyInventoryFilter();
            StatusText.Text = $"背包共 {_allInventory.Count} 个槽位";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void LoadInventory_Click(object sender, RoutedEventArgs e) => await LoadInventoryAsync();
    private void InventoryFilter_Changed(object sender, EventArgs e) => ApplyInventoryFilter();
    private void ApplyInventoryFilter()
    {
        if (InventoryCategory == null || InventoryQuery == null) return;
        var category = (InventoryCategory.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var q = InventoryQuery.Text.Trim(); _visibleInventory.Clear();
        foreach (var item in _allInventory.Where(i => (category.Length == 0 || i.Category == category) && (q.Length == 0 || i.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase)))) _visibleInventory.Add(item);
    }
    private async void DeleteInventoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireCharacter() || InventoryGrid.SelectedItem is not InventoryRow item) { MessageBox.Show(this, "请先选择要删除的物品。", "86JP GM"); return; }
        if (!item.Deletable) { MessageBox.Show(this, "该槽位受服务端保护，不能删除。", "86JP GM"); return; }
        if (MessageBox.Show(this, $"确定删除「{item.Name}」× {item.Count}？\n此操作不可撤销。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var document = await _api.PostAsync($"/api/characters/{_currentCharacter!.CharacterId}/items/delete-at", new { listType = item.ListType, slot = item.Slot, count = 0 });
            EnsureSuccess(document.RootElement); await LoadInventoryAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task LoadCharacterPanelsAsync()
    {
        if (!RequireCharacter()) return;
        var character = _currentCharacter!;
        var errors = new List<string>();
        try
        {
            using (var document = await _api.GetAsync($"/api/characters/{character.CharacterId}/stats"))
            {
                EnsureSuccess(document.RootElement); _stats.Clear();
                foreach (var item in document.RootElement.GetProperty("stats").EnumerateArray()) _stats.Add(new StatRow { Label = ReadString(item, "label"), Value = ReadLong(item, "value") });
            }
        }
        catch (Exception ex)
        {
            _stats.Clear();
            errors.Add("基础属性：" + Friendly(ex));
        }

        try
        {
            using (var document = await _api.GetAsync($"/api/characters/{character.CharacterId}/sptp"))
            {
                EnsureSuccess(document.RootElement); var root = document.RootElement;
                var summary = $"剩余 SP {ReadLong(root, "remainingSp"):N0} / 总 SP {ReadLong(root, "totalSp"):N0}\n剩余 TP {ReadLong(root, "remainingTp"):N0} / 总 TP {ReadLong(root, "totalTp"):N0}";
                if (ReadBool(root, "approximate"))
                    summary += "\n⚠ " + ReadString(root, "warning");
                SpTpSummaryText.Text = summary;
            }
        }
        catch (Exception ex)
        {
            SpTpSummaryText.Text = "SP/TP 暂时无法读取；其他角色属性仍可正常操作。";
            errors.Add("SP/TP：" + Friendly(ex));
        }

        StatusText.Text = errors.Count == 0 ? "角色属性已更新" : string.Join("；", errors);
    }
    private async void SetLevel_Click(object sender, RoutedEventArgs e) { if (!RangedInt(LevelValue.Text, 1, 99, out var level)) { ShowValueError(); return; } await PostCharacterActionAsync("level", new { level }, "等级设置成功", true); }
    private async void AdjustGold_Click(object sender, RoutedEventArgs e) { if (!NonZeroInt(GoldValue.Text, out var amount)) { ShowValueError(); return; } await PostCharacterActionAsync("gold", new { amount }, "金币调整成功", true); }
    private async void AdjustSpTp_Click(object sender, RoutedEventArgs e) { if (!int.TryParse(SpValue.Text, out var sp) || !int.TryParse(TpValue.Text, out var tp) || (sp == 0 && tp == 0)) { ShowValueError(); return; } await PostCharacterActionAsync("sp", new { sp, tp }, "SP/TP 调整成功"); await LoadCharacterPanelsAsync(); }
    private async void ZeroSpTp_Click(object sender, RoutedEventArgs e) { if (!Confirm("确定重置当前角色剩余 SP/TP？")) return; await PostCharacterActionAsync("sp/zero-remaining", null, "剩余 SP/TP 已重置"); await LoadCharacterPanelsAsync(); }
    private async void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        var dangerous = action == "mailbox/clear";
        if (dangerous && !Confirm("确定清空当前角色邮箱？此操作不可撤销。")) return;
        await PostCharacterActionAsync(action, null, "操作成功");
        await LoadCharacterDetailAsync();
    }

    private async void SearchQuests_Click(object sender, RoutedEventArgs e) => await SearchQuestsAsync();
    private async void QuestQuery_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await SearchQuestsAsync(); }
    private async Task SearchQuestsAsync()
    {
        if (!RequireCharacter()) return;
        try
        {
            SetBusy("正在搜索任务…");
            var q = Uri.EscapeDataString(QuestQuery.Text.Trim());
            var grade = Uri.EscapeDataString((QuestGrade.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");
            using var document = await _api.GetAsync($"/api/characters/{_currentCharacter!.CharacterId}/quests/search?q={q}&grade={grade}&region=&limit=500");
            EnsureSuccess(document.RootElement); _quests.Clear();
            foreach (var item in document.RootElement.GetProperty("results").EnumerateArray()) _quests.Add(new QuestRow { QuestId = ReadInt(item, "questId"), Name = ReadString(item, "name"), GradeLabel = ReadString(item, "gradeLabel"), RegionLabel = ReadString(item, "regionLabel"), MinLevel = ReadInt(item, "minLevel"), Status = TranslateQuestStatus(ReadString(item, "status")) });
            StatusText.Text = $"找到 {_quests.Count} 个任务";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void CompleteQuest_Click(object sender, RoutedEventArgs e) => await RunQuestActionAsync("complete", "标记完成");
    private async void CompleteQuestChain_Click(object sender, RoutedEventArgs e) => await RunQuestActionAsync("complete-chain", "完成整条任务链");
    private async void UnclearQuest_Click(object sender, RoutedEventArgs e) => await RunQuestActionAsync("unclear", "取消完成标记");
    private async Task RunQuestActionAsync(string action, string label)
    {
        if (!RequireCharacter() || QuestGrid.SelectedItem is not QuestRow quest) { MessageBox.Show(this, "请先选择任务。", "86JP GM"); return; }
        if (!Confirm($"确定为 {_currentCharacter!.Name} {label}「{quest.Name}」？")) return;
        await PostCharacterActionAsync($"quests/{quest.QuestId}/{action}", null, label + "成功"); await SearchQuestsAsync();
    }

    private async void AccountCurrency_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAccount == null || !NonZeroInt(AccountCurrencyValue.Text, out var amount)) { ShowValueError(); return; }
        var type = (AccountCurrencyType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "cera";
        await PostAccountActionAsync("currency", new { type, amount }, "账号货币调整成功");
    }
    private async void SetHonorLevel_Click(object sender, RoutedEventArgs e) { if (!RangedInt(HonorLevelValue.Text, 0, 99, out var level)) { ShowValueError(); return; } await PostAccountActionAsync("honor-level", new { level }, "荣誉等级设置成功"); }
    private async void AccountQuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        if (action == "cargo/clear" && !Confirm("确定清空当前账号金库？此操作不可撤销。")) return;
        await PostAccountActionAsync(action, null, "账号操作成功");
    }

    private async Task PostCharacterActionAsync(string action, object? body, string success, bool refreshAccount = false)
    {
        if (!RequireCharacter()) return;
        try
        {
            SetBusy("正在提交操作…"); using var document = await _api.PostAsync($"/api/characters/{_currentCharacter!.CharacterId}/{action}", body); EnsureSuccess(document.RootElement);
            StatusText.Text = success; if (refreshAccount) await LoadCharacterDetailAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task PostAccountActionAsync(string action, object? body, string success)
    {
        if (_currentAccount == null) return;
        try
        {
            SetBusy("正在提交账号操作…"); using var document = await _api.PostAsync($"/api/accounts/{_currentAccount.AccountId}/{action}", body); EnsureSuccess(document.RootElement);
            StatusText.Text = success; await LoadCharacterDetailAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void LoadLogs_Click(object sender, RoutedEventArgs e) => await LoadLogsAsync();
    private async Task LoadLogsAsync()
    {
        if (_role < 3) return;
        try
        {
            SetBusy("正在筛选日志…");
            var category = Uri.EscapeDataString((LogCategory.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");
            var account = Uri.EscapeDataString(LogAccount.Text.Trim()); var character = Uri.EscapeDataString(LogCharacter.Text.Trim()); var query = Uri.EscapeDataString(LogQuery.Text.Trim());
            using var document = await _api.GetAsync($"/api/admin/logs?category={category}&account={account}&character={character}&q={query}&limit=300"); EnsureSuccess(document.RootElement);
            _logs.Clear(); foreach (var item in document.RootElement.GetProperty("logs").EnumerateArray()) _logs.Add(new LogRow { Timestamp = ReadString(item, "timestamp"), Category = ReadString(item, "category"), Action = ReadString(item, "action"), Account = ReadString(item, "account"), Character = ReadString(item, "character"), Message = ReadString(item, "message") });
            StatusText.Text = $"已载入 {_logs.Count} 条日志";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void LoadPermissions_Click(object sender, RoutedEventArgs e) => await LoadPermissionsAsync();
    private async Task LoadPermissionsAsync()
    {
        if (_role < 3) return;
        try
        {
            var q = Uri.EscapeDataString(PermissionQuery.Text.Trim()); using var document = await _api.GetAsync($"/api/admin/permissions?q={q}&limit=200"); EnsureSuccess(document.RootElement);
            _permissions.Clear(); foreach (var item in document.RootElement.GetProperty("accounts").EnumerateArray()) _permissions.Add(new PermissionRow { AccountId = ReadInt(item, "accountId"), AccountName = ReadString(item, "accountName"), Role = ReadInt(item, "role") });
            StatusText.Text = $"已载入 {_permissions.Count} 个账号";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void SetPermission_Click(object sender, RoutedEventArgs e)
    {
        if (PermissionsGrid.SelectedItem is not PermissionRow account) { MessageBox.Show(this, "请先选择账号。", "86JP GM"); return; }
        if (!int.TryParse((PermissionRole.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var role)) return;
        if (!Confirm($"确定将账号 {account.AccountName} 的权限设置为 {RoleLabel(role)}？")) return;
        try { using var document = await _api.PostAsync($"/api/admin/permissions/{account.AccountId}", new { role }); EnsureSuccess(document.RootElement); await LoadPermissionsAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try { using var _ = await _api.PostAsync("/api/auth/logout"); } catch { }
        _api.SetToken(""); _currentAccount = null; _currentCharacter = null; _accounts.Clear(); _allCharacters.Clear(); _visibleCharacters.Clear();
        Shell.Visibility = Visibility.Collapsed; LoginPanel.Visibility = Visibility.Visible; LoginStatus.Text = "";
    }

    private bool RequireCharacter()
    {
        if (_currentCharacter != null) return true;
        MessageBox.Show(this, "请先从左侧选择角色。", "86JP GM", MessageBoxButton.OK, MessageBoxImage.Information); return false;
    }
    private bool Confirm(string text) => MessageBox.Show(this, text, "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private static bool PositiveInt(string text, out int value) => int.TryParse(text, out value) && value > 0;
    private static bool NonZeroInt(string text, out int value) => int.TryParse(text, out value) && value != 0;
    private static bool RangedInt(string text, int min, int max, out int value) => int.TryParse(text, out value) && value >= min && value <= max;
    private static string Compact(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string TranslateQuestStatus(string value) => value switch { "active" => "进行中", "cleared" => "已完成", "available" => "可接取", _ => value };
    private static void EnsureSuccess(JsonElement root) { if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) throw new InvalidOperationException(root.TryGetProperty("error", out var error) ? error.GetString() : "操作失败。"); }
    private static string ReadString(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
    private static int ReadInt(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static long ReadLong(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static bool ReadBool(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string RoleLabel(int role) => role switch { 3 => "3 级 · 管理员", 2 => "2 级 · 完整权限", _ => "1 级 · 基础权限" };
    private void SetBusy(string value) => StatusText.Text = value;
    private void ShowError(Exception ex) { StatusText.Text = "操作失败"; MessageBox.Show(this, Friendly(ex), "86JP GM", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void ShowValueError() => MessageBox.Show(this, "请输入有效的非零整数。", "86JP GM", MessageBoxButton.OK, MessageBoxImage.Information);
    private static string Friendly(Exception ex) => ex is HttpRequestException or TaskCanceledException ? "无法连接 GM 服务，请检查网络后重试。" : ex.GetBaseException().Message;
}
