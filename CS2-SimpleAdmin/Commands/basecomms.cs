using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Entities;
using CS2_SimpleAdmin.Managers;
using CS2_SimpleAdmin.Menus;
using CS2_SimpleAdminApi;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    /// <summary>
    /// Processes the 'gag' command, applying a muted penalty to target players with optional time and reason.
    /// </summary>
    /// <param name="caller">The player issuing the gag command or null for console.</param>
    /// <param name="command">The command input containing targets, time, and reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnGagCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        string callerName = Helper.GetCallerName(caller);

        TargetResult? targets = GetTarget(command);
        if (targets == null)
        {
            return;
        }

        List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

        if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
        {
            return;
        }

        string reason = Helper.GetArgsReason(command, 3);

        int time = Helper.ParsePenaltyTime(command.GetArg(2));

        playersToTarget.ForEach(player =>
        {
            if (!caller!.CanTarget(player))
            {
                return;
            }

            if (time < 0 && caller != null && caller.IsValid && Config.OtherSettings.ShowBanMenuIfNoTime)
            {
                BasicMenu.CreatePenaltyMenu(caller, player, PenaltyType.Gag).OpenMenu(caller);
                return;
            }

            Gag(caller, player, time, reason, callerName, command);
        });
    }

    /// <summary>
    /// Applies the gag penalty logic to an individual player, performing permission checks, notification, and logging.
    /// </summary>
    /// <param name="caller">The player issuing the gag.</param>
    /// <param name="player">The player to gag.</param>
    /// <param name="time">Duration of the gag in minutes, 0 is permanent.</param>
    /// <param name="reason">Reason for the gag.</param>
    /// <param name="callerName">Optional caller name for notifications.</param>
    /// <param name="command">Optional command info for logging.</param>
    /// <param name="silent">If true, suppresses logging notifications.</param>
    internal void Gag(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, CommandInfo? command = null, bool silent = false)
    {
        if (DatabaseProvider == null || !player.IsValid || !player.UserId.HasValue)
        {
            return;
        }

        if (!caller.CanTarget(player))
        {
            return;
        }

        if (!CheckValidMute(caller, time))
        {
            return;
        }

        // Set default caller name if not provided
        callerName ??= Helper.GetCallerName(caller);

        // Get player and admin information
        PlayerInfo playerInfo = PlayersInfo[player.SteamID];
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Asynchronously handle gag logic
        Task.Run(async () =>
        {
            int? penaltyId = await MuteManager.MutePlayer(playerInfo, adminInfo, reason, time);
            await Server.NextWorldUpdateAsync(() =>
            {
                SimpleAdminApi?.OnPlayerPenaltiedEvent(playerInfo, adminInfo, PenaltyType.Gag, reason, time,
                    penaltyId);
            });
        });

        // Add penalty to the player's penalty manager
        PlayerPenaltyManager.AddPenalty(player.Slot, PenaltyType.Gag, Time.ActualDateTime().AddMinutes(time), time);

        // Determine message keys and arguments based on gag time (permanent or timed)
        string? messageKey;
        string? activityMessageKey;
        object[]? playerArgs;
        object[]? adminActivityArgs;

        if (time == 0)
        {
            messageKey = "sa_player_gag_message_perm";
            activityMessageKey = "sa_admin_gag_message_perm";
            playerArgs = [reason, "CALLER"];
            adminActivityArgs = ["CALLER", player.PlayerName, reason];
        }
        else
        {
            messageKey = "sa_player_gag_message_time";
            activityMessageKey = "sa_admin_gag_message_time";
            playerArgs = new object[] { reason, time, "CALLER" };
            adminActivityArgs = new object[] { "CALLER", player.PlayerName, reason, time };
        }

        // Display center message to the gagged player
        Helper.DisplayCenterMessage(player, messageKey, callerName, playerArgs);

        // Display admin activity message to other players
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        // Increment the player's total gags count
        PlayersInfo[player.SteamID].TotalGags++;

        // Log the gag command and send Discord notification
        if (!silent)
        {
            if (command == null)
            {
                Helper.LogCommand(caller, $"css_gag {Helper.GetLogName(player)} {time} {reason}");
            }
            else
            {
                Helper.LogCommand(caller, command);
            }
        }

        Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Gag, _localizer);
    }

    /// <summary>
    /// Adds a gag penalty to a player identified by SteamID, supporting offline players.
    /// </summary>
    /// <param name="caller">The player issuing the command or null for console.</param>
    /// <param name="steamid">SteamID of the target player.</param>
    /// <param name="time">Duration in minutes (0 for permanent).</param>
    /// <param name="reason">Reason for the gag.</param>
    internal void AddGag(CCSPlayerController? caller, SteamID steamid, int time, string reason)
    {
        // Set default caller name if not provided
        string callerName = Helper.GetCallerName(caller);

        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid.SteamId64);

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
            {
                return;
            }

            Gag(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            if (!caller.CanTarget(steamid))
            {
                return;
            }

            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                int? penaltyId = await MuteManager.AddMuteBySteamid(steamid.SteamId64, adminInfo, reason, time, 3);
                await Server.NextWorldUpdateAsync(() =>
                {
                    SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamid, adminInfo, PenaltyType.Gag, reason, time,
                        penaltyId);
                });
            });

            Helper.SendDiscordPenaltyMessage(caller, steamid.SteamId64.ToString(), reason, time, PenaltyType.Gag, _localizer);
        }
    }

    /// <summary>
    /// Handles the 'addgag' command, which adds a gag penalty to a player specified by SteamID.
    /// </summary>
    /// <param name="caller">The player issuing the command or null for console.</param>
    /// <param name="command">Command input that includes SteamID, optional time, and reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<steamid> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnAddGagCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        // Set caller name
        string callerName = Helper.GetCallerName(caller);

        // Validate command arguments
        if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1)))
        {
            return;
        }

        // Validate and extract SteamID
        if (!Helper.ValidateSteamId(command.GetArg(1), out SteamID? steamId) || steamId == null)
        {
            command.ReplyToCommand("Invalid SteamID64.");
            return;
        }

        ulong steamid = steamId.SteamId64;
        string reason = Helper.GetArgsReason(command, 3);

        int time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(2)));
        if (!CheckValidMute(caller, time))
        {
            return;
        }

        // Get player and admin info
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Attempt to match player based on SteamID
        CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid);

        if (player != null && player.IsValid)
        {
            // Check if caller can target the player
            if (!caller.CanTarget(player))
            {
                return;
            }

            // Perform the gag for an online player
            Gag(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            if (!caller.CanTarget(new SteamID(steamId.SteamId64)))
            {
                return;
            }

            // Asynchronous gag operation for offline players
            Task.Run(async () =>
            {
                int? penaltyId = await MuteManager.AddMuteBySteamid(steamid, adminInfo, reason, time);
                await Server.NextWorldUpdateAsync(() =>
                {
                    SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamId, adminInfo, PenaltyType.Gag, reason, time,
                        penaltyId);
                });
            });

            Helper.SendDiscordPenaltyMessage(caller, steamid.ToString(), reason, time, PenaltyType.Gag, _localizer);

            command.ReplyToCommand($"Player with steamid {steamid} is not online. Gag has been added offline.");
        }

        // Log the gag command and respond to the command
        Helper.LogCommand(caller, command);
    }

    /// <summary>
    /// Handles removing a gag penalty ('ungag') of a player, either by SteamID or pattern match.
    /// </summary>
    /// <param name="caller">The player issuing the ungag command or null for console.</param>
    /// <param name="command">Command input containing SteamID or player name and optional reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<steamid or name> [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnUngagCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        string callerSteamId = caller?.SteamID.ToString() ?? _localizer?["sa_console"] ?? "Console";
        string pattern = command.GetArg(1);
        string reason = Helper.GetArgsReason(command, 2);

        if (pattern.Length <= 1)
        {
            command.ReplyToCommand($"Too short pattern to search.");
            return;
        }

        Helper.LogCommand(caller, command);

        // Check if pattern is a valid SteamID64
        if (Helper.ValidateSteamId(pattern, out SteamID? steamId) && steamId != null)
        {
            CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamId.SteamId64);

            if (player != null && player.IsValid)
            {
                PlayerPenaltyManager.RemovePenaltiesByType(player.Slot, PenaltyType.Gag);

                Task.Run(async () =>
                {
                    await MuteManager.UnmutePlayer(player.SteamID.ToString(), callerSteamId, reason);
                });

                command.ReplyToCommand($"Ungaged player {player.PlayerName}.");
                return;
            }
        }

        // If not a valid SteamID64, check by player name
        List<CCSPlayerController> nameMatches = Helper.GetPlayerFromName(pattern);
        CCSPlayerController? namePlayer;

        if (nameMatches.Count == 1)
        {
            namePlayer = nameMatches.FirstOrDefault();
        }
        else
        {
            namePlayer = null;
        }

        if (namePlayer != null && namePlayer.IsValid)
        {
            PlayerPenaltyManager.RemovePenaltiesByType(namePlayer.Slot, PenaltyType.Gag);

            if (namePlayer.UserId.HasValue && PlayersInfo[namePlayer.SteamID].TotalGags > 0)
            {
                PlayersInfo[namePlayer.SteamID].TotalGags--;
            }

            Task.Run(async () =>
            {
                await MuteManager.UnmutePlayer(namePlayer.SteamID.ToString(), callerSteamId, reason);
            });

            command.ReplyToCommand($"Ungaged player {namePlayer.PlayerName}.");
        }
        else
        {
            Task.Run(async () =>
            {
                await MuteManager.UnmutePlayer(pattern, callerSteamId, reason);
            });

            command.ReplyToCommand($"Ungaged offline player with pattern {pattern}.");
        }
    }

    /// <summary>
    /// Processes the 'mute' command, applying a voice mute penalty to target players with optional time and reason.
    /// </summary>
    /// <param name="caller">The player issuing the mute command or null for console.</param>
    /// <param name="command">The command input containing targets, time, and reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnMuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        string callerName = Helper.GetCallerName(caller);

        TargetResult? targets = GetTarget(command);
        if (targets == null)
        {
            return;
        }

        List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

        if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
        {
            return;
        }

        string reason = Helper.GetArgsReason(command, 3);

        int time = Helper.ParsePenaltyTime(command.GetArg(2));

        playersToTarget.ForEach(player =>
        {
            if (!caller!.CanTarget(player))
            {
                return;
            }

            if (time < 0 && caller != null && caller.IsValid && Config.OtherSettings.ShowBanMenuIfNoTime)
            {
                BasicMenu.CreatePenaltyMenu(caller, player, PenaltyType.Mute).OpenMenu(caller);
                return;
            }

            Mute(caller, player, time, reason, callerName, command);
        });
    }

    /// <summary>
    /// Applies the mute penalty logic to an individual player, handling permissions, notifications, logging, and countdown timers.
    /// </summary>
    /// <param name="caller">The player issuing the mute.</param>
    /// <param name="player">The player to mute.</param>
    /// <param name="time">Duration in minutes, 0 indicates permanent mute.</param>
    /// <param name="reason">Reason for the mute.</param>
    /// <param name="callerName">Optional caller name for notification messages.</param>
    /// <param name="command">Optional command info for logging.</param>
    /// <param name="silent">If true, suppresses some logging.</param>
    internal void Mute(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, CommandInfo? command = null, bool silent = false)
    {
        if (DatabaseProvider == null || !player.IsValid || !player.UserId.HasValue)
        {
            return;
        }

        if (!caller.CanTarget(player))
        {
            return;
        }

        if (!CheckValidMute(caller, time))
        {
            return;
        }

        // Set default caller name if not provided
        callerName ??= Helper.GetCallerName(caller);

        // Get player and admin information
        PlayerInfo playerInfo = PlayersInfo[player.SteamID];
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Set player's voice flags to muted
        player.VoiceFlags = VoiceFlags.Muted;

        // Asynchronously handle mute logic
        Task.Run(async () =>
        {
            int? penaltyId = await MuteManager.MutePlayer(playerInfo, adminInfo, reason, time, 1);
            await Server.NextWorldUpdateAsync(() =>
            {
                SimpleAdminApi?.OnPlayerPenaltiedEvent(playerInfo, adminInfo, PenaltyType.Mute, reason, time,
                    penaltyId);
            });
        });

        // Add penalty to the player's penalty manager
        PlayerPenaltyManager.AddPenalty(player.Slot, PenaltyType.Mute, Time.ActualDateTime().AddMinutes(time), time);

        // Determine message keys and arguments based on mute time (permanent or timed)
        string? messageKey;
        string? activityMessageKey;
        object[]? playerArgs;
        object[]? adminActivityArgs;

        if (time == 0)
        {
            messageKey = "sa_player_mute_message_perm";
            activityMessageKey = "sa_admin_mute_message_perm";
            playerArgs = [reason, "CALLER"];
            adminActivityArgs = ["CALLER", player.PlayerName, reason];
        }
        else
        {
            messageKey = "sa_player_mute_message_time";
            activityMessageKey = "sa_admin_mute_message_time";
            playerArgs = new object[] { reason, time, "CALLER" };
            adminActivityArgs = new object[] { "CALLER", player.PlayerName, reason, time };
        }

        // Display center message to the muted player
        Helper.DisplayCenterMessage(player, messageKey, callerName, playerArgs);

        // Display admin activity message to other players
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        // Increment the player's total mutes count
        PlayersInfo[player.SteamID].TotalMutes++;

        // Log the mute command and send Discord notification
        if (!silent)
        {
            if (command == null)
            {
                Helper.LogCommand(caller, $"css_mute {Helper.GetLogName(player)} {time} {reason}");
            }
            else
            {
                Helper.LogCommand(caller, command);
            }
        }

        Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Mute, _localizer);
    }

    /// <summary>
    /// Handles the 'addmute' command that adds a mute penalty to a player specified by SteamID.
    /// </summary>
    /// <param name="caller">The player issuing the command or null for console.</param>
    /// <param name="command">Command input includes SteamID, optional time, and reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<steamid> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnAddMuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        // Set caller name
        string callerName = Helper.GetCallerName(caller);

        // Validate command arguments
        if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1)))
        {
            return;
        }

        // Validate and extract SteamID
        if (!Helper.ValidateSteamId(command.GetArg(1), out SteamID? steamId) || steamId == null)
        {
            command.ReplyToCommand("Invalid SteamID64.");
            return;
        }

        ulong steamid = steamId.SteamId64;
        string reason = Helper.GetArgsReason(command, 3);

        int time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(2)));
        if (!CheckValidMute(caller, time))
        {
            return;
        }

        // Get player and admin info
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Attempt to match player based on SteamID
        CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid);

        if (player != null && player.IsValid)
        {
            // Check if caller can target the player
            if (!caller.CanTarget(player))
            {
                return;
            }

            // Perform the mute for an online player
            Mute(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            if (!caller.CanTarget(new SteamID(steamId.SteamId64)))
            {
                return;
            }

            // Asynchronous mute operation for offline players
            Task.Run(async () =>
            {
                int? penaltyId = await MuteManager.AddMuteBySteamid(steamid, adminInfo, reason, time, 1);
                await Server.NextWorldUpdateAsync(() =>
                {
                    SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamId, adminInfo, PenaltyType.Mute, reason, time,
                        penaltyId);
                });
            });

            Helper.SendDiscordPenaltyMessage(caller, steamid.ToString(), reason, time, PenaltyType.Mute, _localizer);

            command.ReplyToCommand($"Player with steamid {steamid} is not online. Mute has been added offline.");
        }

        // Log the mute command and respond to the command
        Helper.LogCommand(caller, command);
    }

    /// <summary>
    /// Asynchronously adds a mute penalty to a player by Steam ID. Handles both online and offline players.
    /// </summary>
    /// <param name="caller">The admin/player issuing the mute.</param>
    /// <param name="steamid">The Steam ID of the player to mute.</param>
    /// <param name="time">Duration of the mute in minutes.</param>
    /// <param name="reason">Reason for the mute.</param>
    /// <param name="muteManager">Optional mute manager instance for handling database ops.</param>
    internal void AddMute(CCSPlayerController? caller, SteamID steamid, int time, string reason, MuteManager? muteManager = null)
    {
        // Set default caller name if not provided
        string callerName = Helper.GetCallerName(caller);

        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid.SteamId64);

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
            {
                return;
            }

            Mute(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            if (!caller.CanTarget(steamid))
            {
                return;
            }

            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                int? penaltyId = await MuteManager.AddMuteBySteamid(steamid.SteamId64, adminInfo, reason, time, 1);
                await Server.NextWorldUpdateAsync(() =>
                {
                    SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamid, adminInfo, PenaltyType.Mute, reason, time,
                        penaltyId);
                });
            });

            Helper.SendDiscordPenaltyMessage(caller, steamid.SteamId64.ToString(), reason, time, PenaltyType.Mute, _localizer);
        }
    }

    /// <summary>
    /// Handles the unmute command - removes mute penalty from player identified by SteamID or name.
    /// Can target both online and offline players.
    /// </summary>
    /// <param name="caller">The admin/player issuing the unmute.</param>
    /// <param name="command">The command arguments including target identifier and optional reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<steamid or name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnUnmuteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        string callerSteamId = caller?.SteamID.ToString() ?? _localizer?["sa_console"] ?? "Console";
        string pattern = command.GetArg(1);
        string reason = Helper.GetArgsReason(command, 2);

        if (pattern.Length <= 1)
        {
            command.ReplyToCommand("Too short pattern to search.");
            return;
        }

        Helper.LogCommand(caller, command);

        // Check if pattern is a valid SteamID64
        if (Helper.ValidateSteamId(pattern, out SteamID? steamId) && steamId != null)
        {
            CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamId.SteamId64);

            if (player != null && player.IsValid)
            {
                PlayerPenaltyManager.RemovePenaltiesByType(player.Slot, PenaltyType.Mute);
                player.VoiceFlags = VoiceFlags.Normal;

                Task.Run(async () =>
                {
                    await MuteManager.UnmutePlayer(player.SteamID.ToString(), callerSteamId, reason, 1);
                });

                command.ReplyToCommand($"Unmuted player {player.PlayerName}.");
                return;
            }
        }

        // If not a valid SteamID64, check by player name
        List<CCSPlayerController> nameMatches = Helper.GetPlayerFromName(pattern);
        CCSPlayerController? namePlayer;

        if (nameMatches.Count == 1)
        {
            namePlayer = nameMatches.FirstOrDefault();
        }
        else
        {
            namePlayer = null;
        }

        if (namePlayer != null && namePlayer.IsValid)
        {
            PlayerPenaltyManager.RemovePenaltiesByType(namePlayer.Slot, PenaltyType.Mute);
            namePlayer.VoiceFlags = VoiceFlags.Normal;

            if (namePlayer.UserId.HasValue && PlayersInfo[namePlayer.SteamID].TotalMutes > 0)
            {
                PlayersInfo[namePlayer.SteamID].TotalMutes--;
            }

            Task.Run(async () =>
            {
                await MuteManager.UnmutePlayer(namePlayer.SteamID.ToString(), callerSteamId, reason, 1);
            });

            command.ReplyToCommand($"Unmuted player {namePlayer.PlayerName}.");
        }
        else
        {
            Task.Run(async () =>
            {
                await MuteManager.UnmutePlayer(pattern, callerSteamId, reason, 1);
            });

            command.ReplyToCommand($"Unmuted offline player with pattern {pattern}.");
        }
    }

    /// <summary>
    /// Issue a 'silence' penalty to a player - disables voice communication.
    /// Handles online and offline players, with duration and reason specified.
    /// </summary>
    /// <param name="caller">The admin/player issuing the silence.</param>
    /// <param name="command">Command containing target, duration, and optional reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSilenceCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        string callerName = Helper.GetCallerName(caller);

        TargetResult? targets = GetTarget(command);
        if (targets == null)
        {
            return;
        }

        List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

        if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
        {
            return;
        }

        string reason = Helper.GetArgsReason(command, 3);

        int time = Helper.ParsePenaltyTime(command.GetArg(2));

        playersToTarget.ForEach(player =>
        {
            if (!caller!.CanTarget(player))
            {
                return;
            }

            if (time < 0 && caller != null && caller.IsValid && Config.OtherSettings.ShowBanMenuIfNoTime)
            {
                BasicMenu.CreatePenaltyMenu(caller, player, PenaltyType.Silence).OpenMenu(caller);
                return;
            }

            Silence(caller, player, time, reason, callerName, command);
        });
    }

    /// <summary>
    /// Applies silence logical processing for a player - updates database and notifies.
    /// </summary>
    /// <param name="caller">Admin/player applying the silence.</param>
    /// <param name="player">Target player.</param>
    /// <param name="time">Duration of silence.</param>
    /// <param name="reason">Reason for silence.</param>
    /// <param name="callerName">Optional name of silent admin or console.</param>
    /// <param name="command">Optional command details for logging.</param>
    /// <param name="silent">If true, suppresses logging notifications.</param>
    internal void Silence(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, CommandInfo? command = null, bool silent = false)
    {
        if (DatabaseProvider == null || !player.IsValid || !player.UserId.HasValue)
        {
            return;
        }

        if (!caller.CanTarget(player))
        {
            return;
        }

        if (!CheckValidMute(caller, time))
        {
            return;
        }

        // Set default caller name if not provided
        callerName ??= Helper.GetCallerName(caller);

        // Get player and admin information
        PlayerInfo playerInfo = PlayersInfo[player.SteamID];
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Asynchronously handle silence logic
        Task.Run(async () =>
        {
            int? penaltyId = await MuteManager.MutePlayer(playerInfo, adminInfo, reason, time, 2);
            await Server.NextWorldUpdateAsync(() =>
            {
                SimpleAdminApi?.OnPlayerPenaltiedEvent(playerInfo, adminInfo, PenaltyType.Silence, reason, time,
                    penaltyId);
            });
        });

        // Add penalty to the player's penalty manager
        PlayerPenaltyManager.AddPenalty(player.Slot, PenaltyType.Silence, Time.ActualDateTime().AddMinutes(time), time);
        player.VoiceFlags = VoiceFlags.Muted;

        // Determine message keys and arguments based on silence time (permanent or timed)
        string? messageKey;
        string? activityMessageKey;
        object[]? playerArgs;
        object[]? adminActivityArgs;

        if (time == 0)
        {
            messageKey = "sa_player_silence_message_perm";
            activityMessageKey = "sa_admin_silence_message_perm";
            playerArgs = [reason, "CALLER"];
            adminActivityArgs = ["CALLER", player.PlayerName, reason];
        }
        else
        {
            messageKey = "sa_player_silence_message_time";
            activityMessageKey = "sa_admin_silence_message_time";
            playerArgs = new object[] { reason, time, "CALLER" };
            adminActivityArgs = new object[] { "CALLER", player.PlayerName, reason, time };
        }

        // Display center message to the silenced player
        Helper.DisplayCenterMessage(player, messageKey, callerName, playerArgs);

        // Display admin activity message to other players
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        // Increment the player's total silences count
        PlayersInfo[player.SteamID].TotalSilences++;

        // Log the silence command and send Discord notification
        if (!silent)
        {
            if (command == null)
            {
                Helper.LogCommand(caller, $"css_silence {Helper.GetLogName(player)} {time} {reason}");
            }
            else
            {
                Helper.LogCommand(caller, command);
            }
        }

        Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Silence, _localizer);
    }

    /// <summary>
    /// Handles the 'AddSilence' command, applying a silence penalty to a player specified by SteamID,
    /// with support for offline player penalties.
    /// </summary>
    /// <param name="caller">The player/admin issuing the command.</param>
    /// <param name="command">The command input containing SteamID, optional time, and reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnAddSilenceCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        // Set caller name
        string callerName = Helper.GetCallerName(caller);

        // Validate command arguments
        if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1)))
        {
            return;
        }

        // Validate and extract SteamID
        if (!Helper.ValidateSteamId(command.GetArg(1), out SteamID? steamId) || steamId == null)
        {
            command.ReplyToCommand("Invalid SteamID64.");
            return;
        }

        ulong steamid = steamId.SteamId64;
        string reason = Helper.GetArgsReason(command, 3);

        int time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(2)));
        if (!CheckValidMute(caller, time))
        {
            return;
        }

        // Get player and admin info
        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        // Attempt to match player based on SteamID
        CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid);

        if (player != null && player.IsValid)
        {
            // Check if caller can target the player
            if (!caller.CanTarget(player))
            {
                return;
            }

            // Perform the silence for an online player
            Silence(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            if (!caller.CanTarget(new SteamID(steamId.SteamId64)))
            {
                return;
            }

            // Asynchronous silence operation for offline players
            Task.Run(async () =>
            {
                int? penaltyId = await MuteManager.AddMuteBySteamid(steamid, adminInfo, reason, time, 2);
                await Server.NextWorldUpdateAsync(() =>
                {
                    SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamId, adminInfo, PenaltyType.Silence, reason,
                        time, penaltyId);
                });
            });

            Helper.SendDiscordPenaltyMessage(caller, steamid.ToString(), reason, time, PenaltyType.Silence, _localizer);

            command.ReplyToCommand($"Player with steamid {steamid} is not online. Silence has been added offline.");
        }

        // Log the silence command and respond to the command
        Helper.LogCommand(caller, command);
    }

    /// <summary>
    /// Adds a silence penalty to a player by Steam ID. Manages both online and offline player cases.
    /// </summary>
    /// <param name="caller">Admin/player initiating the silence.</param>
    /// <param name="steamid">Steam ID of player.</param>
    /// <param name="time">Duration of silence.</param>
    /// <param name="reason">Reason for the penalty.</param>
    /// <param name="muteManager">Optional mute manager for DB operations.</param>
    internal void AddSilence(CCSPlayerController? caller, SteamID steamid, int time, string reason, MuteManager? muteManager = null)
    {
        // Set default caller name if not provided
        string callerName = Helper.GetCallerName(caller);

        PlayerInfo? adminInfo = Helper.GetAdminInfo(caller);

        CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid.SteamId64);

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
            {
                return;
            }

            Silence(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            if (!caller.CanTarget(steamid))
            {
                return;
            }

            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                int? penaltyId = await MuteManager.AddMuteBySteamid(steamid.SteamId64, adminInfo, reason, time, 2);
                await Server.NextWorldUpdateAsync(() =>
                {
                    SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamid, adminInfo, PenaltyType.Silence, reason,
                        time, penaltyId);
                });
            });

            Helper.SendDiscordPenaltyMessage(caller, steamid.SteamId64.ToString(), reason, time, PenaltyType.Silence, _localizer);
        }
    }

    /// <summary>
    /// Removes the silence penalty from a player, either by SteamID, name, or offline pattern.
    /// Resets voice settings and updates notices accordingly.
    /// </summary>
    /// <param name="caller">Admin/player issuing the unsilence.</param>
    /// <param name="command">Command arguments with target pattern and optional reason.</param>
    [RequiresPermissions("@css/chat")]
    [CommandHelper(minArgs: 1, usage: "<steamid or name> [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnUnsilenceCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (DatabaseProvider == null)
        {
            return;
        }

        string callerSteamId = caller?.SteamID.ToString() ?? _localizer?["sa_console"] ?? "Console";
        string pattern = command.GetArg(1);
        string reason = Helper.GetArgsReason(command, 2);

        if (pattern.Length <= 1)
        {
            command.ReplyToCommand("Too short pattern to search.");
            return;
        }

        Helper.LogCommand(caller, command);

        // Check if pattern is a valid SteamID64
        if (Helper.ValidateSteamId(pattern, out SteamID? steamId) && steamId != null)
        {
            CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamId.SteamId64);

            if (player != null && player.IsValid)
            {
                PlayerPenaltyManager.RemovePenaltiesByType(player.Slot, PenaltyType.Silence);

                // Reset voice flags to normal
                player.VoiceFlags = VoiceFlags.Normal;

                Task.Run(async () =>
                {
                    await MuteManager.UnmutePlayer(player.SteamID.ToString(), callerSteamId, reason, 2); // Unmute by type 2 (silence)
                });

                command.ReplyToCommand($"Unsilenced player {player.PlayerName}.");
                return;
            }
        }

        // If not a valid SteamID64, check by player name
        List<CCSPlayerController> nameMatches = Helper.GetPlayerFromName(pattern);
        CCSPlayerController? namePlayer;

        if (nameMatches.Count == 1)
        {
            namePlayer = nameMatches.FirstOrDefault();
        }
        else
        {
            namePlayer = null;
        }

        if (namePlayer != null && namePlayer.IsValid)
        {
            PlayerPenaltyManager.RemovePenaltiesByType(namePlayer.Slot, PenaltyType.Silence);

            // Reset voice flags to normal
            namePlayer.VoiceFlags = VoiceFlags.Normal;

            if (namePlayer.UserId.HasValue && PlayersInfo[namePlayer.SteamID].TotalSilences > 0)
            {
                PlayersInfo[namePlayer.SteamID].TotalSilences--;
            }

            Task.Run(async () =>
            {
                await MuteManager.UnmutePlayer(namePlayer.SteamID.ToString(), callerSteamId, reason, 2); // Unmute by type 2 (silence)
            });

            command.ReplyToCommand($"Unsilenced player {namePlayer.PlayerName}.");
        }
        else
        {
            Task.Run(async () =>
            {
                await MuteManager.UnmutePlayer(pattern, callerSteamId, reason, 2); // Unmute by type 2 (silence)
            });

            command.ReplyToCommand($"Unsilenced offline player with pattern {pattern}.");
        }
    }

    /// <summary>
    /// Validates mute penalty duration based on admin privileges and configured max duration.
    /// </summary>
    /// <param name="caller">Admin/player issuing the mute.</param>
    /// <param name="duration">Requested duration in minutes.</param>
    /// <returns>True if mute penalty duration is allowed; false otherwise.</returns>
    private bool CheckValidMute(CCSPlayerController? caller, int duration)
    {
        if (caller == null)
        {
            return true;
        }

        bool canPermMute = AdminManager.PlayerHasPermissions(new SteamID(caller.SteamID), "@css/permmute");

        if (duration <= 0 && !canPermMute)
        {
            caller.PrintToChat($"{_localizer!["sa_prefix"]} {_localizer["sa_ban_perm_restricted"]}");
            return false;
        }

        if (duration <= Config.OtherSettings.MaxMuteDuration || canPermMute)
        {
            return true;
        }

        caller.PrintToChat($"{_localizer!["sa_prefix"]} {_localizer["sa_ban_max_duration_exceeded", Config.OtherSettings.MaxMuteDuration]}");
        return false;
    }
}
