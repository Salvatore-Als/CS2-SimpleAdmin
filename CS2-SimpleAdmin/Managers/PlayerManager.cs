using System.Data.Common;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using CS2_SimpleAdminApi;
using Dapper;
using Microsoft.Extensions.Logging;
using ZLinq;

namespace CS2_SimpleAdmin.Managers;

internal class PlayerManager
{
    private readonly SemaphoreSlim _loadPlayerSemaphore = new(6);
    private readonly CS2_SimpleAdminConfig _config = CS2_SimpleAdmin.Instance.Config;

    /// <summary>
    /// Loads and initializes player data when a client connects.
    /// </summary>
    /// <param name="player">The <see cref="CCSPlayerController"/> instance representing the connecting player.</param>
    /// <param name="fullConnect">
    /// Determines whether to perform a full synchronization of player data.
    /// If true, full checks (bans, IP history, penalties, warns, mutes) will be loaded and applied.
    /// </param>
    /// <remarks>
    /// This method validates the player's identity, checks for bans, updates the IP history table,
    /// loads penalties (mutes/gags/warns), and optionally notifies admin players about the connecting player's penalties.
    /// </remarks>
    public void LoadPlayerData(CCSPlayerController player, bool fullConnect = false)
    {
        if (!player.UserId.HasValue)
        {
            Helper.KickPlayer(player, NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_INVALIDCONNECTION);
            return;
        }

        int userId = player.UserId.Value;
        int slot = player.Slot;
        ulong steamId = player.SteamID;
        string playerName;

        if (!string.IsNullOrEmpty(player.PlayerName))
        {
            playerName = player.PlayerName;
        }
        else
        {
            playerName = CS2_SimpleAdmin._localizer?["sa_unknown"] ?? "Unknown";
        }
        string? ipAddress = player.IpAddress?.Split(":")[0];

        if (CS2_SimpleAdmin.RenamedPlayers.TryGetValue(steamId, out string? renamedTo))
        {
            player.Rename(renamedTo);
        }

        if (CS2_SimpleAdmin.DatabaseProvider == null || CS2_SimpleAdmin.Instance.CacheManager == null)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await _loadPlayerSemaphore.WaitAsync();

                // Save ip address before ban check
                await SavePlayerIpAddress(steamId, playerName, ipAddress);

                // Always check bans first, regardless of PlayersInfo state
                bool isBanned = IsBanned(CS2_SimpleAdmin.Instance.CacheManager, CS2_SimpleAdmin.Instance.Config.OtherSettings.BanType,
                    CS2_SimpleAdmin.Instance.Config.OtherSettings.CheckMultiAccountsByIp, playerName, steamId, ipAddress);

                CS2_SimpleAdmin._logger?.LogInformation($"[BAN CHECK] Player {playerName} ({steamId}) IP: {ipAddress} - BanType: {CS2_SimpleAdmin.Instance.Config.OtherSettings.BanType} - CheckMultiAccounts: {CS2_SimpleAdmin.Instance.Config.OtherSettings.CheckMultiAccountsByIp} - isBanned: {isBanned}");

                if (isBanned)
                {
                    CS2_SimpleAdmin._logger?.LogInformation($"[BAN CHECK] KICKING {playerName} ({steamId})");
                    await Server.NextWorldUpdateAsync(() =>
                    {
                        CS2_SimpleAdmin._logger?.LogInformation($"[BAN CHECK] Executing kick for {playerName}");
                        Helper.KickPlayer(userId, NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
                    });

                    return;
                }

                if (!CS2_SimpleAdmin.PlayersInfo.ContainsKey(steamId))
                {
                    PlayerInfo playerInfo = new PlayerInfo(userId, slot, new SteamID(steamId), playerName, ipAddress);
                    CS2_SimpleAdmin.PlayersInfo[steamId] = playerInfo;

                    if (_config.OtherSettings.CheckMultiAccountsByIp && ipAddress != null)
                    {
                        playerInfo.AccountsAssociated =
                            CS2_SimpleAdmin.Instance.CacheManager?.GetAccountsByIp(ipAddress).AsValueEnumerable()
                                .Select(x => (x.SteamId, x.PlayerName)).ToList() ?? [];
                    }

                    try
                    {
                        // var isBanned = CS2_SimpleAdmin.Instance.Config.OtherSettings.BanType == 0
                        //     ? CS2_SimpleAdmin.Instance.CacheManager.IsPlayerBanned(
                        //         CS2_SimpleAdmin.PlayersInfo[userId].SteamId.SteamId64.ToString(), null)
                        //     : CS2_SimpleAdmin.Instance.Config.OtherSettings.CheckMultiAccountsByIp
                        //         ? CS2_SimpleAdmin.Instance.CacheManager.IsPlayerOrAnyIpBanned(CS2_SimpleAdmin
                        //             .PlayersInfo[userId].SteamId.SteamId64)
                        //         : CS2_SimpleAdmin.Instance.CacheManager.IsPlayerBanned(CS2_SimpleAdmin.PlayersInfo[userId].SteamId.SteamId64.ToString(), ipAddress);

                        if (CS2_SimpleAdmin.PlayersInfo.TryGetValue(steamId, out PlayerInfo? value)) // Temp skip
                        {
                            List<dynamic> warns = await CS2_SimpleAdmin.Instance.WarnManager.GetPlayerWarns(value, false);
                            (int totalMutes, int totalGags, int totalSilences) =
                                await CS2_SimpleAdmin.Instance.MuteManager.GetPlayerMutes(value);
                            value.TotalBans = CS2_SimpleAdmin.Instance.CacheManager
                                ?.GetPlayerBansBySteamId(value.SteamId.SteamId64)
                                .Count ?? 0;
                            value.TotalMutes = totalMutes;
                            value.TotalGags = totalGags;
                            value.TotalSilences = totalSilences;
                            value.TotalWarns = warns.Count;

                            List<dynamic> activeMutes =
                                await CS2_SimpleAdmin.Instance.MuteManager.IsPlayerMuted(value.SteamId.SteamId64
                                    .ToString());

                            if (activeMutes.Count > 0)
                            {
                                foreach (dynamic mute in activeMutes)
                                {
                                    string muteType = mute.type;
                                    DateTime ends = mute.ends;
                                    int duration = mute.duration;
                                    switch (muteType)
                                    {
                                        // Apply mute penalty based on mute type
                                        case "GAG":
                                            PlayerPenaltyManager.AddPenalty(
                                                CS2_SimpleAdmin.PlayersInfo[steamId].Slot,
                                                PenaltyType.Gag, ends, duration);
                                            // if (CS2_SimpleAdmin._localizer != null)
                                            // 	mutesList[PenaltyType.Gag].Add(CS2_SimpleAdmin._localizer["sa_player_penalty_info_active_gag", ends.ToLocalTime().ToString(CultureInfo.CurrentCulture)]);
                                            break;
                                        case "MUTE":
                                            PlayerPenaltyManager.AddPenalty(
                                                CS2_SimpleAdmin.PlayersInfo[steamId].Slot,
                                                PenaltyType.Mute, ends, duration);
                                            await Server.NextWorldUpdateAsync(() =>
                                            {
                                                player.VoiceFlags = VoiceFlags.Muted;
                                            });
                                            // if (CS2_SimpleAdmin._localizer != null)
                                            // 	mutesList[PenaltyType.Mute].Add(CS2_SimpleAdmin._localizer["sa_player_penalty_info_active_mute", ends.ToLocalTime().ToString(CultureInfo.InvariantCulture)]);
                                            break;
                                        default:
                                            PlayerPenaltyManager.AddPenalty(
                                                CS2_SimpleAdmin.PlayersInfo[steamId].Slot,
                                                PenaltyType.Silence, ends, duration);
                                            await Server.NextWorldUpdateAsync(() =>
                                            {
                                                player.VoiceFlags = VoiceFlags.Muted;
                                            });
                                            // if (CS2_SimpleAdmin._localizer != null)
                                            // 	mutesList[PenaltyType.Silence].Add(CS2_SimpleAdmin._localizer["sa_player_penalty_info_active_silence", ends.ToLocalTime().ToString(CultureInfo.CurrentCulture)]);
                                            break;
                                    }
                                }
                            }

                            if (CS2_SimpleAdmin.Instance.Config.OtherSettings.NotifyPenaltiesToAdminOnConnect)
                            {
                                await Server.NextWorldUpdateAsync(() =>
                                {
                                    foreach (CCSPlayerController? admin in Helper.GetValidPlayers()
                                                 .Where(p => (AdminManager.PlayerHasPermissions(
                                                                  new SteamID(p.SteamID),
                                                                  "@css/kick") ||
                                                              AdminManager.PlayerHasPermissions(
                                                                  new SteamID(p.SteamID),
                                                                  "@css/ban")) &&
                                                             p.Connected == PlayerConnectedState.Connected &&
                                                             !CS2_SimpleAdmin.AdminDisabledJoinComms
                                                                 .Contains(p.SteamID)))
                                    {
                                        if (CS2_SimpleAdmin._localizer == null || admin == player)
                                        {
                                            continue;
                                        }

                                        admin.SendLocalizedMessage(CS2_SimpleAdmin._localizer,
                                            "sa_admin_penalty_info",
                                            player.PlayerName,
                                            CS2_SimpleAdmin.PlayersInfo[steamId].TotalBans,
                                            CS2_SimpleAdmin.PlayersInfo[steamId].TotalGags,
                                            CS2_SimpleAdmin.PlayersInfo[steamId].TotalMutes,
                                            CS2_SimpleAdmin.PlayersInfo[steamId].TotalSilences,
                                            CS2_SimpleAdmin.PlayersInfo[steamId].TotalWarns
                                        );

                                        if (CS2_SimpleAdmin.PlayersInfo[steamId].AccountsAssociated.Count >= 2)
                                        {
                                            List<IEnumerable<(ulong SteamId, string PlayerName)>> associatedAcccountsChunks =
                                                CS2_SimpleAdmin.PlayersInfo[steamId].AccountsAssociated.ChunkBy(5)
                                                    .ToList();
                                            foreach (IEnumerable<(ulong SteamId, string PlayerName)> chunk in associatedAcccountsChunks)
                                            {
                                                admin.SendLocalizedMessage(CS2_SimpleAdmin._localizer,
                                                    "sa_admin_associated_accounts",
                                                    player.PlayerName,
                                                    string.Join(", ",
                                                        chunk.Select(a => $"{a.PlayerName} ({a.SteamId})"))
                                                );
                                            }
                                        }
                                    }
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CS2_SimpleAdmin._logger?.LogError("Error processing player connection: {exception}",
                            ex.Message);
                    }
                }
            }
            finally
            {
                _loadPlayerSemaphore.Release();
            }
        });
    }

    /// <summary>
    /// Returns every ban, gag/mute/silence and warn ever recorded for a SteamID, newest first.
    /// Rows carry: type, status, reason, duration, created, ends, admin_name, lift_reason, lift_date, lift_admin.
    /// </summary>
    /// <param name="steamId">SteamID64 of the player, online or not.</param>
    /// <param name="type">Optional filter: bans, gags, mutes, silences or warns.</param>
    public async Task<List<dynamic>> GetPenaltyHistory(ulong steamId, string? type = null)
    {
        if (CS2_SimpleAdmin.DatabaseProvider == null)
        {
            return [];
        }

        try
        {
            await using DbConnection connection = await CS2_SimpleAdmin.DatabaseProvider.CreateConnectionAsync();
            string sql = CS2_SimpleAdmin.DatabaseProvider.GetPenaltyHistoryQuery(CS2_SimpleAdmin.Instance.Config.MultiServerMode);
            List<dynamic> rows = (await connection.QueryAsync(sql, new { PlayerSteamID = steamId, serverid = CS2_SimpleAdmin.ServerId })).ToList();

            // Filter word is the plural (bans, gags, mutes, silences, warns); rows carry BAN/GAG/MUTE/SILENCE/WARN
            if (type != null)
            {
                rows.RemoveAll(r => !type.Equals((string)r.type + "s", StringComparison.OrdinalIgnoreCase));
            }

            return rows;
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError("Unable to load penalty history: {exception}", ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Dapper gives DateTime on MySQL but can give a string on SQLite; accept both.
    /// </summary>
    internal static DateTime? ToDateTime(object? value) =>
        value switch
        {
            DateTime d => d,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) => parsed,
            _ => null
        };

    /// <summary>
    /// Loads permanent renames from the database into memory.
    /// </summary>
    public async Task LoadRenamedPlayers()
    {
        if (CS2_SimpleAdmin.DatabaseProvider == null)
        {
            return;
        }

        try
        {
            await using DbConnection connection = await CS2_SimpleAdmin.DatabaseProvider.CreateConnectionAsync();
            IEnumerable<(long steamId, string name)> rows = await connection.QueryAsync<(long steamId, string name)>(CS2_SimpleAdmin.DatabaseProvider.GetRenamesQuery());

            await Server.NextWorldUpdateAsync(() =>
            {
                foreach ((long steamId, string? name) in rows)
                {
                    CS2_SimpleAdmin.RenamedPlayers[(ulong)steamId] = name;
                }
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError("Unable to load renames: {exception}", ex.Message);
        }
    }

    /// <summary>
    /// Persists or removes a permanent rename (css_prename) for a player.
    /// </summary>
    /// <param name="steamId">SteamID64 of the player.</param>
    /// <param name="name">Forced name, or null/empty to remove the rename.</param>
    public async Task SaveRenamedPlayer(ulong steamId, string? name)
    {
        if (CS2_SimpleAdmin.DatabaseProvider == null)
        {
            return;
        }

        try
        {
            await using DbConnection connection = await CS2_SimpleAdmin.DatabaseProvider.CreateConnectionAsync();
            if (string.IsNullOrEmpty(name))
            {
                await connection.ExecuteAsync(CS2_SimpleAdmin.DatabaseProvider.GetDeleteRenameQuery(), new { steamId });
            }
            else
            {
                await connection.ExecuteAsync(CS2_SimpleAdmin.DatabaseProvider.GetUpsertRenameQuery(), new { steamId, name });
            }
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError("Unable to save rename: {exception}", ex.Message);
        }
    }

    /// <summary>
    /// Saves player's IP address to the database for multi-account detection.
    /// This is called before ban checks to ensure IP is recorded even if player is banned.
    /// </summary>
    private async Task SavePlayerIpAddress(ulong steamId, string playerName, string? ipAddress)
    {
        if (!_config.OtherSettings.CheckMultiAccountsByIp || ipAddress == null || CS2_SimpleAdmin.DatabaseProvider == null)
        {
            return;
        }

        try
        {
            await using DbConnection connection = await CS2_SimpleAdmin.DatabaseProvider.CreateConnectionAsync();

            ulong steamId64 = steamId;
            uint ipUint = IpHelper.IpToUint(ipAddress);

            string upsertQuery = CS2_SimpleAdmin.DatabaseProvider.GetUpsertPlayerIpQuery();

            await connection.ExecuteAsync(upsertQuery, new
            {
                SteamID = steamId64,
                playerName,
                IPAddress = ipUint
            });
        }
        catch (Exception ex)
        {
            CS2_SimpleAdmin._logger?.LogError(
                $"Unable to save ip address for {playerName} ({ipAddress}): {ex.Message}");
        }
    }

    /// <summary>
    /// Periodically checks the status of online players and applies timers for speed, gravity,
    /// and penalty expiration validation.
    /// </summary>
    /// <remarks>
    /// This method registers two repeating timers:
    /// <list type="bullet">
    ///   <item><description>One short-interval timer to update speed/gravity modifications applied to players.</description></item>
    ///   <item><description>
    ///   One long-interval timer (default 61 seconds) to expire bans, mutes, warns, refresh caches,
    ///   and remove outdated penalties from connected players.
    ///   </description></item>
    /// </list>
    /// Additionally, banned players still online are kicked, and admins may be updated about mute statuses based on the configured time mode.
    /// </remarks>
    public void CheckPlayersTimer()
    {
        CS2_SimpleAdmin.Instance.AddTimer(5f, () =>
        {
            foreach ((ulong steamid, string? name) in CS2_SimpleAdmin.RenamedPlayers)
            {
                CCSPlayerController? player = Helper.GetPlayerFromSteamid64(steamid);
                if (player == null || !player.IsValid || player.PlayerName == name)
                {
                    continue;
                }

                player.Rename(name);
            }
        }, TimerFlags.REPEAT);

        CS2_SimpleAdmin.Instance.PlayersTimer = CS2_SimpleAdmin.Instance.AddTimer(61.0f, () =>
        {
#if DEBUG
            CS2_SimpleAdmin._logger?.LogCritical("[OnMapStart] Expired check");
#endif
            if (CS2_SimpleAdmin.DatabaseProvider == null)
            {
                return;
            }

            // Optimization: Get players once and avoid allocating anonymous types
            List<CCSPlayerController> validPlayers = Helper.GetValidPlayers();
            // Use ValueTuple instead of anonymous type - better performance and less allocations
            List<(string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot)> tempPlayers = new List<(string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot)>(validPlayers.Count);
            foreach (CCSPlayerController p in validPlayers)
            {
                tempPlayers.Add((p.PlayerName, p.SteamID, p.IpAddress, p.UserId, p.Slot));
            }

            CS2_SimpleAdmin pluginInstance = CS2_SimpleAdmin.Instance;
            OtherSettings config = _config.OtherSettings; // Cache config access

            _ = Task.Run(async () =>
            {
                try
                {
                    // Run all expire tasks in parallel
                    Task[] expireTasks = new[]
                    {
                        pluginInstance.BanManager.ExpireOldBans(),
                        pluginInstance.MuteManager.ExpireOldMutes(),
                        pluginInstance.WarnManager.ExpireOldWarns(),
                        pluginInstance.CacheManager?.RefreshCacheAsync() ?? Task.CompletedTask,
                        pluginInstance.PermissionManager.DeleteOldAdmins()
                    };

                    await Task.WhenAll(expireTasks);
                }
                catch (Exception ex)
                {
                    CS2_SimpleAdmin._logger?.LogError($"Error processing players timer tasks: {ex.Message}");

                    if (ex is AggregateException aggregate)
                    {
                        foreach (Exception inner in aggregate.InnerExceptions)
                        {
                            CS2_SimpleAdmin._logger?.LogError($"Inner exception: {inner.Message}");
                        }
                    }
                }

                if (pluginInstance.CacheManager == null)
                {
                    return;
                }

                // Optimization: Cache ban type and multi-account check to avoid repeated config access
                int banType = config.BanType;
                bool checkMultiAccounts = config.CheckMultiAccountsByIp;

                List<(string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot)> bannedPlayers = new List<(string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot)>();

                // Manual loop instead of LINQ - better performance
                foreach ((string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot) player in tempPlayers)
                {
                    string playerName = player.PlayerName;
                    ulong steamId = player.SteamID;
                    string? ip = player.IpAddress?.Split(':')[0];

                    bool isBanned = IsBanned(pluginInstance.CacheManager, banType, checkMultiAccounts, playerName, steamId, ip);

                    if (isBanned)
                    {
                        bannedPlayers.Add(player);
                    }
                }

                if (bannedPlayers.Count > 0)
                {
                    foreach ((string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot) player in bannedPlayers)
                    {
                        if (!player.UserId.HasValue)
                        {
                            continue;
                        }

                        await Server.NextWorldUpdateAsync(() =>
                        {
                            if (Helper.GetPlayerFromSteamid64(player.SteamID) != null)
                            {
                                Helper.KickPlayer((int)player.UserId,
                                    NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
                            }
                        });
                    }
                }

                if (config.TimeMode == 0)
                {
                    // Optimization: Manual projection instead of LINQ
                    List<(ulong, int?, int)> onlinePlayers = new List<(ulong, int?, int)>(tempPlayers.Count);
                    foreach ((string PlayerName, ulong SteamID, string? IpAddress, int? UserId, int Slot) player in tempPlayers)
                    {
                        onlinePlayers.Add((player.SteamID, player.UserId, player.Slot));
                    }

                    if (onlinePlayers.Count > 0)
                    {
                        await pluginInstance.MuteManager.CheckOnlineModeMutes(onlinePlayers);
                    }
                }
            });

            try
            {
                // Optimization: Process penalties without LINQ allocations
                List<CCSPlayerController> players = Helper.GetValidPlayers();
                foreach (CCSPlayerController player in players)
                {
                    if (!PlayerPenaltyManager.IsSlotInPenalties(player.Slot))
                    {
                        continue;
                    }

                    bool isMuted = PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Mute, out _);
                    bool isSilenced = PlayerPenaltyManager.IsPenalized(player.Slot, PenaltyType.Silence, out _);

                    // Only reset voice flags if not muted or silenced
                    if (!isMuted && !isSilenced)
                    {
                        player.VoiceFlags = VoiceFlags.Normal;
                    }
                }

                PlayerPenaltyManager.RemoveExpiredPenalties();
            }
            catch (Exception ex)
            {
                CS2_SimpleAdmin._logger?.LogError($"Unable to remove old penalties: {ex.Message}");
            }
        }, TimerFlags.REPEAT);
    }

    /// <summary>
    /// BanType 0 checks the SteamID only; otherwise the IP too, and every IP the player used when multi-account checks are on.
    /// </summary>
    private static bool IsBanned(CacheManager cacheManager, int banType, bool checkMultiAccounts, string playerName, ulong steamId, string? ipAddress)
    {
        if (banType == 0)
        {
            return cacheManager.IsPlayerBanned(playerName, steamId, null);
        }

        if (checkMultiAccounts)
        {
            return cacheManager.IsPlayerOrAnyIpBanned(playerName, steamId, ipAddress);
        }

        return cacheManager.IsPlayerBanned(playerName, steamId, ipAddress);
    }
}
