using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipper.App;

/// <summary>A game filter chip in the library (e.g. "Roblox · 9"), derived from clips' Game field.</summary>
public partial class GameChipViewModel : ObservableObject
{
    public string Name { get; }
    public int Count { get; }

    [ObservableProperty] private bool _isSelected;

    public GameChipViewModel(string name, int count)
    {
        Name = name;
        Count = count;
    }
}
