using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using Microsoft.Extensions.Localization;
using PanoramaUiKit.Shared.Api;
using PanoramaUiKit.Shared.Menus;

namespace CS2_SimpleAdmin.Menus;

/// <summary>
/// Describes a menu and renders it through the Panorama UI Kit.
/// Sub-menus are pushed on top of their parent, the kit draws the back button.
/// </summary>
public class MenuBuilder
{
    /// <summary>
    /// Bumped every time a menu is opened for a player, so an option can tell whether its action opened another one.
    /// </summary>
    private static readonly Dictionary<ulong, int> OpenCounters = [];

    private readonly string _title;
    private readonly CCSPlayerController? _player;
    private readonly IStringLocalizer? _localizer;
    private readonly List<MenuOption> _options = [];
    private bool _isSubMenu;

    /// <summary>
    /// Constructor for player-localized menu with translation key
    /// </summary>
    public MenuBuilder(string titleKey, CCSPlayerController player, IStringLocalizer? localizer = null)
    {
        _title = titleKey;
        _player = player;
        _localizer = localizer ?? CS2_SimpleAdmin._localizer;
    }

    /// <summary>
    /// Constructor for static title
    /// </summary>
    public MenuBuilder(string title)
    {
        _title = title;
        _player = null;
        _localizer = null;
    }

    /// <summary>
    /// Adds a menu option with an action.
    /// </summary>
    /// <param name="name">Display name or translation key</param>
    /// <param name="action">Action to perform when selected</param>
    /// <param name="disabled">Whether the option is disabled</param>
    /// <param name="permission">Required permission</param>
    /// <param name="isTranslationKey">If true, name is a translation key to be localized</param>
    /// <param name="keepOpen">If true, the menu stays open after the action</param>
    public MenuBuilder AddOption(string name, Action<CCSPlayerController> action, bool disabled = false, string? permission = null,
        bool isTranslationKey = false, bool keepOpen = false)
    {
        _options.Add(new MenuOption
        {
            Name = name,
            Action = action,
            Disabled = disabled,
            Permission = permission,
            IsTranslationKey = isTranslationKey,
            KeepOpen = keepOpen
        });
        return this;
    }

    /// <summary>
    /// Adds a menu option that opens a submenu.
    /// </summary>
    public MenuBuilder AddSubMenu(string name, Func<MenuBuilder> subMenuFactory, bool disabled = false, string? permission = null)
    {
        return AddSubMenu(name, _ => subMenuFactory(), disabled, permission);
    }

    /// <summary>
    /// Adds a menu option that opens a submenu (with player parameter in factory).
    /// </summary>
    public MenuBuilder AddSubMenu(string name, Func<CCSPlayerController, MenuBuilder> subMenuFactory, bool disabled = false, string? permission = null)
    {
        _options.Add(new MenuOption
        {
            Name = name,
            SubMenuFactory = subMenuFactory,
            Disabled = disabled,
            Permission = permission
        });
        return this;
    }

    /// <summary>
    /// Opens this menu on top of the current one, with a back button.
    /// </summary>
    public MenuBuilder WithBackButton()
    {
        _isSubMenu = true;
        return this;
    }

    /// <summary>
    /// Opens the menu for a player.
    /// </summary>
    /// <param name="player">The player to open the menu for.</param>
    public void OpenMenu(CCSPlayerController player)
    {
        if (!player.IsValid)
        {
            return;
        }

        IPanoramaUiKit? kit = UiKit.Instance;
        if (kit == null)
        {
            return;
        }

        MenuOptions menu = Build(player);
        BumpOpenCounter(player);

        if (_isSubMenu)
        {
            kit.Menus.Push(player, menu);
        }
        else
        {
            kit.Menus.Open(player, menu);
        }
    }

    /// <summary>
    /// Closes any SimpleAdmin menu open for the player.
    /// </summary>
    public static void CloseMenu(CCSPlayerController player)
    {
        UiKit.Instance?.Menus.Close(player, UiKit.MenuLayout);
    }

    /// <summary>
    /// Clears all menu options.
    /// </summary>
    /// <returns>This MenuBuilder instance for chaining.</returns>
    public MenuBuilder Clear()
    {
        _options.Clear();
        return this;
    }

    private MenuOptions Build(CCSPlayerController player)
    {
        List<MenuEntry> entries = [];
        SteamID steamId = new SteamID(player.SteamID);

        foreach (MenuOption option in _options)
        {
            if (!string.IsNullOrEmpty(option.Permission) && !AdminManager.PlayerHasPermissions(steamId, option.Permission))
            {
                continue;
            }

            string label = GetLocalizedOptionName(option);
            MenuEntry entry;

            if (option.SubMenuFactory != null)
            {
                Func<CCSPlayerController, MenuBuilder> subMenuFactory = option.SubMenuFactory;
                entry = MenuEntry.Open(label, menuPlayer =>
                {
                    BumpOpenCounter(menuPlayer);
                    return subMenuFactory(menuPlayer).Build(menuPlayer);
                });
            }
            else
            {
                entry = MenuEntry.Button(label, menuPlayer => RunOption(option, menuPlayer));
            }

            if (option.Disabled)
            {
                entry = entry with { Disabled = true };
            }

            entries.Add(entry);
        }

        return new MenuOptions
        {
            Layout = UiKit.MenuLayout,
            Title = GetLocalizedTitle(),
            Entries = entries
        };
    }

    /// <summary>
    /// Runs the option, then closes the menu unless the option keeps it open or its action opened another menu.
    /// </summary>
    private static void RunOption(MenuOption option, CCSPlayerController player)
    {
        int counterBefore = GetOpenCounter(player);

        option.Action?.Invoke(player);

        if (!option.KeepOpen && player.IsValid && GetOpenCounter(player) == counterBefore)
        {
            CloseMenu(player);
        }
    }

    private static int GetOpenCounter(CCSPlayerController player)
    {
        OpenCounters.TryGetValue(player.SteamID, out int counter);
        return counter;
    }

    private static void BumpOpenCounter(CCSPlayerController player)
    {
        OpenCounters[player.SteamID] = GetOpenCounter(player) + 1;
    }

    private string GetLocalizedTitle()
    {
        if (_player == null || _localizer == null)
        {
            return _title;
        }

        using (new WithTemporaryCulture(_player.GetLanguage()))
        {
            return _localizer[_title];
        }
    }

    private string GetLocalizedOptionName(MenuOption option)
    {
        if (!option.IsTranslationKey || _player == null || _localizer == null)
        {
            return option.Name;
        }

        using (new WithTemporaryCulture(_player.GetLanguage()))
        {
            return _localizer[option.Name];
        }
    }
}

/// <summary>
/// Represents an option within a menu.
/// </summary>
public class MenuOption
{
    public string Name { get; set; } = string.Empty;
    public Action<CCSPlayerController>? Action { get; set; }
    public Func<CCSPlayerController, MenuBuilder>? SubMenuFactory { get; set; }
    public bool Disabled { get; set; }
    public string? Permission { get; set; }
    public bool IsTranslationKey { get; set; }
    public bool KeepOpen { get; set; }
}
