using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Jp86.GmClient;

public sealed class AccountRow
{
    public int AccountId { get; set; }
    public string Name { get; set; } = "";
    public long Cera { get; set; }
    public long TokenCera { get; set; }
    public long LuckyStar { get; set; }
    public int CharacterCount { get; set; }
    public string Summary => $"{Name}  ·  #{AccountId}";
    public override string ToString() => Summary;
}

public sealed class CharacterRow
{
    public int CharacterId { get; set; }
    public int AccountId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string JobName { get; set; } = "";
    public string SearchText => $"{Name} {CharacterId} {JobName}";
    public override string ToString() => $"{Name}  ·  Lv.{Level}\n{JobName}  #{CharacterId}";
}

public sealed class ItemRow : INotifyPropertyChanged
{
    private ImageSource? _icon;
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public int Rarity { get; set; }
    public int MinLevel { get; set; }
    public string IconPath { get; set; } = "";
    public int IconIndex { get; set; } = -1;
    public ImageSource? Icon
    {
        get => _icon;
        set { if (!ReferenceEquals(_icon, value)) { _icon = value; OnPropertyChanged(); } }
    }
    public string Display => $"{Name}   #{ItemId}   Lv.{MinLevel}";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class InventoryRow
{
    public string Container { get; set; } = "";
    public string Category { get; set; } = "";
    public int ListType { get; set; }
    public int Slot { get; set; }
    public int TemplateId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Rarity { get; set; }
    public int Count { get; set; }
    public int Durability { get; set; }
    public bool Deletable { get; set; }
    public bool Configurable { get; set; }
    public string Location => $"{Container} / {Category} / {Slot}";
    public string SearchText => $"{Name} {TemplateId} {Container} {Category}";
}

public sealed class QuestRow
{
    public int QuestId { get; set; }
    public string Name { get; set; } = "";
    public string GradeLabel { get; set; } = "";
    public string RegionLabel { get; set; } = "";
    public int MinLevel { get; set; }
    public string Status { get; set; } = "";
}

public sealed class StatRow
{
    public string Label { get; set; } = "";
    public long Value { get; set; }
}

public sealed class PermissionRow
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public int Role { get; set; }
}

public sealed class LogRow
{
    public string Timestamp { get; set; } = "";
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string Account { get; set; } = "";
    public string Character { get; set; } = "";
    public string Message { get; set; } = "";
}
