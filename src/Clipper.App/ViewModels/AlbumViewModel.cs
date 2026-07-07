using Clipper.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipper.App;

public partial class AlbumViewModel : ObservableObject
{
    public Album Model { get; }
    public long Id => Model.Id;

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isSelected;

    public AlbumViewModel(Album model)
    {
        Model = model;
        _name = model.Name;
    }
}
