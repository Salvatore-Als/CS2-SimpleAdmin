using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using CS2_SimpleAdmin.Managers;
using CS2_SimpleAdmin.Menus;
using CS2_SimpleAdmin.Models;
using CS2_SimpleAdminApi;
using Microsoft.Extensions.Logging;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    /// <summary>
    /// Handles the command that shows active penalties and warns for the caller or specified player.
    /// Queries warnings and mute status, formats them locally, and sends the result to caller's chat.
    /// </summary>
    /// <param name="caller">The player issuing this command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(usage: "[#userid or name]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPenaltiesCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !caller.IsValid || !caller.UserId.HasValue || DatabaseProvider == null)
        {
            return;
        }

        ulong steamId = caller.SteamID;

        if (!string.IsNullOrEmpty(command.GetArg(1)) && AdminManager.PlayerHasPermissions(new SteamID(caller.SteamID), "@css/kick"))
        {
            TargetResult? targets = GetTarget(command);

            if (targets == null)
            {
                return;
            }

            List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();
            if (playersToTarget.Count != 1)
            {
                command.ReplyToCommand("Only one target is allowed.");
                return;
            }

            CCSPlayerController target = playersToTarget[0];
            if (!target.UserId.HasValue || !caller.CanTarget(target))
            {
                return;
            }

            // Only the user id was reassigned before, so every lookup below used the caller's own penalties
            steamId = target.SteamID;
        }

        // PlayersInfo is filled by the async connect load; a target who joined a moment ago may not be in it yet
        if (!PlayersInfo.ContainsKey(steamId))
        {
            command.ReplyToCommand("Player data is still loading, try again in a moment.");
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                List<dynamic> warns = await WarnManager.GetPlayerWarns(PlayersInfo[steamId], false);

                // Check if the player is muted
                List<dynamic> activeMutes = await MuteManager.IsPlayerMuted(PlayersInfo[steamId].SteamId.SteamId64.ToString());

                Dictionary<PenaltyType, List<string>> mutesList = new()
                {
                    { PenaltyType.Gag, [] },
                    { PenaltyType.Mute, [] },
                    { PenaltyType.Silence, [] }
                };

                List<string> warnsList = [];

                bool found = false;
                foreach (dynamic? warn in warns.TakeWhile(warn => (string)warn.status == "ACTIVE"))
                {
                    DateTime ends = warn.ends;
                    if (_localizer == null)
                    {
                        continue;
                    }

                    using (new WithTemporaryCulture(caller.GetLanguage()))
                    {
                        warnsList.Add(_localizer["sa_player_penalty_info_active_warn", ends.ToLocalTime().ToString(CultureInfo.CurrentCulture), (string)warn.reason]);
                    }

                    found = true;
                }

                if (!found)
                {
                    if (_localizer != null)
                    {
                        warnsList.Add(_localizer["sa_player_penalty_info_no_active_warn"]);
                    }
                }

                if (activeMutes.Count > 0)
                {
                    foreach (dynamic mute in activeMutes)
                    {
                        string muteType = mute.type;
                        DateTime ends = mute.ends;
                        using (new WithTemporaryCulture(caller.GetLanguage()))
                        {
                            switch (muteType)
                            {
                                // Apply mute penalty based on mute type
                                case "GAG":
                                    if (_localizer != null)
                                    {
                                        mutesList[PenaltyType.Gag].Add(_localizer["sa_player_penalty_info_active_gag", ends.ToLocalTime().ToString(CultureInfo.CurrentCulture)]);
                                    }

                                    break;
                                case "MUTE":
                                    if (_localizer != null)
                                    {
                                        mutesList[PenaltyType.Mute].Add(_localizer["sa_player_penalty_info_active_mute", ends.ToLocalTime().ToString(CultureInfo.CurrentCulture)]);
                                    }

                                    break;
                                default:
                                    if (_localizer != null)
                                    {
                                        mutesList[PenaltyType.Silence].Add(_localizer["sa_player_penalty_info_active_silence", ends.ToLocalTime().ToString(CultureInfo.CurrentCulture)]);
                                    }

                                    break;
                            }
                        }
                    }
                }

                if (_localizer != null)
                {
                    if (mutesList[PenaltyType.Gag].Count == 0)
                    {
                        mutesList[PenaltyType.Gag].Add(_localizer["sa_player_penalty_info_no_active_gag"]);
                    }

                    if (mutesList[PenaltyType.Mute].Count == 0)
                    {
                        mutesList[PenaltyType.Mute].Add(_localizer["sa_player_penalty_info_no_active_mute"]);
                    }

                    if (mutesList[PenaltyType.Silence].Count == 0)
                    {
                        mutesList[PenaltyType.Silence].Add(_localizer["sa_player_penalty_info_no_active_silence"]);
                    }
                }

                await Server.NextWorldUpdateAsync(() =>
                {
                    caller.SendLocalizedMessage(_localizer, "sa_player_penalty_info",
                    [
                        PlayersInfo[steamId].Name,
                        PlayersInfo[steamId].TotalBans,
                        PlayersInfo[steamId].TotalGags,
                        PlayersInfo[steamId].TotalMutes,
                        PlayersInfo[steamId].TotalSilences,
                        PlayersInfo[steamId].TotalWarns,
                        string.Join("\n", mutesList.SelectMany(kvp => kvp.Value)),
                        string.Join("\n", warnsList)
                    ]);
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error processing player information: {ex}");
            }
        });
    }

    /// <summary>
    /// Toggles the admin voice listening mode or mutes/unmutes all players' voice.
    /// Sends confirmation messages accordingly.
    /// </summary>
    /// <param name="caller">The player issuing this command.</param>
    /// <param name="command">Command input parameters.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAdminVoiceCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !caller.IsValid)
        {
            return;
        }

        if (command.ArgCount > 1)
        {
            if (command.GetArg(2).ToLower().Equals("muteAll"))
            {
                caller.SendLocalizedMessage(_localizer, "sa_admin_voice_mute_all");
                foreach (CCSPlayerController? player in Helper.GetValidPlayers().Where(p => p != caller && !AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat")))
                {
                    player.VoiceFlags = VoiceFlags.Muted;
                }
            }

            if (command.GetArg(2).ToLower().Equals("unmuteAll"))
            {
                caller.SendLocalizedMessage(_localizer, "sa_admin_voice_unmute_all");
                foreach (CCSPlayerController? player in Helper.GetValidPlayers().Where(p => p != caller))
                {
                    if (PlayerPenaltyManager.GetPlayerPenalties(player.Slot, [PenaltyType.Silence, PenaltyType.Mute]).Count == 0)
                    {
                        player.VoiceFlags = VoiceFlags.Normal;
                    }
                }
            }

            return;
        }

        bool enabled = caller.VoiceFlags.HasFlag(VoiceFlags.ListenAll);
        string messageKey;

        if (enabled)
        {
            messageKey = "sa_admin_voice_unlisten_all";
        }
        else
        {
            messageKey = "sa_admin_voice_listen_all";
        }

        caller.SendLocalizedMessage(_localizer, messageKey);
        caller.VoiceFlags ^= VoiceFlags.ListenAll;
    }

    /// <summary>
    /// Opens the admin menu for the caller.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters.</param>
    [RequiresPermissions("@css/generic")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !caller.IsValid)
        {
            return;
        }

        MenuManager.Instance.OpenMainMenu(caller);
    }

    /// <summary>
    /// Displays admin help text read from a file.
    /// Outputs lines one at a time as replies to the command.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters.</param>
    [RequiresPermissions("@css/generic")]
    public void OnAdminHelpCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string[] lines = File.ReadAllLines(ModuleDirectory + "/admin_help.txt");

        foreach (string line in lines)
        {
            string text = " ";
            if (!string.IsNullOrWhiteSpace(line))
            {
                text = line.ReplaceColorTags();
            }

            command.ReplyToCommand(text);
        }
    }

    /// <summary>
    /// Handles adding a new admin with specified SteamID, name, flags, immunity, and duration.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(minArgs: 4, usage: "<steamid> <name> <flags/groups> <immunity> <duration>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnAddAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        if (!Helper.ValidateSteamId(command.GetArg(1), out SteamID? steamId) || steamId == null)
        {
            command.ReplyToCommand($"Invalid SteamID64.");
            return;
        }

        string steamid = steamId.SteamId64.ToString();

        if (command.GetArg(2).Length <= 0)
        {
            command.ReplyToCommand($"Invalid player name.");
            return;
        }
        if (!command.GetArg(3).Contains('@') && !command.GetArg(3).Contains('#'))
        {
            command.ReplyToCommand($"Invalid flag or group.");
            return;
        }

        string name = command.GetArg(2);
        string flags = command.GetArg(3);
        bool globalAdmin = command.GetArg(4).ToLower().Equals("-g") || command.GetArg(5).ToLower().Equals("-g") ||
                          command.GetArg(6).ToLower().Equals("-g");
        int.TryParse(command.GetArg(4), out int immunity);
        int time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(5)));

        AddAdmin(caller, steamid, name, flags, immunity, time, globalAdmin, command);
    }

    /// <summary>
    /// Adds admin permissions and groups for a player.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="steamid">SteamID as string identifying the player.</param>
    /// <param name="name">Player's name.</param>
    /// <param name="flags">Comma-separated admin flags/groups.</param>
    /// <param name="immunity">Admin immunity level.</param>
    /// <param name="time">Duration of permission (default 0 = permanent).</param>
    /// <param name="globalAdmin">Whether admin is global.</param>
    /// <param name="command">Optional command info for confirmation messages.</param>
    public static void AddAdmin(CCSPlayerController? caller, string steamid, string name, string flags, int immunity, int time = 0, bool globalAdmin = false, CommandInfo? command = null)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        List<string> flagsList = flags.Split(',').Select(flag => flag.Trim()).ToList();
        _ = Instance.PermissionManager.AddAdminBySteamId(steamid, name, flagsList, immunity, time, globalAdmin);

        Helper.LogCommand(caller, $"css_addadmin {steamid} {name} {flags} {immunity} {time}");

        string msg = $"Added '{flags}' flags to '{name}' ({steamid})";
        if (command != null)
        {
            command.ReplyToCommand(msg);
        }
        else if (caller != null && caller.IsValid)
        {
            caller.PrintToChat(msg);
        }
        else
        {
            Server.PrintToConsole(msg);
        }
    }

    /// <summary>
    /// Handles removing an admin's flags and groups by SteamID.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(minArgs: 1, usage: "<steamid>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnDelAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        if (!Helper.ValidateSteamId(command.GetArg(1), out SteamID? steamId) || steamId == null)
        {
            command.ReplyToCommand($"Invalid SteamID64.");
            return;
        }

        bool globalDelete = command.GetArg(2).ToLower().Equals("-g");

        RemoveAdmin(caller, steamId.SteamId64.ToString(), globalDelete, command);
    }

    /// <summary>
    /// Removes admin permissions and groups for a player.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="steamid">SteamID as string identifying the player.</param>
    /// <param name="globalDelete">Whether to delete globally.</param>
    /// <param name="command">Optional command info.</param>
    public void RemoveAdmin(CCSPlayerController? caller, string steamid, bool globalDelete = false, CommandInfo? command = null)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        _ = PermissionManager.DeleteAdminBySteamId(steamid, globalDelete);

        AddTimer(2, () =>
        {
            if (string.IsNullOrEmpty(steamid) || !SteamID.TryParse(steamid, out SteamID? steamId) ||
                steamId == null)
            {
                return;
            }

            if (PermissionManager.AdminCache.ContainsKey(steamId))
            {
                PermissionManager.AdminCache.TryRemove(steamId, out _);
            }

            AdminManager.ClearPlayerPermissions(steamId);
            AdminManager.RemovePlayerAdminData(steamId);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

        Helper.LogCommand(caller, $"css_deladmin {steamid}");

        string msg = $"Removed flags from '{steamid}'";
        if (command != null)
        {
            command.ReplyToCommand(msg);
        }
        else if (caller != null && caller.IsValid)
        {
            caller.PrintToChat(msg);
        }
        else
        {
            Server.PrintToConsole(msg);
        }
    }

    /// <summary>
    /// Adds a new admin group with specified flags and immunity settings.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(minArgs: 3, usage: "<group_name> <flags> <immunity>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnAddGroup(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        if (!command.GetArg(1).StartsWith("#"))
        {
            command.ReplyToCommand($"Group name must start with #.");
            return;
        }

        if (!command.GetArg(2).StartsWith($"@") && !command.GetArg(2).StartsWith($"#"))
        {
            command.ReplyToCommand($"Invalid flag or group.");
            return;
        }

        string groupName = command.GetArg(1);
        string flags = command.GetArg(2);
        int.TryParse(command.GetArg(3), out int immunity);
        bool globalGroup = command.GetArg(4).ToLower().Equals("-g");

        AddGroup(caller, groupName, flags, immunity, globalGroup, command);
    }

    /// <summary>
    /// Adds a new admin group with specified flags and immunity level.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="name">Group name (prefix with #).</param>
    /// <param name="flags">Comma-separated flags/groups string.</param>
    /// <param name="immunity">Immunity level.</param>
    /// <param name="globalGroup">Whether group is global.</param>
    /// <param name="command">Optional command info.</param>
    private static void AddGroup(CCSPlayerController? caller, string name, string flags, int immunity, bool globalGroup, CommandInfo? command = null)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        List<string> flagsList = flags.Split(',').Select(flag => flag.Trim()).ToList();
        _ = Instance.PermissionManager.AddGroup(name, flagsList, immunity, globalGroup);

        Helper.LogCommand(caller, $"css_addgroup {name} {flags} {immunity}");

        string msg = $"Created group '{name}' with flags '{flags}'";
        if (command != null)
        {
            command.ReplyToCommand(msg);
        }
        else if (caller != null && caller.IsValid)
        {
            caller.PrintToChat(msg);
        }
        else
        {
            Server.PrintToConsole(msg);
        }
    }

    /// <summary>
    /// Handles removing a group by name.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(minArgs: 1, usage: "<group_name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnDelGroupCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        if (!command.GetArg(1).StartsWith($"#"))
        {
            command.ReplyToCommand($"Group name must start with #.");
            return;
        }

        string groupName = command.GetArg(1);

        RemoveGroup(caller, groupName, command);
    }

    /// <summary>
    /// Removes a group.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="name">The group name to remove.</param>
    /// <param name="command">Optional command info for confirmation.</param>
    private void RemoveGroup(CCSPlayerController? caller, string name, CommandInfo? command = null)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        _ = PermissionManager.DeleteGroup(name);

        AddTimer(2, () =>
        {
            ReloadAdmins(caller);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

        Helper.LogCommand(caller, $"css_delgroup {name}");

        string msg = $"Removed group '{name}'";
        if (command != null)
        {
            command.ReplyToCommand(msg);
        }
        else if (caller != null && caller.IsValid)
        {
            caller.PrintToChat(msg);
        }
        else
        {
            Server.PrintToConsole(msg);
        }
    }

    /// <summary>
    /// Reloads admin and group data from database and json files.
    /// </summary>
    /// <param name="caller">The player issuing the reload command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnRelAdminCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        ReloadAdmins(caller);
        command.ReplyToCommand("Reloaded sql admins and groups");
    }

    /// <summary>
    /// Reloads bans cache.
    /// </summary>
    /// <param name="caller">The player issuing the reload command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnRelBans(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        _ = Instance.CacheManager?.ForceReInitializeCacheAsync();
        command.ReplyToCommand("Reloaded bans");
    }

    /// <summary>
    /// Reloads admin data asynchronously and updates admin caches.
    /// </summary>
    /// <param name="caller">The player issuing the reload command.</param>
    private static readonly SemaphoreSlim ReloadAdminsLock = new(1, 1);
    private static int _reloadGeneration;

    public void ReloadAdmins(CCSPlayerController? caller)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        Task.Run(async () =>
        {
            // Serialize reloads end to end: overlapping ones (css_admins_reload, add admin, map change) race on the JSON files,
            // and CSS reads them on timers up to 4s later. Capped so a hibernating server cannot hold the lock forever.
            await ReloadAdminsLock.WaitAsync();
            // If the cap expires (hibernation) and a newer reload runs, this one's pending timers must not apply a stale snapshot
            int generation = Interlocked.Increment(ref _reloadGeneration);
            try
            {
                await PermissionManager.CreateGroupsJsonFile();
                List<(SteamID steamId, DateTime? ends, List<string> flags)> admins = await PermissionManager.CreateAdminsJsonFile();

                string adminsFile = await File.ReadAllTextAsync(Instance.ModuleDirectory + "/data/admins.json");
                string groupsFile = await File.ReadAllTextAsync(Instance.ModuleDirectory + "/data/groups.json");

                TaskCompletionSource loaded = new TaskCompletionSource();
                Task scheduled = Server.NextWorldUpdateAsync(() =>
                {
                    AddTimer(1, () =>
                    {
                        if (!string.IsNullOrEmpty(adminsFile))
                        {
                            AddTimer(2.0f, () =>
                            {
                                if (generation != _reloadGeneration)
                                {
                                    return;
                                }
                                // Strip old permissions right before loading the new ones, not seconds earlier
                                PermissionManager.ApplyAdminCache(admins);
                                AdminManager.LoadAdminData(ModuleDirectory + "/data/admins.json");
                            });
                        }

                        if (!string.IsNullOrEmpty(groupsFile))
                        {
                            AddTimer(3.0f, () =>
                            {
                                if (generation != _reloadGeneration)
                                {
                                    return;
                                }

                                AdminManager.LoadAdminGroups(ModuleDirectory + "/data/groups.json");
                            });
                        }

                        AddTimer(4.0f, () =>
                        {
                            if (generation == _reloadGeneration)
                            {
                                if (!string.IsNullOrEmpty(adminsFile))
                                {
                                    AdminManager.LoadAdminData(ModuleDirectory + "/data/admins.json");
                                }

                                _logger?.LogInformation("Loaded admins!");
                            }

                            // Always completes, so a superseded reload cannot hold the lock until the cap
                            loaded.TrySetResult();
                        });
                    });
                });

                await Task.WhenAny(Task.WhenAll(scheduled, loaded.Task), Task.Delay(TimeSpan.FromSeconds(15)));
            }
            catch (Exception ex)
            {
                // A failed DB read aborts here, leaving current permissions untouched
                _logger?.LogError("Unable to reload admins: {exception}", ex.Message);
            }
            finally
            {
                ReloadAdminsLock.Release();
            }
        });

        //_ = _adminManager.GiveAllGroupsFlags();
        //_ = _adminManager.GiveAllFlags();
    }

    /// <summary>
    /// Toggles player visibility on the server, hiding or revealing them.
    /// </summary>
    /// <param name="caller">The player issuing the hide command.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/kick")]
    public void OnHideCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null)
        {
            return;
        }

        Helper.LogCommand(caller, command);

        if (!SilentPlayers.Add(caller.Slot))
        {
            SilentPlayers.Remove(caller.Slot);
            caller.PrintToChat($"You aren't hidden now!");
            if (caller.TeamNum <= 1)
            {
                caller.ChangeTeam(CsTeam.Spectator);
            }

            SimpleAdminApi?.OnAdminToggleSilentEvent(caller.Slot, false);
        }
        else
        {
            Server.ExecuteCommand("sv_disable_teamselect_menu 1");

            if (caller.PlayerPawn?.Value?.LifeState == (int)LifeState_t.LIFE_ALIVE)
            {
                caller.PlayerPawn.Value?.CommitSuicide(true, false);
            }

            caller.PrintToChat($"You are hidden now!");
            if (caller.TeamNum > 1)
            {
                AddTimer(0.15f, () => { Server.NextWorldUpdate(() => caller.ChangeTeam(CsTeam.Spectator)); }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            }

            AddTimer(0.26f, () => { Server.NextWorldUpdate(() => caller.ChangeTeam(CsTeam.None)); }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(0.50f, () => { Server.NextWorldUpdate(() => Server.ExecuteCommand("sv_disable_teamselect_menu 0")); }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            SimpleAdminApi?.OnAdminToggleSilentEvent(caller.Slot, true);
        }
    }

    /// <summary>
    /// Toggles penalty notification visibility to admins.
    /// </summary>
    /// <param name="caller">The player toggling notification visibility.</param>
    /// <param name="command">Command input parameters.</param>
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/kick")]
    public void OnHideCommsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null)
        {
            return;
        }

        if (!AdminDisabledJoinComms.Add(caller.SteamID))
        {
            AdminDisabledJoinComms.Remove(caller.SteamID);
            command.ReplyToCommand("From now on, you'll see penalty notifications");
        }
        else
        {
            AdminDisabledJoinComms.Add(caller.SteamID);
            command.ReplyToCommand($"You don't see penalty notifications now");
        }
    }

    /// <summary>
    /// Displays detailed information about target players, including admin groups, permissions, and penalties.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">Command input parameters including targets.</param>
    [CommandHelper(minArgs: 1, usage: "<#userid or name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/generic")]
    public void OnWhoCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        TargetResult? targets = GetTarget(command);
        if (targets == null)
        {
            return;
        }

        Helper.LogCommand(caller, command);

        List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

        playersToTarget.ForEach(player =>
        {
            if (!player.UserId.HasValue)
            {
                return;
            }

            if (!caller!.CanTarget(player))
            {
                return;
            }

            PlayerInfo playerInfo = PlayersInfo[player.SteamID];

            Task.Run(async () =>
            {
                await Server.NextWorldUpdateAsync(() =>
                {
                    Action<string> printMethod;

                    if (caller == null)
                    {
                        printMethod = Server.PrintToConsole;
                    }
                    else
                    {
                        printMethod = caller.PrintToConsole;
                    }

                    AdminData? adminData = AdminManager.GetPlayerAdminData(new SteamID(player.SteamID));

                    printMethod($"--------- INFO ABOUT \"{playerInfo.Name}\" ---------");
                    printMethod($"• Clan: \"{player.Clan}\" Name: \"{playerInfo.Name}\"");
                    printMethod($"• UserID: \"{playerInfo.UserId}\"");
                    printMethod($"• SteamID64: \"{playerInfo.SteamId.SteamId64}\"");
                    if (adminData != null)
                    {
                        string flags = string.Join(",", adminData._flags);
                        string groups = string.Join(",", adminData.Groups);

                        printMethod($"• Groups/Flags: \"{groups}{flags}\"");
                    }
                    printMethod($"• SteamID2: \"{playerInfo.SteamId.SteamId2}\"");
                    printMethod($"• Community link: \"{playerInfo.SteamId.ToCommunityUrl()}\"");
                    if (playerInfo.IpAddress != null && AdminManager.PlayerHasPermissions(new SteamID(caller!.SteamID), "@css/showip"))
                    {
                        printMethod($"• IP Address: \"{playerInfo.IpAddress}\"");
                    }

                    printMethod($"• Ping: \"{player.Ping}\"");
                    printMethod($"• Total Bans: \"{playerInfo.TotalBans}\"");
                    printMethod($"• Total Gags: \"{playerInfo.TotalGags}\"");
                    printMethod($"• Total Mutes: \"{playerInfo.TotalMutes}\"");
                    printMethod($"• Total Silences: \"{playerInfo.TotalSilences}\"");
                    printMethod($"• Total Warns: \"{playerInfo.TotalWarns}\"");

                    List<IEnumerable<(ulong SteamId, string PlayerName)>> chunkedAccounts = playerInfo.AccountsAssociated.ChunkBy(3).ToList();
                    foreach (IEnumerable<(ulong SteamId, string PlayerName)> chunk in chunkedAccounts)
                    {
                        printMethod($"• Associated Accounts: \"{string.Join(", ", chunk.Select(a => $"{a.PlayerName} ({a.SteamId})"))}\"");
                    }

                    printMethod($"--------- END INFO ABOUT \"{player.PlayerName}\" ---------");
                });
            });
        });
    }

    /// <summary>
    /// Displays a menu with disconnected players, allowing the caller to apply penalties like ban, mute, gag, or silence.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">The command containing parameters.</param>
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/kick")]
    public void OnDisconnectedCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (_localizer == null || caller == null)
        {
            return;
        }

        MenuBuilder disconnectedMenu = new MenuBuilder("sa_menu_disconnected_title", caller);

        foreach (DisconnectedPlayer player in DisconnectedPlayers)
        {
            DisconnectedPlayer target = player;
            disconnectedMenu.AddSubMenu(target.Name, () => CreateDisconnectedActionMenu(caller, target));
        }

        disconnectedMenu.OpenMenu(caller);
    }

    /// <summary>
    /// Penalties that can be applied to a disconnected player: duration, then reason, then applied by SteamID.
    /// </summary>
    private static MenuBuilder CreateDisconnectedActionMenu(CCSPlayerController caller, DisconnectedPlayer target)
    {
        MenuBuilder actionMenu = new MenuBuilder("sa_menu_disconnected_action_title", caller);

        (PenaltyType Type, string Name)[] penalties =
        [
            (PenaltyType.Ban, "ban"),
            (PenaltyType.Mute, "mute"),
            (PenaltyType.Gag, "gag"),
            (PenaltyType.Silence, "silence")
        ];

        foreach ((PenaltyType penaltyType, string penaltyName) in penalties)
        {
            string label;
            using (new WithTemporaryCulture(caller.GetLanguage()))
            {
                label = _localizer?[$"sa_{penaltyName}"] ?? penaltyName;
            }

            actionMenu.AddSubMenu(label, () => BasicMenu.CreateDurationMenu(caller, target.Name, penaltyType,
                duration => BasicMenu.CreateReasonMenu(caller, target.Name, penaltyType,
                    reason => PenalizeDisconnectedPlayer(caller, target, penaltyName, duration, reason))));
        }

        return actionMenu.WithBackButton();
    }

    private static void PenalizeDisconnectedPlayer(CCSPlayerController caller, DisconnectedPlayer target, string penaltyName, int duration, string reason)
    {
        caller.ExecuteClientCommandFromServer($"css_add{penaltyName} {target.SteamId.SteamId64} {duration} \"{reason}\"");

        if (SilentPlayers.Contains(caller.Slot))
        {
            return;
        }

        string activityMessageKey;
        object[] adminActivityArgs;

        if (duration == 0)
        {
            activityMessageKey = $"sa_admin_{penaltyName}_message_perm";
            adminActivityArgs = ["CALLER", target.Name, reason];
        }
        else
        {
            activityMessageKey = $"sa_admin_{penaltyName}_message_time";
            adminActivityArgs = ["CALLER", target.Name, reason, duration];
        }

        Helper.ShowAdminActivity(activityMessageKey, caller.PlayerName, false, adminActivityArgs);
    }

    /// <summary>
    /// Displays the warning menu for a player specified by a command argument,
    /// showing active and past warns with options to remove them.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">The command containing target player identifier.</param>
    [CommandHelper(minArgs: 1, usage: "<#userid or name>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/kick")]
    public void OnWarnsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null || _localizer == null || caller == null)
        {
            return;
        }

        TargetResult? targets = GetTarget(command);
        if (targets == null)
        {
            return;
        }

        Helper.LogCommand(caller, command);

        List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsBot: false }).ToList();
        if (playersToTarget.Count > 1)
        {
            return;
        }

        playersToTarget.ForEach(player =>
        {
            if (!player.UserId.HasValue)
            {
                return;
            }

            if (!caller.CanTarget(player))
            {
                return;
            }

            int userId = player.UserId.Value;
            ulong steamId = player.SteamID;

            MenuBuilder warnsMenu = new MenuBuilder(_localizer["sa_admin_warns_menu_title", player.PlayerName]);

            Task.Run(async () =>
            {
                List<dynamic> warnsList = await WarnManager.GetPlayerWarns(PlayersInfo[steamId], false);
                List<dynamic> sortedWarns = warnsList
                    .OrderBy(warn => (string)warn.status != "ACTIVE")
                    .ThenByDescending(warn => (int)warn.id)
                    .ToList();

                foreach (dynamic warn in sortedWarns)
                {
                    int warnId = (int)warn.id;
                    string warnReason = (string)warn.reason;

                    warnsMenu.AddOption($"{BasicMenu.GetStatusMark((string)warn.status)} {warnReason}", menuPlayer =>
                    {
                        _ = WarnManager.UnwarnPlayer(PlayersInfo[steamId], warnId);
                        player.PrintToChat(_localizer["sa_admin_warns_unwarn", player.PlayerName, warnReason]);
                    });
                }

                await Server.NextWorldUpdateAsync(() =>
                {
                    warnsMenu.OpenMenu(caller);
                });
            });
        });
    }

    /// <summary>
    /// Prints a player's full penalty history (bans, gags, mutes, silences, warns) to the caller's console.
    /// Accepts a SteamID64 so it also works for players who are not online.
    /// </summary>
    /// <param name="caller">The player issuing the command or null for console.</param>
    /// <param name="command">The command containing the target and an optional penalty type filter.</param>
    [RequiresPermissions("@css/kick")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name or steamid64> [bans|gags|mutes|silences|warns]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnHistoryCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null || _localizer == null)
        {
            return;
        }

        string? filter;

        if (command.ArgCount > 2)
        {
            filter = command.GetArg(2).ToLowerInvariant();
        }
        else
        {
            filter = null;
        }
        if (filter is not (null or "bans" or "gags" or "mutes" or "silences" or "warns"))
        {
            command.ReplyToCommand("Usage: css_history <#userid or name or steamid64> [bans|gags|mutes|silences|warns]");
            return;
        }

        ulong steamId;
        string? name = null;

        if (Helper.ValidateSteamId(command.GetArg(1), out SteamID? parsedSteamId) && parsedSteamId != null)
        {
            if (caller != null && !caller.CanTarget(parsedSteamId))
            {
                return;
            }

            steamId = parsedSteamId.SteamId64;
            if (PlayersInfo.TryGetValue(steamId, out PlayerInfo? info))
            {
                name = info.Name;
            }
        }
        else
        {
            TargetResult? targets = GetTarget(command);
            if (targets == null)
            {
                return;
            }

            List<CCSPlayerController> players = targets.Players.Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false }).ToList();
            if (players.Count != 1)
            {
                command.ReplyToCommand("Only one target is allowed.");
                return;
            }

            CCSPlayerController player = players[0];
            if (caller != null && !caller.CanTarget(player))
            {
                return;
            }

            steamId = player.SteamID;
            name = player.PlayerName;
        }

        Helper.LogCommand(caller, command);
        Action<string> print;

        if (caller == null)
        {
            print = Server.PrintToConsole;
        }
        else
        {
            print = caller.PrintToConsole;
        }
        ulong? callerSteamId = caller?.SteamID;

        Task.Run(async () =>
        {
            List<dynamic> rows = await PlayerManager.GetPenaltyHistory(steamId, filter);
            // Offline players are only known by SteamID; borrow the name stored on their latest record
            if (name == null && rows.Count > 0)
            {
                name = (string?)rows[0].player_name;
            }

            await Server.NextWorldUpdateAsync(() =>
            {
                // The slot may have been reused by another player while the query ran
                if (caller != null && (!caller.IsValid || caller.SteamID != callerSteamId))
                {
                    return;
                }

                PrintHistory(print, name ?? steamId.ToString(), steamId, rows);
            });
        });
    }

    /// <summary>
    /// Writes history rows from <see cref="PlayerManager.GetPenaltyHistory"/> as one console line each.
    /// </summary>
    internal void PrintHistory(Action<string> print, string name, ulong steamId, List<dynamic> rows)
    {
        if (_localizer == null)
        {
            return;
        }

        if (rows.Count == 0)
        {
            print(_localizer["sa_history_none", name]);
            return;
        }

        print(_localizer["sa_history_header", name, steamId, rows.Count]);
        foreach (dynamic r in rows)
        {
            string duration;

            if ((int)r.duration == 0)
            {
                duration = "perm";
            }
            else
            {
                duration = $"{(int)r.duration}m";
            }
            string line = $"[{Date(r.created)}] {(string)r.type,-7} {(string)r.status,-8} {duration,-6} by {(string)r.admin_name}: {(string)r.reason}";
            if (r.lift_date != null)
            {
                line += $" | lifted {Date(r.lift_date)} by {(string?)r.lift_admin ?? "?"}: {(string?)r.lift_reason}";
            }

            print(line);
        }

        static string Date(object? value) => PlayerManager.ToDateTime(value)?.ToString("yyyy-MM-dd HH:mm") ?? "?";
    }

    /// <summary>
    /// Lists players currently connected to the server with options to output JSON or filter duplicate IPs.
    /// </summary>
    /// <param name="caller">The player issuing the command or null for console.</param>
    /// <param name="command">The command containing output options.</param>
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/generic")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public void OnPlayersCommand(CCSPlayerController? caller, CommandInfo command)
    {
        bool isJson = command.GetArg(1).ToLower().Equals("-json");
        bool isDuplicate = command.GetArg(1).ToLower().Equals("-duplicate") || command.GetArg(2).ToLower().Equals("-duplicate");

        List<CCSPlayerController> playersToTarget;

        if (isDuplicate)
        {
            playersToTarget = Helper.GetValidPlayers().GroupBy(player => player.IpAddress?.Split(":")[0] ?? "Unknown")
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToList();
        }
        else
        {
            playersToTarget = Helper.GetValidPlayers();
        }

        if (!isJson)
        {
            if (caller != null)
            {
                bool canSeeIp = AdminManager.PlayerHasPermissions(new SteamID(caller.SteamID), "@css/showip");
                caller.PrintToConsole("--------- PLAYER LIST ---------");
                foreach (CCSPlayerController player in playersToTarget)
                {
                    caller.PrintToConsole(
                        $"• [#{player.UserId}] \"{player.PlayerName}\" (IP Address: \"{GetVisibleIp(canSeeIp, player)}\" SteamID64: \"{player.SteamID}\")");
                }
                ;
                caller.PrintToConsole("--------- END PLAYER LIST ---------");
            }
            else
            {
                Server.PrintToConsole("--------- PLAYER LIST ---------");
                foreach (CCSPlayerController player in playersToTarget)
                {
                    Server.PrintToConsole($"• [#{player.UserId}] \"{player.PlayerName}\" (IP Address: \"{player.IpAddress?.Split(":")[0]}\" SteamID64: \"{player.SteamID}\")");
                }
                ;
                Server.PrintToConsole("--------- END PLAYER LIST ---------");
            }
        }
        else
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            List<PlayerDto> playerDtos = playersToTarget
                .Where(player => player.UserId.HasValue)
                .Select(player =>
                {
                    CSMatchStats_t? matchStats = player.ActionTrackingServices?.MatchStats;

                    return new PlayerDto(
                        player.UserId.GetValueOrDefault(0),
                        player.PlayerName,
                        player.SteamID.ToString(),
                        GetVisibleIp(AdminManager.PlayerHasPermissions(new SteamID(caller!.SteamID), "@css/showip"), player),
                        player.Ping,
                        AdminManager.PlayerHasPermissions(new SteamID(player.SteamID), "@css/ban")
                        || AdminManager.PlayerHasPermissions(new SteamID(player.SteamID), "@css/generic"),
                        new PlayerStats(
                            player.Score,
                            matchStats?.Kills ?? 0,
                            matchStats?.Deaths ?? 0,
                            player.MVPs
                        )
                    );
                })
                .ToList();

            string playersJson = JsonSerializer.Serialize(playerDtos, options);

            if (caller != null)
            {
                caller.PrintToConsole(playersJson);
            }
            else
            {
                Server.PrintToConsole(playersJson);
            }
        }
    }

    /// <summary>
    /// Issues a kick to one or multiple players specified in the command arguments.
    /// </summary>
    /// <param name="caller">The player issuing the kick command.</param>
    /// <param name="command">The command with target player(s) and optional reason.</param>
    [RequiresPermissions("@css/kick")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnKickCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string callerName = Helper.GetCallerName(caller);

        TargetResult? targets = GetTarget(command);

        if (targets == null)
        {
            return;
        }

        List<CCSPlayerController> playersToTarget = targets.Players
            .Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

        if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
        {
            return;
        }

        string reason = Helper.GetArgsReason(command, 2);

        playersToTarget.ForEach(player =>
        {
            if (!player.IsValid)
            {
                return;
            }

            if (caller!.CanTarget(player))
            {
                Kick(caller, player, reason, callerName, command);
            }
        });

        Helper.LogCommand(caller, command);
    }


    /// <summary>
    /// Kicks a specified player immediately with reason, notifying the server and logging the action.
    /// </summary>
    /// <param name="caller">The player issuing the kick.</param>
    /// <param name="player">The player to be kicked.</param>
    /// <param name="reason">The reason for the kick.</param>
    /// <param name="callerName">Optional name of the kick issuer for notifications.</param>
    /// <param name="command">Optional command for logging.</param>
    public void Kick(CCSPlayerController? caller, CCSPlayerController player, string? reason = "Unknown", string? callerName = null, CommandInfo? command = null)
    {
        if (!player.IsValid)
        {
            return;
        }

        if (!caller.CanTarget(player))
        {
            return;
        }

        if (!player.UserId.HasValue)
        {
            return;
        }

        // Set default caller name if not provided
        callerName ??= Helper.GetCallerName(caller);
        reason ??= _localizer?["sa_unknown"] ?? "Unknown";

        PlayerInfo playerInfo = PlayersInfo[player.SteamID];
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Determine message keys and arguments for the kick notification
        (string? messageKey, string? activityMessageKey, object[]? centerArgs, object[]? adminActivityArgs) =
            ("sa_player_kick_message", "sa_admin_kick_message",
                new object[] { reason, "CALLER" },
                new object[] { "CALLER", player.PlayerName, reason });

        // Display center message to the kicked player
        Helper.DisplayCenterMessage(player, messageKey, callerName, centerArgs);

        // Display admin activity message to other players
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        // Schedule the kick for the player
        if (player.UserId.HasValue)
        {
            Helper.KickPlayer(player.UserId.Value, NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED, Config.OtherSettings.KickTime);
        }

        // Log the command and send Discord notification
        if (command == null)
        {
            Helper.LogCommand(caller, $"css_kick {Helper.GetLogName(player)} {reason}");
        }

        SimpleAdminApi?.OnPlayerPenaltiedEvent(playerInfo, adminInfo, PenaltyType.Kick, reason, -1, null);
    }

    /// <summary>
    /// Changes the current map to the specified map name or workshop map ID.
    /// </summary>
    /// <param name="caller">The player issuing the map change.</param>
    /// <param name="command">The command containing the map name or ID.</param>
    [RequiresPermissions("@css/changemap")]
    [CommandHelper(minArgs: 1, usage: "<mapname>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnMapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string map = command.GetCommandString.Split(" ")[1];
        ChangeMap(caller, map, command);
    }

    /// <summary>
    /// Changes to a specified map, validating it or handling workshop maps, and notifying the server and admins.
    /// </summary>
    /// <param name="caller">The player issuing the change.</param>
    /// <param name="map">The map name or identifier.</param>
    /// <param name="command">Optional command object for logging and replies.</param>
    public void ChangeMap(CCSPlayerController? caller, string map, CommandInfo? command = null)
    {
        string callerName = Helper.GetCallerName(caller);
        map = map.ToLower();

        if (map.StartsWith("ws:"))
        {
            string issuedCommand;

            if (long.TryParse(map.Replace("ws:", ""), out long mapId))
            {
                issuedCommand = $"host_workshop_map {mapId}";
            }
            else
            {
                issuedCommand = $"ds_workshop_changelevel {map.Replace("ws:", "")}";
            }

            AddTimer(3.0f, () =>
            {
                Server.ExecuteCommand(issuedCommand);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }
        else
        {
            if (!Server.IsMapValid(map))
            {
                string msg = $"Map {map} not found.";
                if (command != null)
                {
                    command.ReplyToCommand(msg);
                }
                else if (caller != null && caller.IsValid)
                {
                    caller.PrintToChat(msg);
                }
                else
                {
                    Server.PrintToConsole(msg);
                }

                return;
            }

            AddTimer(3.0f, () =>
            {
                Server.ExecuteCommand($"changelevel {map}");
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        (string? activityMessageKey, object[]? adminActivityArgs) =
            ("sa_admin_changemap_message",
                new object[] { "CALLER", map });

        // Display admin activity message to other players
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        Helper.LogCommand(caller, command?.GetCommandString ?? $"css_map {map}");
    }

    /// <summary>
    /// Changes the current map to a workshop map specified by name or ID.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">The command containing the workshop map identifier.</param>
    [CommandHelper(1, "<name or id>")]
    [RequiresPermissions("@css/changemap")]
    public void OnWorkshopMapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string map = command.GetArg(1);
        ChangeWorkshopMap(caller, map, command);
    }

    /// <summary>
    /// Changes to a specified workshop map by name or ID and notifies admins.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="map">The workshop map identifier.</param>
    /// <param name="command">Optional command for logging.</param>
    public void ChangeWorkshopMap(CCSPlayerController? caller, string map, CommandInfo? command = null)
    {
        map = map.ToLower();
        string callerName = Helper.GetCallerName(caller);

        // Determine the workshop command
        string issuedCommand;

        if (long.TryParse(map, out long mapId))
        {
            issuedCommand = $"host_workshop_map {mapId}";
        }
        else
        {
            issuedCommand = $"ds_workshop_changelevel {map}";
        }

        // Define the admin activity message and arguments
        string activityMessageKey = "sa_admin_changemap_message";

        // Display admin activity message to other players
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, ["CALLER", map]);
        }

        // Add timer to execute the map change command after a delay
        AddTimer(3.0f, () =>
        {
            Server.ExecuteCommand(issuedCommand);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

        // Log the command for the change
        Helper.LogCommand(caller, command?.GetCommandString ?? $"css_wsmap {map}");
    }

    /// <summary>
    /// Allows changing a console variable's value.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">The command with cvar name and value.</param>
    [CommandHelper(2, "<cvar> <value>")]
    [RequiresPermissions("@css/cvar")]
    public void OnCvarCommand(CCSPlayerController? caller, CommandInfo command)
    {
        ConVar? cvar = ConVar.Find(command.GetArg(1));
        string callerName = Helper.GetCallerName(caller);

        if (cvar == null)
        {
            command.ReplyToCommand($"Cvar \"{command.GetArg(1)}\" not found.");
            return;
        }

        if (cvar.Name.Equals("sv_cheats") && !AdminManager.PlayerHasPermissions(new SteamID(caller!.SteamID), "@css/cheats"))
        {
            command.ReplyToCommand($"You don't have permissions to change \"{command.GetArg(1)}\".");
            return;
        }

        Helper.LogCommand(caller, command);
        string value = command.GetArg(2);
        Server.ExecuteCommand($"{cvar.Name} {value}");
        command.ReplyToCommand($"{callerName} changed cvar {cvar.Name} to {value}.");
        Logger.LogInformation($"{callerName} changed cvar {cvar.Name} to {value}.");
    }

    /// <summary>
    /// Executes an RCON command on the server.
    /// </summary>
    /// <param name="caller">The player issuing the command.</param>
    /// <param name="command">The command string to execute via RCON.</param>
    [CommandHelper(1, "<command>")]
    [RequiresPermissions("@css/rcon")]
    public void OnRconCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string callerName = Helper.GetCallerName(caller);
        Helper.LogCommand(caller, command);
        Server.ExecuteCommand(command.ArgString);
        command.ReplyToCommand($"{callerName} executed command {command.ArgString}.");
        Logger.LogInformation($"{callerName} executed command ({command.ArgString}).");
    }

    /// <summary>
    /// Restarts the game.
    /// </summary>
    /// <param name="caller">The player or console initiating the restart.</param>
    /// <param name="command">The restart command info.</param>
    [RequiresPermissions("@css/generic")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnRestartCommand(CCSPlayerController? caller, CommandInfo command)
    {
        RestartGame(caller);
    }

    /// <summary>
    /// Opens plugin manager menu for the caller with options to load or unload plugins.
    /// </summary>
    /// <param name="caller">The player opening the plugin manager.</param>
    /// <param name="commandInfo">The command parameters.</param>
    [RequiresPermissions("@css/root")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPluginManagerCommand(CCSPlayerController? caller, CommandInfo commandInfo)
    {
        if (caller == null)
        {
            return;
        }

        IPluginManager? pluginManager = Helper.GetPluginManager();
        if (pluginManager == null)
        {
            Logger.LogError("Unable to access PluginManager.");
            return;
        }

        MethodInfo? getLoadedPluginsMethod = pluginManager.GetType().GetMethod("GetLoadedPlugins", BindingFlags.Public | BindingFlags.Instance);
        if (getLoadedPluginsMethod?.Invoke(pluginManager, null) is not IEnumerable plugins)
        {
            Logger.LogError("Unable to retrieve plugins.");
            return;
        }

        MenuBuilder pluginsMenu = new MenuBuilder("sa_menu_pluginsmanager_title", caller, Localizer);

        foreach (object? plugin in plugins)
        {
            Type pluginType = plugin.GetType();

            // Accessing each property with the Type of the plugin
            object? pluginId = pluginType.GetProperty("PluginId")?.GetValue(plugin);
            string? state = pluginType.GetProperty("State")?.GetValue(plugin)?.ToString();
            string? path = pluginType.GetProperty("FilePath")?.GetValue(plugin)?.ToString();
            path = Path.GetFileName(Path.GetDirectoryName(path));

            // Access nested properties within "Plugin" (like ModuleName, ModuleVersion, etc.)
            object? nestedPlugin = pluginType.GetProperty("Plugin")?.GetValue(plugin);
            if (nestedPlugin == null)
            {
                continue;
            }

            bool loaded = state?.ToUpper() != "UNLOADED";
            string status = "OFF";
            if (loaded)
            {
                status = "ON";
            }

            Type nestedType = nestedPlugin.GetType();
            string moduleName = nestedType.GetProperty("ModuleName")?.GetValue(nestedPlugin)?.ToString() ?? "Unknown";
            string? moduleVersion = nestedType.GetProperty("ModuleVersion")?.GetValue(nestedPlugin)?.ToString();
            // var moduleAuthor = nestedType.GetProperty("ModuleAuthor")?.GetValue(nestedPlugin)?.ToString();
            // var moduleDescription = nestedType.GetProperty("ModuleDescription")?.GetValue(nestedPlugin)?.ToString();

            pluginsMenu.AddOption($"({status}) [{moduleName} {moduleVersion}]", _ =>
            {
                if (loaded)
                {
                    caller.SendLocalizedMessage(Localizer, "sa_menu_pluginsmanager_unloaded", moduleName);
                    Server.ExecuteCommand($"css_plugins unload {pluginId}");
                }
                else
                {
                    caller.SendLocalizedMessage(Localizer, "sa_menu_pluginsmanager_loaded", moduleName);
                    Server.ExecuteCommand($"css_plugins load {path}");
                }

                AddTimer(0.1f, () => OnPluginManagerCommand(caller, commandInfo));
            }, keepOpen: true);

            // Console.WriteLine($"[#{pluginId}:{state?.ToUpper()}]: \"{moduleName ?? "Unknown"}\" ({moduleVersion ?? "Unknown"}) by {moduleAuthor}");
        }

        pluginsMenu.OpenMenu(caller);
    }

    /// <summary>
    /// The player's IP without port, or "Unknown" when the viewer may not see it.
    /// </summary>
    private static string GetVisibleIp(bool canSeeIp, CCSPlayerController player)
    {
        if (!canSeeIp)
        {
            return "Unknown";
        }

        return player.IpAddress?.Split(":")[0] ?? "Unknown";
    }

    /// <summary>
    /// Restarts the game process by issuing the restart game command to the server and logging the action.
    /// </summary>
    /// <param name="admin">The admin or console requesting the restart.</param>
    public static void RestartGame(CCSPlayerController? admin)
    {
        Helper.LogCommand(admin, "css_restartgame");

        // TODO: Localize
        string name = Helper.GetCallerName(admin);
        Server.PrintToChatAll($"[SA] {name}: Restarting game...");
        Server.ExecuteCommand("mp_restartgame 2");
    }
}
