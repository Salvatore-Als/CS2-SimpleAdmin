using System.Numerics;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using CS2_SimpleAdmin.Managers;
using CS2_SimpleAdmin.Models;
using CS2_SimpleAdminApi;
using Microsoft.Extensions.Logging;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    private bool _serverLoading;

    private void RegisterEvents()
    {
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        // RegisterListener<Listeners.OnClientConnect>(OnClientConnect);
        // RegisterListener<Listeners.OnClientConnect>(OnClientConnect);
        RegisterListener<Listeners.OnClientConnected>(OnClientConnected);
        RegisterListener<Listeners.OnGameServerSteamAPIActivated>(OnGameServerSteamAPIActivated);
        if (Config.OtherSettings.UserMessageGagChatType)
        {
            HookUserMessage(118, HookUmChat);
        }

        AddCommandListener(null, ComamndListenerHandler);
        // AddCommandListener("callvote", OnCommandCallVote);
        // AddCommandListener("say", OnCommandSay);
        // AddCommandListener("say_team", OnCommandTeamSay);
    }

    private void UnregisterEvents()
    {
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnClientConnect>(OnClientConnect);
        RemoveListener<Listeners.OnClientConnected>(OnClientConnected);
        RemoveListener<Listeners.OnGameServerSteamAPIActivated>(OnGameServerSteamAPIActivated);
        if (Config.OtherSettings.UserMessageGagChatType)
        {
            UnhookUserMessage(118, HookUmChat);
        }

        RemoveCommandListener(null!, ComamndListenerHandler, HookMode.Pre);
        // AddCommandListener("callvote", OnCommandCallVote);
        // AddCommandListener("say", OnCommandSay);
        // AddCommandListener("say_team", OnCommandTeamSay);
    }


    // private HookResult OnCommandCallVote(CCSPlayerController? caller, CommandInfo info)
    // {
    //     var voteType = info.GetArg(1).ToLower();
    //     
    //     if (voteType != "kick")
    //         return HookResult.Continue;
    //
    //     var target = int.TryParse(info.GetArg(2), out var userId) 
    //         ? Utilities.GetPlayerFromUserid(userId) 
    //         : null;
    //     
    //     if (target == null || !target.IsValid || target.Connected != PlayerConnectedState.PlayerConnected)
    //         return HookResult.Continue;
    //
    //     return !AdminManager.CanPlayerTarget(caller, target) ? HookResult.Stop : HookResult.Continue;
    // }

    private void OnGameServerSteamAPIActivated()
    {
        if (ServerLoaded || _serverLoading)
        {
            return;
        }

        _serverLoading = true;
        new ServerManager().LoadServerData();
    }

    [GameEventHandler]
    public HookResult OnClientDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Reason is 149 or 6)
        {
            info.DontBroadcast = true;
        }

        CCSPlayerController? player = @event.Userid;

#if DEBUG
        Logger.LogCritical("[OnClientDisconnect] Before");
#endif

        if (player == null || !player.IsValid || player.IsHLTV)
        {
            return HookResult.Continue;
        }

        CachedPlayers.Remove(player);
        BotPlayers.Remove(player);
        SilentPlayers.Remove(player.Slot);

        if (player.IsBot)
        {
            return HookResult.Continue;
        }

#if DEBUG
        Logger.LogCritical("[OnClientDisconnect] After Check");
#endif
        try
        {
            if (DisconnectedPlayers.Count >= Config.OtherSettings.DisconnectedPlayersHistoryCount)
            {
                DisconnectedPlayers.RemoveAt(0);
            }

            SteamID steamId = new SteamID(player.SteamID);
            DisconnectedPlayer? disconnectedPlayer = DisconnectedPlayers.FirstOrDefault(p => p.SteamId == steamId);

            if (disconnectedPlayer != null)
            {
                disconnectedPlayer.Name = player.PlayerName;
                disconnectedPlayer.IpAddress = player.IpAddress?.Split(":")[0];
                disconnectedPlayer.DisconnectTime = Time.ActualDateTime();
            }
            else
            {
                DisconnectedPlayers.Add(new DisconnectedPlayer(steamId, player.PlayerName,
                    player.IpAddress?.Split(":")[0], Time.ActualDateTime()));
            }

            PlayerPenaltyManager.RemoveAllPenalties(player.Slot);

            if (player.UserId.HasValue)
            {
                PlayersInfo.TryRemove(player.SteamID, out _);
            }

            if (!PermissionManager.AdminCache.TryGetValue(steamId, out (DateTime? ExpirationTime, List<string> Flags) data)
                || !(data.ExpirationTime <= Time.ActualDateTime()))
            {
                return HookResult.Continue;
            }

            AdminManager.RemovePlayerPermissions(steamId, PermissionManager.AdminCache[steamId].Flags.ToArray());
            AdminManager.RemovePlayerFromGroup(steamId, true, PermissionManager.AdminCache[steamId].Flags.ToArray());
            AdminData? adminData = AdminManager.GetPlayerAdminData(steamId);

            if (adminData == null || data.Flags.ToList().Count != 0 && adminData.Groups.ToList().Count != 0)
            {
                return HookResult.Continue;
            }

            AdminManager.ClearPlayerPermissions(steamId);
            AdminManager.RemovePlayerAdminData(steamId);

            return HookResult.Continue;
        }
        catch (Exception ex)
        {
            Logger.LogError($"An error occurred in OnClientDisconnect: {ex.Message}");
            return HookResult.Continue;
        }
    }

    private void OnClientConnect(int playerslot, string name, string ipAddress)
    {
#if DEBUG
        Logger.LogCritical("[OnClientConnect]");
#endif

        CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerslot);
        if (player == null || !player.IsValid || player.IsBot)
        {
            return;
        }

        PlayerManager.LoadPlayerData(player);
    }

    private void OnClientConnected(int playerslot)
    {
#if DEBUG
        Logger.LogCritical("[OnClientConnected]");
#endif

        CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerslot);
        if (player == null || !player.IsValid || player.IsBot)
        {
            return;
        }

        if (!CachedPlayers.Contains(player))
        {
            CachedPlayers.Add(player);
        }

        PlayerManager.LoadPlayerData(player);
    }

    //     private void OnClientConnect(int playerslot, string name, string ipaddress)
    //     {
    // #if DEBUG
    //         Logger.LogCritical("[OnClientConnect]");
    // #endif
    //         if (Config.OtherSettings.BanType == 0)
    //             return;
    //         
    //         if (Instance.CacheManager != null && !Instance.CacheManager.IsPlayerBanned(null, ipaddress.Split(":")[0]))
    //                 return;
    //         
    //         var testPlayer = Utilities.GetPlayerFromSlot(playerslot);
    //         if (testPlayer == null)
    //             return;
    //         Logger.LogInformation($"Gracz {testPlayer.PlayerName} ({testPlayer.SteamID.ToString()}) Czas: {DateTime.Now}");
    //
    //         Server.NextFrame((() =>
    //         {
    //             var player = Utilities.GetPlayerFromSlot(playerslot);
    //             if (player == null || !player.IsValid || player.IsBot)
    //                 return;
    //
    //             Helper.KickPlayer(player, NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
    //         }));
    //         
    //         // Server.NextFrame(() =>
    //         // {
    //         //     var player = Utilities.GetPlayerFromSlot(playerslot);
    //         //
    //         //     if (player == null || !player.IsValid || player.IsBot)
    //         //         return;
    //         //
    //         //     new PlayerManager().LoadPlayerData(player);
    //         // });
    //     }

    [GameEventHandler]
    public HookResult OnPlayerFullConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
#if DEBUG
        Logger.LogCritical("[OnPlayerFullConnect]");
#endif

        CCSPlayerController? player = @event.Userid;

        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        if (player is { IsBot: true, IsHLTV: false })
        {
            BotPlayers.Add(player);
            return HookResult.Continue;
        }

        PlayerManager.LoadPlayerData(player, true);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
#if DEBUG
        Logger.LogCritical("[OnRoundStart]");
#endif

        foreach (PlayerInfo player in PlayersInfo.Values)
        {
            player.DiePosition = null;
        }

        AddTimer(0.5f, () =>
        {
            foreach (KeyValuePair<ulong, string> list in RenamedPlayers)
            {
                CCSPlayerController? player = Utilities.GetPlayerFromSteamId(list.Key);

                if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.Connected)
                {
                    continue;
                }

                if (player.PlayerName.Equals(list.Value))
                {
                    continue;
                }

                player.Rename(list.Value);
            }
        });

        return HookResult.Continue;
    }

    private HookResult HookUmChat(UserMessage um)
    {
        CCSPlayerController? author = Utilities.GetPlayerFromIndex(um.ReadInt("entityindex"));
        if (author == null || !author.IsValid || author.IsBot)
        {
            return HookResult.Continue;
        }

        if (!PlayerPenaltyManager.IsPenalized(author.Slot, PenaltyType.Gag, out DateTime? endDateTime) &&
            !PlayerPenaltyManager.IsPenalized(author.Slot, PenaltyType.Silence, out endDateTime))
        {
            return HookResult.Continue;
        }

        if (_localizer == null || endDateTime == null)
        {
            return HookResult.Continue;
        }

        string message = um.ReadString("param2");
        IEnumerable<string> triggers = CoreConfig.PublicChatTrigger.Concat(CoreConfig.SilentChatTrigger);
        if (!triggers.Any(trigger => message.StartsWith(trigger)))
        {
            return HookResult.Stop;
        }

        for (int i = um.Recipients.Count - 1; i >= 0; i--)
        {
            if (um.Recipients[i] != author)
            {
                um.Recipients.RemoveAt(i);
            }
        }

        return HookResult.Continue;

        // author.SendLocalizedMessage(_localizer, "sa_player_penalty_chat_active", endDateTime.Value.ToString("g", author.GetLanguage()));
    }

    private HookResult ComamndListenerHandler(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot)
        {
            return HookResult.Continue;
        }

        string command = info.GetArg(0).ToLower();

        if (Config.OtherSettings.AdditionalCommandsToLog.Contains(command))
        {
            Helper.LogCommand(player, info);
        }

        switch (command)
        {
            case "css_admins_reload":
                AddTimer(1.0f, () => ReloadAdmins(null));
                return HookResult.Continue;
            case "callvote":
                {
                    string voteType = info.GetArg(1).ToLower();

                    if (voteType != "kick")
                    {
                        return HookResult.Continue;
                    }

                    CCSPlayerController? target;

                    if (int.TryParse(info.GetArg(2), out int userId))
                    {
                        target = Utilities.GetPlayerFromUserid(userId);
                    }
                    else
                    {
                        target = null;
                    }

                    if (target == null || !target.IsValid || target.Connected != PlayerConnectedState.Connected)
                    {
                        return HookResult.Continue;
                    }

                    if (!player.CanTarget(target))
                    {
                        return HookResult.Stop;
                    }

                    return HookResult.Continue;
                }
        }

        if (!command.Contains("say"))
        {
            return HookResult.Continue;
        }

        if (info.GetArg(1).Length == 0)
        {
            return HookResult.Stop;
        }

        IEnumerable<string> triggers = CoreConfig.PublicChatTrigger.Concat(CoreConfig.SilentChatTrigger);
        if (triggers.Any(trigger => info.GetArg(1).StartsWith(trigger)))
        {
            return HookResult.Continue;
        }

        // if (!Config.OtherSettings.UserMessageGagChatType)
        // {
        if (PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Gag, out DateTime? endDateTime) ||
            PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Silence, out endDateTime))
        {
            if (_localizer != null && endDateTime is not null)
            {
                player.SendLocalizedMessage(_localizer, "sa_player_penalty_chat_active", endDateTime.Value.ToString("g", player.GetLanguage()));
            }

            return HookResult.Stop;
        }
        // }

        if (AdminManager.PlayerHasPermissions(new SteamID(player.SteamID), "@css/chat") && command == "say" && info.GetArg(1).StartsWith($"@"))
        {
            player.ExecuteClientCommandFromServer($"css_say {info.GetArg(1).Remove(0, 1)}");
            return HookResult.Stop;
        }

        if (command != "say_team" || !info.GetArg(1).StartsWith($"@"))
        {
            return HookResult.Continue;
        }

        StringBuilder sb = new();
        if (AdminManager.PlayerHasPermissions(new SteamID(player.SteamID), "@css/chat"))
        {
            sb.Append(_localizer!["sa_adminchat_template_admin", player.PlayerName, info.GetArg(1).Remove(0, 1)]);
            foreach (CCSPlayerController? p in Utilities.GetPlayers().Where(p => p.IsValid && p is { IsBot: false, IsHLTV: false } && AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat")))
            {
                p.PrintToChat(sb.ToString());
            }
        }
        else
        {
            sb.Append(_localizer!["sa_adminchat_template_player", player.PlayerName, info.GetArg(1).Remove(0, 1)]);
            player.PrintToChat(sb.ToString());
            foreach (CCSPlayerController? p in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false } && AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat")))
            {
                p.PrintToChat(sb.ToString());
            }
        }

        return HookResult.Stop;
    }

    /*public HookResult OnCommandSay(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null ||  !player.IsValid || player.IsBot)
			return HookResult.Continue;

		if (info.GetArg(1).StartsWith($"/")
			|| info.GetArg(1).StartsWith($"!"))
			return HookResult.Continue;

		if (info.GetArg(1).Length == 0)
			return HookResult.Handled;

		if (PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Gag) || PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Silence))
			return HookResult.Handled;

		return HookResult.Continue;
	}*/

    public HookResult OnCommandTeamSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot)
        {
            return HookResult.Continue;
        }

        if (info.GetArg(1).StartsWith($"/")
            || info.GetArg(1).StartsWith($"!"))
        {
            return HookResult.Continue;
        }

        if (info.GetArg(1).Length == 0)
        {
            return HookResult.Handled;
        }

        if (PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Gag, out _) || PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Silence, out _))
        {
            return HookResult.Stop;
        }

        if (!info.GetArg(1).StartsWith($"@"))
        {
            return HookResult.Continue;
        }

        StringBuilder sb = new();

        if (AdminManager.PlayerHasPermissions(new SteamID(player.SteamID), "@css/chat"))
        {
            sb.Append(_localizer!["sa_adminchat_template_admin", player.PlayerName, info.GetArg(1).Remove(0, 1)]);
            foreach (CCSPlayerController? p in Utilities.GetPlayers().Where(p => p.IsValid && p is { IsBot: false, IsHLTV: false } && AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat")))
            {
                p.PrintToChat(sb.ToString());
            }
        }
        else
        {
            sb.Append(_localizer!["sa_adminchat_template_player", player.PlayerName, info.GetArg(1).Remove(0, 1)]);
            player.PrintToChat(sb.ToString());
            foreach (CCSPlayerController? p in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false } && AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat")))
            {
                p.PrintToChat(sb.ToString());
            }
        }

        return HookResult.Handled;
    }

    private void OnMapStart(string mapName)
    {
        if (Config.OtherSettings.ReloadAdminsEveryMapChange && ServerLoaded && ServerId != null)
        {
            ReloadAdmins(null);
        }

        AddTimer(1.0f, ServerManager.CheckHibernationStatus);

        if (!ServerLoaded || ServerId == null)
        {
            AddTimer(1.5f, OnGameServerSteamAPIActivated);
        }

        // AddTimer(34, () =>
        // {
        //     if (!ServerLoaded)
        //         OnGameServerSteamAPIActivated();
        // });

        SilentPlayers.Clear();

        PlayerPenaltyManager.RemoveAllPenalties();
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;

        if (player?.UserId == null || !player.IsValid || player.IsHLTV ||
            player.Connected != PlayerConnectedState.Connected || !PlayersInfo.ContainsKey(player.SteamID) ||
            @event.Attacker == null)
        {
            return HookResult.Continue;
        }

        CounterStrikeSharp.API.Modules.Utils.Vector? playerPosition = player.PlayerPawn.Value?.AbsOrigin;
        QAngle? playerRotation = player.PlayerPawn.Value?.AbsRotation;
        PlayersInfo[player.SteamID].DiePosition = new DiePosition(
            new Vector3(
                playerPosition?.X ?? 0,
                playerPosition?.Y ?? 0,
                playerPosition?.Z ?? 0
            ),
            new Vector3(
                playerRotation?.X ?? 0,
                playerRotation?.Y ?? 0,
                playerRotation?.Z ?? 0
            )
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || !SilentPlayers.Contains(player.Slot))
        {
            return HookResult.Continue;
        }

        if (@event is not { Oldteam: <= 1, Team: >= 1 })
        {
            return HookResult.Continue;
        }

        SilentPlayers.Remove(player.Slot);
        SimpleAdminApi?.OnAdminToggleSilentEvent(player.Slot, false);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerInfo(EventPlayerInfo @event, GameEventInfo _)
    {
        EnforceRename(@event.Userid);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerChangename(EventPlayerChangename @event, GameEventInfo _)
    {
        EnforceRename(@event.Userid);
        return HookResult.Continue;
    }

    /// <summary>
    /// Re-applies a permanent rename. Checked after a short delay because the engine may not
    /// have written the client's new name yet when the event fires, and Rename itself
    /// takes 0.4s to settle (it briefly writes the name with a trailing space).
    /// </summary>
    private void EnforceRename(CCSPlayerController? player)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            return;
        }

        if (!RenamedPlayers.TryGetValue(player.SteamID, out string? name))
        {
            return;
        }

        AddTimer(0.5f, () =>
        {
            if (player.IsValid && !player.PlayerName.Equals(name))
            {
                player.Rename(name);
            }
        });
    }
}
