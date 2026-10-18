using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AuroraPad.App.Localization;
using AuroraPad.Core.Games;

namespace AuroraPad.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<InstalledGame> LibraryGames { get; } = [];
    public ICollectionView LibraryView { get; private set; } = null!;

    [ObservableProperty] private InstalledGame? _selectedLibraryGame;
    [ObservableProperty] private string _libraryFilter = "";
    [ObservableProperty] private string _libraryStatus = "";
    [ObservableProperty] private bool _isScanning;

    private void InitLibrary()
    {
        LibraryView = CollectionViewSource.GetDefaultView(LibraryGames);
        LibraryView.Filter = o => o is InstalledGame g
            && (LibraryFilter.Length == 0 || g.Name.Contains(LibraryFilter, StringComparison.CurrentCultureIgnoreCase));
        LibraryStatus = L.T("lib.notScanned");
    }

    [RelayCommand]
    private async Task ScanLibrary()
    {
        if (IsScanning) return;
        IsScanning = true;
        LibraryStatus = L.T("lib.scanning");
        var games = await Task.Run(GameLibrary.Scan);
        LibraryGames.Clear();
        foreach (var g in games) LibraryGames.Add(g);
        LibraryStatus = L.F("lib.found", games.Count,
            games.Count(g => g.Store == GameStore.Steam), games.Count(g => g.Store == GameStore.Epic), games.Count(g => g.Store == GameStore.Gog));
        IsScanning = false;
    }

    [RelayCommand]
    private void CreateProfileForGame()
    {
        var g = SelectedLibraryGame;
        if (g == null) { LibraryStatus = L.T("lib.selectFirst"); return; }
        var existing = Profiles.FirstOrDefault(p => p.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
        var profile = existing ?? CreateProfile(g.Name);
        if (profile == null) return;
        Associate(g, profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void AssociateGame()
    {
        var g = SelectedLibraryGame;
        if (g == null) { LibraryStatus = L.T("lib.selectFirst"); return; }
        if (SelectedProfile != null) Associate(g, SelectedProfile);
    }

    private void Associate(InstalledGame g, Core.Profiles.Profile profile)
    {
        if (g.Executable == null)
        {
            LibraryStatus = L.F("lib.noExe", g.Name);
            return;
        }
        AddExecutableToProfile(profile, Path.GetFileName(g.Executable).ToLowerInvariant());
        LibraryStatus = L.F("lib.associated", g.Name, profile.Name);
    }
}
