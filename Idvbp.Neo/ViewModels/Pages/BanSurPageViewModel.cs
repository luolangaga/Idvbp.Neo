using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Client;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Resources;
using Idvbp.Neo.Service;
using Idvbp.Neo.Services;
using ToolGood.Words.Pinyin;

namespace Idvbp.Neo.ViewModels.Pages;

public sealed class BanCharacterOption
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string? Abbrev { get; init; }
    public string? FullSpell { get; init; }
    public override string ToString() => DisplayName;
}

public partial class BanSlotItem : ObservableObject
{
    [ObservableProperty]
    private BanCharacterOption? _selectedCharacter;

    [ObservableProperty]
    private ObservableCollection<BanCharacterOption> _availableCharacters = [];

    [ObservableProperty]
    private string _selectedChara = string.Empty;

    [ObservableProperty]
    private string? _previewImage;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private bool _isFilled;

    [ObservableProperty]
    private string _filledText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BanCharacterOption> _filteredCharacters = [];

    [ObservableProperty]
    private BanCharacterOption? _pendingCharacter;

    public Func<BanSlotItem, Task>? SubmitSelectionAsync { get; set; }

    public Func<BanSlotItem, IReadOnlyList<BanCharacterOption>>? SearchCharacters { get; set; }

    public bool CanSubmit => IsEnabled && PendingCharacter is not null;

    partial void OnSearchTextChanged(string value)
    {
        RebuildFilteredCharacters();
    }

    partial void OnPendingCharacterChanged(BanCharacterOption? value)
    {
        OnPropertyChanged(nameof(CanSubmit));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSubmit));
    }

    partial void OnIsFilledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSubmit));
    }

    public async Task ConfirmPendingSelectionAsync()
    {
        RebuildFilteredCharacters();

        if (PendingCharacter is null && FilteredCharacters.Count > 0)
        {
            PendingCharacter = FilteredCharacters[0];
        }

        if (CanSubmit && SubmitSelectionAsync is not null)
        {
            await SubmitSelectionAsync(this);
        }
    }

    public void RebuildFilteredCharacters()
    {
        var matches = (SearchCharacters?.Invoke(this) ?? SearchCharactersInternal()).Take(30).ToArray();

        FilteredCharacters = new ObservableCollection<BanCharacterOption>(matches);
        PendingCharacter = matches.FirstOrDefault(x => string.Equals(x.Id, PendingCharacter?.Id, StringComparison.OrdinalIgnoreCase))
                           ?? matches.FirstOrDefault();
        OnPropertyChanged(nameof(CanSubmit));
    }

    private IReadOnlyList<BanCharacterOption> SearchCharactersInternal()
    {
        var query = NormalizeSearch(SearchText);
        return AvailableCharacters
            .Where(x => IsMatch(x, query))
            .OrderBy(x => GetMatchRank(x, query))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsMatch(BanCharacterOption character, string query)
        => string.IsNullOrWhiteSpace(query)
           || NormalizeSearch(character.DisplayName).Contains(query, StringComparison.Ordinal)
           || NormalizeSearch(character.Id).Contains(query, StringComparison.Ordinal)
           || NormalizeSearch(character.Abbrev).Contains(query, StringComparison.Ordinal)
           || NormalizeSearch(character.FullSpell).Contains(query, StringComparison.Ordinal);

    private static int GetMatchRank(BanCharacterOption character, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        if (NormalizeSearch(character.Abbrev) == query || NormalizeSearch(character.FullSpell) == query || NormalizeSearch(character.DisplayName) == query)
        {
            return 0;
        }

        if (NormalizeSearch(character.Abbrev).StartsWith(query, StringComparison.Ordinal))
        {
            return 1;
        }

        if (NormalizeSearch(character.FullSpell).StartsWith(query, StringComparison.Ordinal))
        {
            return 2;
        }

        return NormalizeSearch(character.DisplayName).StartsWith(query, StringComparison.Ordinal) ? 3 : 9;
    }

    private static string NormalizeSearch(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}

public partial class BanSurPageViewModel : ViewModelBase
{
    private readonly BpApiClient _apiClient;
    private readonly BpRoomWorkspace _workspace;
    private readonly AppNotificationService _notifications;
    private readonly PinyinMatch _pinyinMatch = new();
    private BanCharacterOption[] _characters = [];
    private bool _hasLoadedCatalog;

    public BanSurPageViewModel(BpApiClient apiClient, BpRoomWorkspace workspace, AppNotificationService notifications)
    {
        _apiClient = apiClient;
        _workspace = workspace;
        _notifications = notifications;
        _workspace.ActiveRoomChanged += ApplyRoom;
        _workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BpRoomWorkspace.IsBusy) or nameof(BpRoomWorkspace.IsSwitchingRoom) or nameof(BpRoomWorkspace.StatusMessage))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(StatusMessage));
            }
        };

        _ = InitializeAsync();
    }

    public bool IsBusy => _workspace.IsBusy || _workspace.IsSwitchingRoom;

    public string StatusMessage => _workspace.StatusMessage;

    public string CurrentRoomTitle => _workspace.CurrentRoomTitle;

    public bool HasSelectedRoom => _workspace.SelectedRoom is not null;

    [ObservableProperty]
    private ObservableCollection<BanSlotItem> _currentBanList = [];

    [ObservableProperty]
    private ObservableCollection<BanSlotItem> _globalBanList = [];

    [RelayCommand]
    private async Task SubmitCurrentBanAsync(BanSlotItem? slot)
        => await SubmitBanAsync(slot, isGlobal: false);

    [RelayCommand]
    private async Task SubmitGlobalBanAsync(BanSlotItem? slot)
        => await SubmitBanAsync(slot, isGlobal: true);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await EnsureCatalogAsync();
            ApplyRoom(_workspace.SelectedRoom);
        }
        catch (Exception ex)
        {
            _workspace.StatusMessage = $"加载求生者资源失败: {ex.Message}";
            _notifications.Error(ex, "加载求生者资源失败");
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await EnsureCatalogAsync();
            ApplyRoom(_workspace.SelectedRoom);
        }
        catch (Exception ex)
        {
            _workspace.StatusMessage = $"加载求生者资源失败: {ex.Message}";
            _notifications.Error(ex, "加载求生者资源失败");
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    private async Task EnsureCatalogAsync()
    {
        if (_hasLoadedCatalog)
        {
            return;
        }

        _characters = (await _apiClient.GetCharactersAsync())
            .Where(x => string.Equals(x.Role, "survivor", StringComparison.OrdinalIgnoreCase))
            .Select(ToOption)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _pinyinMatch.SetKeywords(_characters.Select(x => x.DisplayName).ToList());
        _hasLoadedCatalog = true;
    }

    private async Task SubmitBanAsync(BanSlotItem? slot, bool isGlobal)
    {
        if (slot?.PendingCharacter is null || !slot.IsEnabled)
        {
            return;
        }

        var room = await _workspace.AddBanAsync(new AddBanRequest
        {
            Role = CharacterRole.Survivor,
            CharacterId = slot.PendingCharacter.Id,
            Order = slot.Index
        }, isGlobal);

        if (room is not null)
        {
            ApplyRoom(room);
        }
    }

    private void ApplyRoom(BpRoom? room)
    {
        OnPropertyChanged(nameof(CurrentRoomTitle));
        OnPropertyChanged(nameof(HasSelectedRoom));

        if (room is null)
        {
            CurrentBanList = [];
            GlobalBanList = [];
            return;
        }

        CurrentBanList = BuildSlots(room.Bans.SurvivorBans, isGlobal: false);
        GlobalBanList = BuildSlots(room.GlobalBans.SurvivorBans, isGlobal: true);
    }

    private ObservableCollection<BanSlotItem> BuildSlots(ObservableCollection<PickBanEntry> entries, bool isGlobal)
    {
        var orderedEntries = entries.OrderBy(x => x.Order).ToArray();
        var count = orderedEntries.Length + 1;
        var slots = new ObservableCollection<BanSlotItem>();
        for (var index = 0; index < count; index++)
        {
            var entry = orderedEntries.ElementAtOrDefault(index);
            var selected = entry is null ? null : _characters.FirstOrDefault(x => string.Equals(x.Id, entry.CharacterId, StringComparison.OrdinalIgnoreCase));
            var slot = new BanSlotItem
            {
                Index = index + 1,
                AvailableCharacters = new ObservableCollection<BanCharacterOption>(_characters),
                SearchCharacters = SearchCharacters,
                SelectedCharacter = selected,
                PendingCharacter = selected,
                SelectedChara = selected?.DisplayName ?? string.Empty,
                PreviewImage = selected?.ImageUrl,
                SearchText = selected?.DisplayName ?? string.Empty,
                IsEnabled = true,
                IsFilled = entry is not null,
                FilledText = selected?.DisplayName ?? entry?.CharacterId ?? string.Empty,
                SubmitSelectionAsync = item => SubmitBanAsync(item, isGlobal)
            };
            slot.RebuildFilteredCharacters();
            slots.Add(slot);
        }

        return slots;
    }

    private BanCharacterOption ToOption(CharacterResourceItem resource)
    {
        var name = resource.Names.TryGetValue("zh-CN", out var chineseName) && !string.IsNullOrWhiteSpace(chineseName)
            ? chineseName!
            : resource.Names.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? resource.Id;
        var image = resource.Images.FirstOrDefault(x => string.Equals(x.Variant, "header", StringComparison.OrdinalIgnoreCase))
                    ?? resource.Images.FirstOrDefault(x => x.IsPrimary)
                    ?? resource.Images.FirstOrDefault();
        return new BanCharacterOption
        {
            Id = resource.Id,
            DisplayName = name,
            ImageUrl = _apiClient.ToAbsoluteUrl(image?.Url),
            Abbrev = resource.Abbrev,
            FullSpell = resource.FullSpell
        };
    }

    private IReadOnlyList<BanCharacterOption> SearchCharacters(BanSlotItem slot)
    {
        var query = slot.SearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return _characters.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var matchedNames = _pinyinMatch.Find(query);
        var matchedItems = _characters
            .Where(character => matchedNames.Contains(character.DisplayName, StringComparer.OrdinalIgnoreCase))
            .OrderBy(character => matchedNames.FindIndex(x => string.Equals(x, character.DisplayName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var character in _characters
                     .Where(character =>
                         character.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || character.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || (character.Abbrev?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                         || (character.FullSpell?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                     .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (matchedItems.Any(x => string.Equals(x.Id, character.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            matchedItems.Add(character);
        }

        return matchedItems;
    }
}
