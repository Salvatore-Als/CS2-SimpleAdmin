using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CS2_SimpleAdminApi;
using Microsoft.Extensions.Localization;

namespace CS2_SimpleAdmin.Menus;

public static class BasicMenu
{
    private const int MaxPlayerNameLength = 26;
    private const int MaxHistoryLabelLength = 40;

    /// <summary>
    /// Initializes all menus in the system by registering them with the MenuManager.
    /// Register with translation keys instead of static names - translation happens per-player.
    /// </summary>
    public static void Initialize()
    {
        MenuManager manager = MenuManager.Instance;

        // Players category menus
        manager.RegisterMenu("players", "slap", "sa_slap", CreateSlapMenu, "@css/slay");
        manager.RegisterMenu("players", "slay", "sa_slay", CreateSlayMenu, "@css/slay");
        manager.RegisterMenu("players", "kick", "sa_kick", CreateKickMenu, "@css/kick");
        manager.RegisterMenu("players", "warn", "sa_warn", admin => CreatePenaltyPlayersMenu(admin, PenaltyType.Warn), "@css/kick");
        manager.RegisterMenu("players", "ban", "sa_ban", admin => CreatePenaltyPlayersMenu(admin, PenaltyType.Ban), "@css/ban");
        manager.RegisterMenu("players", "gag", "sa_gag", admin => CreatePenaltyPlayersMenu(admin, PenaltyType.Gag), "@css/chat");
        manager.RegisterMenu("players", "mute", "sa_mute", admin => CreatePenaltyPlayersMenu(admin, PenaltyType.Mute), "@css/chat");
        manager.RegisterMenu("players", "silence", "sa_silence", admin => CreatePenaltyPlayersMenu(admin, PenaltyType.Silence), "@css/chat");
        manager.RegisterMenu("players", "team", "sa_team_force", CreateForceTeamMenu, "@css/kick");
        manager.RegisterMenu("players", "history", "sa_history", CreateHistoryMenu, "@css/kick");

        // Server category menus
        manager.RegisterMenu("server", "plugins", "sa_menu_pluginsmanager_title", CreatePluginsMenu, "@css/root");
        manager.RegisterMenu("server", "changemap", "sa_changemap", CreateChangeMapMenu, "@css/changemap");
        manager.RegisterMenu("server", "restart", "sa_restart_game", CreateRestartGameMenu, "@css/generic");
        manager.RegisterMenu("server", "custom", "sa_menu_custom_commands", CreateCustomCommandsMenu, "@css/generic");

        // Admin category menus
        manager.RegisterMenu("admin", "add", "sa_admin_add", CreateAddAdminMenu, "@css/root");
        manager.RegisterMenu("admin", "remove", "sa_admin_remove", CreateRemoveAdminMenu, "@css/root");
        manager.RegisterMenu("admin", "reload", "sa_admin_reload", CreateReloadAdminsMenu, "@css/root");
    }

    /// <summary>
    /// Duration then reason selection for a penalty on an online player, applied on the last click.
    /// </summary>
    /// <param name="admin">The admin applying the penalty.</param>
    /// <param name="target">The target player.</param>
    /// <param name="penaltyType">Ban, Warn, Gag, Mute or Silence.</param>
    /// <returns>A MenuBuilder instance for the duration menu.</returns>
    internal static MenuBuilder CreatePenaltyMenu(CCSPlayerController admin, CCSPlayerController target, PenaltyType penaltyType)
    {
        return CreateDurationMenu(admin, target.PlayerName, penaltyType,
            duration => CreateReasonMenu(admin, target.PlayerName, penaltyType,
                reason => ApplyPenalty(admin, target, penaltyType, duration, reason)));
    }

    /// <summary>
    /// Creates duration selection menu for time-based penalties (ban, mute, gag, etc.).
    /// </summary>
    /// <param name="admin">The admin player selecting duration.</param>
    /// <param name="targetName">The name of the target shown in the title.</param>
    /// <param name="penaltyType">The penalty being applied.</param>
    /// <param name="onSelect">Returns the next menu when a duration is selected.</param>
    /// <returns>A MenuBuilder instance for the duration menu.</returns>
    internal static MenuBuilder CreateDurationMenu(CCSPlayerController admin, string targetName, PenaltyType penaltyType,
        Func<int, MenuBuilder> onSelect)
    {
        MenuBuilder durationMenu = new MenuBuilder(GetPenaltyTitle(admin, penaltyType, "sa_duration", "Duration", targetName));

        foreach (DurationItem durationItem in CS2_SimpleAdmin.Instance.Config.MenuConfigs.Durations)
        {
            int duration = durationItem.Duration;
            durationMenu.AddSubMenu(durationItem.Name, () => onSelect(duration));
        }

        return durationMenu.WithBackButton();
    }

    /// <summary>
    /// Creates reason selection menu for penalties with predefined reasons from configuration.
    /// </summary>
    /// <param name="admin">The admin player selecting reason.</param>
    /// <param name="targetName">The name of the target shown in the title.</param>
    /// <param name="penaltyType">The type of penalty to determine which reason list to use.</param>
    /// <param name="onSelect">Executed when a reason is selected.</param>
    /// <returns>A MenuBuilder instance for the reason menu.</returns>
    internal static MenuBuilder CreateReasonMenu(CCSPlayerController admin, string targetName, PenaltyType penaltyType,
        Action<string> onSelect)
    {
        MenuBuilder reasonMenu = new MenuBuilder(GetPenaltyTitle(admin, penaltyType, "sa_reason", "Reason", targetName));

        foreach (string reason in GetReasons(penaltyType))
        {
            reasonMenu.AddOption(reason, _ => onSelect(reason));
        }

        return reasonMenu.WithBackButton();
    }

    private static void ApplyPenalty(CCSPlayerController admin, CCSPlayerController target, PenaltyType penaltyType, int duration, string reason)
    {
        if (!target.IsValid)
        {
            return;
        }

        switch (penaltyType)
        {
            case PenaltyType.Ban:
                CS2_SimpleAdmin.Instance.Ban(admin, target, duration, reason, admin.PlayerName);
                break;
            case PenaltyType.Warn:
                CS2_SimpleAdmin.Instance.Warn(admin, target, duration, reason, admin.PlayerName);
                break;
            case PenaltyType.Gag:
                CS2_SimpleAdmin.Instance.Gag(admin, target, duration, reason);
                break;
            case PenaltyType.Mute:
                CS2_SimpleAdmin.Instance.Mute(admin, target, duration, reason);
                break;
            case PenaltyType.Silence:
                CS2_SimpleAdmin.Instance.Silence(admin, target, duration, reason);
                break;
        }
    }

    private static List<string> GetReasons(PenaltyType penaltyType)
    {
        MenuConfig menuConfig = CS2_SimpleAdmin.Instance.Config.MenuConfigs;

        return penaltyType switch
        {
            PenaltyType.Kick => menuConfig.KickReasons,
            PenaltyType.Warn => menuConfig.WarnReasons,
            PenaltyType.Mute or PenaltyType.Gag or PenaltyType.Silence => menuConfig.MuteReasons,
            _ => menuConfig.BanReasons
        };
    }

    private static string GetPenaltyKey(PenaltyType penaltyType)
    {
        return penaltyType switch
        {
            PenaltyType.Kick => "sa_kick",
            PenaltyType.Warn => "sa_warn",
            PenaltyType.Gag => "sa_gag",
            PenaltyType.Mute => "sa_mute",
            PenaltyType.Silence => "sa_silence",
            _ => "sa_ban"
        };
    }

    /// <summary>
    /// "Ban Duration: name", in the admin's language.
    /// </summary>
    private static string GetPenaltyTitle(CCSPlayerController admin, PenaltyType penaltyType, string stepKey, string stepFallback, string targetName)
    {
        IStringLocalizer? localizer = CS2_SimpleAdmin._localizer;
        string penaltyKey = GetPenaltyKey(penaltyType);

        using (new WithTemporaryCulture(admin.GetLanguage()))
        {
            string action = localizer?[penaltyKey] ?? penaltyType.ToString();
            string step = localizer?[stepKey] ?? stepFallback;
            return $"{action} {step}: {targetName}";
        }
    }

    private static string GetLocalized(CCSPlayerController admin, string key, string fallback)
    {
        using (new WithTemporaryCulture(admin.GetLanguage()))
        {
            return CS2_SimpleAdmin._localizer?[key] ?? fallback;
        }
    }

    /// <summary>
    /// One entry per targetable player, each opening the penalty flow for that player.
    /// </summary>
    private static MenuBuilder CreatePenaltyPlayersMenu(CCSPlayerController admin, PenaltyType penaltyType)
    {
        MenuBuilder playersMenu = new MenuBuilder(GetPenaltyKey(penaltyType), admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(p => !p.IsBot && admin.CanTarget(p)))
        {
            CCSPlayerController target = player;
            playersMenu.AddSubMenu(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), () => CreatePenaltyMenu(admin, target, penaltyType));
        }

        return playersMenu.WithBackButton();
    }

    private static MenuBuilder CreateSlapMenu(CCSPlayerController admin)
    {
        MenuBuilder slapMenu = new MenuBuilder("sa_slap", admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(admin.CanTarget))
        {
            CCSPlayerController target = player;
            slapMenu.AddSubMenu(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), () => CreateSlapDamageMenu(admin, target));
        }

        return slapMenu.WithBackButton();
    }

    private static MenuBuilder CreateSlapDamageMenu(CCSPlayerController admin, CCSPlayerController target)
    {
        MenuBuilder slapDamageMenu = new MenuBuilder($"{GetLocalized(admin, "sa_slap", "Slap")}: {target.PlayerName}");
        int[] damages = [0, 1, 5, 10, 50, 100];

        foreach (int damage in damages)
        {
            slapDamageMenu.AddOption($"{damage} HP", currentAdmin =>
            {
                if (target.IsValid)
                {
                    CS2_SimpleAdmin.Slap(currentAdmin, target, damage);
                }
            }, keepOpen: true);
        }

        return slapDamageMenu.WithBackButton();
    }

    private static MenuBuilder CreateSlayMenu(CCSPlayerController admin)
    {
        MenuBuilder slayMenu = new MenuBuilder("sa_slay", admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(admin.CanTarget))
        {
            CCSPlayerController target = player;
            slayMenu.AddOption(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), _ =>
            {
                if (target.IsValid)
                {
                    CS2_SimpleAdmin.Slay(admin, target);
                }
            });
        }

        return slayMenu.WithBackButton();
    }

    /// <summary>
    /// Pick an online player, then one entry per past penalty.
    /// Selecting an entry prints its full details to the admin's console.
    /// </summary>
    private static MenuBuilder CreateHistoryMenu(CCSPlayerController admin)
    {
        MenuBuilder historyMenu = new MenuBuilder("sa_history", admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(p => !p.IsBot && admin.CanTarget(p)))
        {
            CCSPlayerController target = player;
            historyMenu.AddOption(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), _ => OpenPlayerHistory(admin, target), keepOpen: true);
        }

        return historyMenu.WithBackButton();
    }

    private static void OpenPlayerHistory(CCSPlayerController admin, CCSPlayerController target)
    {
        ulong steamId = target.SteamID;
        string name = target.PlayerName;
        ulong adminSteamId = admin.SteamID;

        // The list is built after the DB round trip, so it is opened from the world update
        Task.Run(async () =>
        {
            List<dynamic> rows = await CS2_SimpleAdmin.PlayerManager.GetPenaltyHistory(steamId);
            await Server.NextWorldUpdateAsync(() =>
            {
                // The slot may have been reused by another player while the query ran
                if (!admin.IsValid || admin.SteamID != adminSteamId)
                {
                    return;
                }

                if (rows.Count == 0)
                {
                    CS2_SimpleAdmin.Instance.PrintHistory(admin.PrintToConsole, name, steamId, rows);
                    return;
                }

                MenuBuilder historyList = new MenuBuilder("sa_history", admin);
                foreach (dynamic row in rows)
                {
                    dynamic capturedRow = row;
                    string label = Helper.Truncate($"{GetStatusMark((string)row.status)} {(string)row.type} {(string)row.reason}", MaxHistoryLabelLength);
                    historyList.AddOption(label, _ => CS2_SimpleAdmin.Instance.PrintHistory(admin.PrintToConsole, name, steamId, [capturedRow]), keepOpen: true);
                }

                historyList.WithBackButton().OpenMenu(admin);
            });
        });
    }

    /// <summary>
    /// ✖ for an active penalty, ✔ for one that is over.
    /// </summary>
    internal static string GetStatusMark(string status)
    {
        if (status == "ACTIVE")
        {
            return "✖";
        }

        return "✔";
    }

    private static MenuBuilder CreateKickMenu(CCSPlayerController admin)
    {
        MenuBuilder kickMenu = new MenuBuilder("sa_kick", admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(p => !p.IsBot && admin.CanTarget(p)))
        {
            CCSPlayerController target = player;
            kickMenu.AddSubMenu(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), () => CreateReasonMenu(admin, target.PlayerName, PenaltyType.Kick,
                reason =>
                {
                    if (target.IsValid)
                    {
                        CS2_SimpleAdmin.Instance.Kick(admin, target, reason, admin.PlayerName);
                    }
                }));
        }

        return kickMenu.WithBackButton();
    }

    private static MenuBuilder CreateForceTeamMenu(CCSPlayerController admin)
    {
        MenuBuilder teamMenu = new MenuBuilder("sa_team_force", admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(admin.CanTarget))
        {
            CCSPlayerController target = player;
            teamMenu.AddSubMenu(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), () => CreateTeamSelectionMenu(admin, target));
        }

        return teamMenu.WithBackButton();
    }

    private static MenuBuilder CreateTeamSelectionMenu(CCSPlayerController admin, CCSPlayerController target)
    {
        MenuBuilder teamSelectionMenu = new MenuBuilder($"{GetLocalized(admin, "sa_team_force", "Force Team")}: {target.PlayerName}");

        (string Name, string TeamName, CsTeam TeamNum)[] teams =
        [
            (GetLocalized(admin, "sa_team_ct", "CT"), "ct", CsTeam.CounterTerrorist),
            (GetLocalized(admin, "sa_team_t", "T"), "t", CsTeam.Terrorist),
            (GetLocalized(admin, "sa_team_swap", "Swap"), "swap", CsTeam.Spectator),
            (GetLocalized(admin, "sa_team_spec", "Spec"), "spec", CsTeam.Spectator)
        ];

        foreach ((string name, string teamName, CsTeam teamNum) in teams)
        {
            teamSelectionMenu.AddOption(name, _ =>
            {
                if (target.IsValid)
                {
                    CS2_SimpleAdmin.ChangeTeam(admin, target, teamName, teamNum, true);
                }
            });
        }

        return teamSelectionMenu.WithBackButton();
    }

    private static MenuBuilder CreatePluginsMenu(CCSPlayerController admin)
    {
        MenuBuilder pluginsMenu = new MenuBuilder("sa_menu_pluginsmanager_title", admin);

        pluginsMenu.AddOption("Open Plugins Manager", _ =>
        {
            admin.ExecuteClientCommandFromServer("css_pluginsmanager");
        });

        return pluginsMenu.WithBackButton();
    }

    private static MenuBuilder CreateChangeMapMenu(CCSPlayerController admin)
    {
        MenuBuilder mapMenu = new MenuBuilder("sa_changemap", admin);

        foreach (string map in CS2_SimpleAdmin.Instance.Config.DefaultMaps)
        {
            mapMenu.AddOption(map, _ =>
            {
                CS2_SimpleAdmin.Instance.ChangeMap(admin, map);
            });
        }

        foreach (KeyValuePair<string, long?> wsMap in CS2_SimpleAdmin.Instance.Config.WorkshopMaps)
        {
            string mapId = wsMap.Value?.ToString() ?? wsMap.Key;
            mapMenu.AddOption($"{wsMap.Key} (WS)", _ =>
            {
                CS2_SimpleAdmin.Instance.ChangeWorkshopMap(admin, mapId);
            });
        }

        return mapMenu.WithBackButton();
    }

    private static MenuBuilder CreateRestartGameMenu(CCSPlayerController admin)
    {
        MenuBuilder restartMenu = new MenuBuilder("sa_restart_game", admin);

        restartMenu.AddOption("Restart Round", _ =>
        {
            CS2_SimpleAdmin.RestartGame(admin);
        });

        return restartMenu.WithBackButton();
    }

    private static MenuBuilder CreateCustomCommandsMenu(CCSPlayerController admin)
    {
        MenuBuilder customMenu = new MenuBuilder("sa_menu_custom_commands", admin);
        SteamID steamId = new SteamID(admin.SteamID);

        foreach (CustomServerCommandData customCommand in CS2_SimpleAdmin.Instance.Config.CustomServerCommands)
        {
            if (string.IsNullOrEmpty(customCommand.DisplayName) || string.IsNullOrEmpty(customCommand.Command))
            {
                continue;
            }

            if (!AdminManager.PlayerHasPermissions(steamId, customCommand.Flag))
            {
                continue;
            }

            customMenu.AddOption(customCommand.DisplayName, _ =>
            {
                Helper.TryLogCommandOnDiscord(admin, customCommand.Command);

                if (customCommand.ExecuteOnClient)
                {
                    admin.ExecuteClientCommandFromServer(customCommand.Command);
                }
                else
                {
                    Server.ExecuteCommand(customCommand.Command);
                }
            });
        }

        return customMenu.WithBackButton();
    }

    private static MenuBuilder CreateAddAdminMenu(CCSPlayerController admin)
    {
        MenuBuilder addAdminMenu = new MenuBuilder("sa_admin_add", admin);

        foreach (CCSPlayerController player in Helper.GetValidPlayers().Where(p => !p.IsBot && admin.CanTarget(p)))
        {
            CCSPlayerController target = player;
            addAdminMenu.AddSubMenu(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), () => CreateAdminFlagsMenu(admin, target));
        }

        return addAdminMenu.WithBackButton();
    }

    private static MenuBuilder CreateAdminFlagsMenu(CCSPlayerController admin, CCSPlayerController target)
    {
        MenuBuilder flagsMenu = new MenuBuilder($"{GetLocalized(admin, "sa_admin_add", "Add Admin")}: {target.PlayerName}");

        foreach (AdminFlag adminFlag in CS2_SimpleAdmin.Instance.Config.MenuConfigs.AdminFlags)
        {
            // Disabled if player already has this flag
            bool hasFlag = AdminManager.PlayerHasPermissions(target, adminFlag.Flag);
            flagsMenu.AddOption(adminFlag.Name, _ =>
            {
                if (target.IsValid)
                {
                    CS2_SimpleAdmin.AddAdmin(admin, target.SteamID.ToString(), target.PlayerName, adminFlag.Flag, 10);
                }
            }, hasFlag);
        }

        return flagsMenu.WithBackButton();
    }

    private static MenuBuilder CreateRemoveAdminMenu(CCSPlayerController admin)
    {
        MenuBuilder removeAdminMenu = new MenuBuilder("sa_admin_remove", admin);

        IEnumerable<CCSPlayerController> adminPlayers = Helper.GetValidPlayers().Where(p =>
            AdminManager.GetPlayerAdminData(p)?.Flags.Count > 0 &&
            p != admin &&
            admin.CanTarget(p));

        foreach (CCSPlayerController player in adminPlayers)
        {
            CCSPlayerController target = player;
            removeAdminMenu.AddOption(Helper.Truncate(target.PlayerName, MaxPlayerNameLength), _ =>
            {
                if (target.IsValid)
                {
                    CS2_SimpleAdmin.Instance.RemoveAdmin(admin, target.SteamID.ToString());
                }
            });
        }

        return removeAdminMenu.WithBackButton();
    }

    private static MenuBuilder CreateReloadAdminsMenu(CCSPlayerController admin)
    {
        MenuBuilder reloadMenu = new MenuBuilder("sa_admin_reload", admin);

        reloadMenu.AddOption("Reload Admins", _ =>
        {
            CS2_SimpleAdmin.Instance.ReloadAdmins(admin);
        });

        return reloadMenu.WithBackButton();
    }
}
