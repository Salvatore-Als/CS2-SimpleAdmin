using System.Data.Common;
using System.Data.SQLite;

namespace CS2_SimpleAdmin.Database;

public class SqliteDatabaseProvider(string filePath) : IDatabaseProvider
{
    private readonly string _connectionString = $"Data Source={filePath}";

    public async Task<DbConnection> CreateConnectionAsync()
    {
        SQLiteConnection conn = new SQLiteConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<(bool Success, string? Exception)> CheckConnectionAsync()
    {
        try
        {
            await using DbConnection conn = await CreateConnectionAsync();
            await using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public Task DatabaseMigrationAsync()
    {
        Migration migration = new Migration(CS2_SimpleAdmin.Instance.ModuleDirectory + "/Database/Migrations/Sqlite");
        return migration.ExecuteMigrationsAsync();
    }

    public string GetBanSelectQuery(bool multiServer)
    {
        if (multiServer)
        {
            return """
              SELECT id AS Id,
                     player_name AS PlayerName,
                     player_steamid AS PlayerSteamId,
                     player_ip AS PlayerIp,
                     status AS Status
              FROM sa_bans
              """;
        }

        return """
          SELECT id AS Id,
                 player_name AS PlayerName,
                 player_steamid AS PlayerSteamId,
                 player_ip AS PlayerIp,
                 status AS Status
          FROM sa_bans
          WHERE server_id = @serverId
          """;
    }

    public static string GetBanUpdatedSelectQuery(bool multiServer)
    {
        if (multiServer)
        {
            return """
              SELECT id AS Id,
                     player_name AS PlayerName,
                     player_steamid AS PlayerSteamId,
                     player_ip AS PlayerIp,
                     status AS Status
              FROM sa_bans
              WHERE updated_at > @lastUpdate OR created > @lastUpdate
              ORDER BY updated_at DESC
              """;
        }

        return """
          SELECT id AS Id,
                 player_name AS PlayerName,
                 player_steamid AS PlayerSteamId,
                 player_ip AS PlayerIp,
                 status AS Status
          FROM sa_bans
          WHERE (updated_at > @lastUpdate OR created > @lastUpdate)
          AND server_id = @serverId
          ORDER BY updated_at DESC
          """;
    }

    public string GetIpHistoryQuery() =>
        "SELECT steamid, name, address, used_at FROM sa_players_ips ORDER BY used_at DESC";

    public string GetUpsertPlayerIpQuery()
    {
        return """
            INSERT INTO sa_players_ips (steamid, name, address, used_at)
            VALUES (@SteamID, @playerName, @IPAddress, CURRENT_TIMESTAMP)
            ON CONFLICT(steamid, address) DO UPDATE SET
                used_at = CURRENT_TIMESTAMP,
                name = @playerName;
            """;
    }

    public string GetBanUpdateQuery(bool multiServer)
    {
        if (multiServer)
        {
            return """
              UPDATE sa_bans
              SET player_ip   = COALESCE(player_ip, @PlayerIP),
                  player_name = COALESCE(player_name, @PlayerName)
              WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)
                AND status = 'ACTIVE'
                AND (duration = 0 OR ends > @CurrentTime)
              """;
        }

        return """
          UPDATE sa_bans
          SET player_ip   = COALESCE(player_ip, @PlayerIP),
              player_name = COALESCE(player_name, @PlayerName)
          WHERE (player_steamid = @PlayerSteamID OR player_ip = @PlayerIP)
            AND status = 'ACTIVE'
            AND (duration = 0 OR ends > @CurrentTime)
            AND server_id = @ServerId
          """;
    }

    public string GetAddBanQuery() =>
        """
        INSERT INTO sa_bans
            (player_steamid, player_name, player_ip, admin_steamid, admin_name, reason, duration, ends, created, server_id)
        VALUES
            (@playerSteamid, @playerName, @playerIp, @adminSteamid, @adminName, @banReason, @duration, @ends, @created, @serverid);
        SELECT last_insert_rowid();
        """;

    public string GetAddBanBySteamIdQuery() =>
        """
        INSERT INTO sa_bans
            (player_steamid, admin_steamid, admin_name, reason, duration, ends, created, server_id)
        VALUES
            (@playerSteamid, @adminSteamid, @adminName, @banReason, @duration, @ends, @created, @serverid);
        SELECT last_insert_rowid();
        """;

    public string GetAddBanByIpQuery() =>
        """
        INSERT INTO sa_bans
            (player_ip, admin_steamid, admin_name, reason, duration, ends, created, server_id)
        VALUES
            (@playerIp, @adminSteamid, @adminName, @banReason, @duration, @ends, @created, @serverid);
        SELECT last_insert_rowid();
        """;

    public string GetUnbanRetrieveBansQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "SELECT id, player_steamid FROM sa_bans WHERE (player_steamid = @pattern OR player_name = @pattern OR player_ip = @pattern) AND status = 'ACTIVE'";
        }

        return "SELECT id, player_steamid FROM sa_bans WHERE (player_steamid = @pattern OR player_name = @pattern OR player_ip = @pattern) AND status = 'ACTIVE' AND server_id = @serverid";
    }

    public string GetUnbanAdminIdQuery() =>
        "SELECT id FROM sa_admins WHERE player_steamid = @adminSteamId";

    public string GetInsertUnbanQuery(bool includeReason)
    {
        if (includeReason)
        {
            return "INSERT INTO sa_unbans (ban_id, admin_id, reason) VALUES (@banId, @adminId, @reason); SELECT last_insert_rowid();";
        }

        return "INSERT INTO sa_unbans (ban_id, admin_id) VALUES (@banId, @adminId); SELECT last_insert_rowid();";
    }

    // SQLite has no ON UPDATE CURRENT_TIMESTAMP, so updated_at must be set explicitly or the cache refresh never sees the change
    public string GetUpdateBanStatusQuery() =>
        "UPDATE sa_bans SET status = 'UNBANNED', unban_id = @unbanId, updated_at = CURRENT_TIMESTAMP WHERE id = @banId";

    public string GetExpireBansQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "UPDATE sa_bans SET status = 'EXPIRED', updated_at = CURRENT_TIMESTAMP WHERE status = 'ACTIVE' AND duration > 0 AND ends <= @currentTime";
        }

        return "UPDATE sa_bans SET status = 'EXPIRED', updated_at = CURRENT_TIMESTAMP WHERE status = 'ACTIVE' AND duration > 0 AND ends <= @currentTime AND server_id = @serverid";
    }

    public string GetExpireIpBansQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "UPDATE sa_bans SET player_ip = NULL WHERE status = 'ACTIVE' AND ends <= @ipBansTime";
        }

        return "UPDATE sa_bans SET player_ip = NULL WHERE status = 'ACTIVE' AND ends <= @ipBansTime AND server_id = @serverid";
    }

    public string GetExpireOldPlayerIpsQuery() =>
        "DELETE FROM sa_players_ips WHERE used_at <= @ipBansTime";

    public string GetRenamesQuery() =>
        "SELECT player_steamid, name FROM sa_renames";

    public string GetUpsertRenameQuery() =>
        "INSERT INTO sa_renames (player_steamid, name) VALUES (@steamId, @name) ON CONFLICT(player_steamid) DO UPDATE SET name = excluded.name";

    public string GetDeleteRenameQuery() =>
        "DELETE FROM sa_renames WHERE player_steamid = @steamId";

    public string GetAdminsQuery() =>
        """
        SELECT sa_admins.player_steamid, sa_admins.player_name, sa_admins_flags.flag, sa_admins.immunity, sa_admins.ends
        FROM sa_admins_flags
        JOIN sa_admins ON sa_admins_flags.admin_id = sa_admins.id
        WHERE (sa_admins.ends IS NULL OR sa_admins.ends > @CurrentTime)
          AND (sa_admins.server_id IS NULL OR sa_admins.server_id = @serverid)
        ORDER BY sa_admins.player_steamid
        """;

    public string GetDeleteAdminQuery(bool globalDelete)
    {
        if (globalDelete)
        {
            return "DELETE FROM sa_admins WHERE player_steamid = @PlayerSteamID";
        }

        return "DELETE FROM sa_admins WHERE player_steamid = @PlayerSteamID AND server_id = @ServerId";
    }

    public string GetAddAdminQuery() =>
        """
        INSERT INTO sa_admins (player_steamid, player_name, immunity, ends, created, server_id)
        VALUES (@playerSteamId, @playerName, @immunity, @ends, @created, @serverid);
        SELECT last_insert_rowid();
        """;

    public string GetGroupsQuery() =>
        """
        SELECT g.group_id, sg.name AS group_name, sg.immunity, f.flag
        FROM sa_groups_flags f
        JOIN sa_groups_servers g ON f.group_id = g.group_id
        JOIN sa_groups sg ON sg.id = g.group_id
        WHERE (g.server_id = @serverid OR server_id IS NULL)
        """;

    public string GetAddAdminFlagsQuery() =>
        "INSERT INTO sa_admins_flags (admin_id, flag) VALUES (@adminId, @flag);";

    public string GetUpdateAdminGroupQuery() =>
        "UPDATE sa_admins SET group_id = @groupId WHERE id = @adminId;";

    public string GetAddGroupQuery() =>
        """
        INSERT INTO sa_groups (name, immunity) VALUES (@groupName, @immunity);
        SELECT last_insert_rowid();
        """;

    public string GetGroupIdByNameQuery() =>
        """
        SELECT sgs.group_id
        FROM sa_groups_servers sgs
        JOIN sa_groups sg ON sgs.group_id = sg.id
        WHERE sg.name = @groupName
        ORDER BY (sgs.server_id = @serverId) DESC, sgs.server_id ASC
        LIMIT 1;
        """;

    public string GetAddGroupFlagsQuery() =>
        "INSERT INTO sa_groups_flags (group_id, flag) VALUES (@groupId, @flag);";

    public string GetAddGroupServerQuery() =>
        "INSERT INTO sa_groups_servers (group_id, server_id) VALUES (@groupId, @server_id);";

    public string GetDeleteGroupQuery() =>
        "DELETE FROM sa_groups WHERE name = @groupName;";

    public string GetDeleteOldAdminsQuery() =>
        "DELETE FROM sa_admins WHERE ends IS NOT NULL AND ends <= @CurrentTime;";

    public string GetAddMuteQuery(bool includePlayerName)
    {
        if (includePlayerName)
        {
            return """
              INSERT INTO sa_mutes
              (player_steamid, player_name, admin_steamid, admin_name, reason, duration, ends, created, type, server_id)
              VALUES (@playerSteamid, @playerName, @adminSteamid, @adminName, @muteReason, @duration, @ends, @created, @type, @serverid);
              SELECT last_insert_rowid();
              """;
        }

        return """
          INSERT INTO sa_mutes
          (player_steamid, admin_steamid, admin_name, reason, duration, ends, created, type, server_id)
          VALUES (@playerSteamid, @adminSteamid, @adminName, @muteReason, @duration, @ends, @created, @type, @serverid);
          SELECT last_insert_rowid();
          """;
    }

    public string GetIsMutedQuery(bool multiServer, int timeMode)
    {
        if (multiServer)
        {
            if (timeMode == 1)
            {
                return "SELECT * FROM sa_mutes WHERE player_steamid = @PlayerSteamID AND status = 'ACTIVE' AND (duration = 0 OR ends > @CurrentTime)";
            }
            else
            {
                return "SELECT * FROM sa_mutes WHERE player_steamid = @PlayerSteamID AND status = 'ACTIVE' AND (duration = 0 OR duration > COALESCE(passed, 0))";
            }
        }

        if (timeMode == 1)
        {
            return "SELECT * FROM sa_mutes WHERE player_steamid = @PlayerSteamID AND status = 'ACTIVE' AND (duration = 0 OR ends > @CurrentTime) AND server_id = @serverid";
        }

        return "SELECT * FROM sa_mutes WHERE player_steamid = @PlayerSteamID AND status = 'ACTIVE' AND (duration = 0 OR duration > COALESCE(passed, 0)) AND server_id = @serverid";
    }

    public string GetMuteStatsQuery(bool multiServer)
    {
        if (multiServer)
        {
            return """
              SELECT
                  COUNT(CASE WHEN type = 'MUTE' THEN 1 END) AS TotalMutes,
                  COUNT(CASE WHEN type = 'GAG' THEN 1 END) AS TotalGags,
                  COUNT(CASE WHEN type = 'SILENCE' THEN 1 END) AS TotalSilences
              FROM sa_mutes
              WHERE player_steamid = @PlayerSteamID;
              """;
        }

        return """
          SELECT
              COUNT(CASE WHEN type = 'MUTE' THEN 1 END) AS TotalMutes,
              COUNT(CASE WHEN type = 'GAG' THEN 1 END) AS TotalGags,
              COUNT(CASE WHEN type = 'SILENCE' THEN 1 END) AS TotalSilences
          FROM sa_mutes
          WHERE player_steamid = @PlayerSteamID AND server_id = @ServerId;
          """;
    }

    public string GetUpdateMutePassedQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "UPDATE sa_mutes SET passed = COALESCE(passed, 0) + 1 WHERE (player_steamid = @PlayerSteamID) AND duration > 0 AND status = 'ACTIVE'";
        }

        return "UPDATE sa_mutes SET passed = COALESCE(passed, 0) + 1 WHERE (player_steamid = @PlayerSteamID) AND duration > 0 AND status = 'ACTIVE' AND server_id = @serverid";
    }

    public string GetCheckExpiredMutesQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "SELECT * FROM sa_mutes WHERE player_steamid = @PlayerSteamID AND passed >= duration AND duration > 0 AND status = 'ACTIVE'";
        }

        return "SELECT * FROM sa_mutes WHERE player_steamid = @PlayerSteamID AND passed >= duration AND duration > 0 AND status = 'ACTIVE' AND server_id = @serverid";
    }

    public string GetRetrieveMutesQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "SELECT id FROM sa_mutes WHERE (player_steamid = @pattern OR player_name = @pattern) AND type = @muteType AND status = 'ACTIVE'";
        }

        return "SELECT id FROM sa_mutes WHERE (player_steamid = @pattern OR player_name = @pattern) AND type = @muteType AND status = 'ACTIVE' AND server_id = @serverid";
    }

    public string GetUnmuteAdminIdQuery() =>
        "SELECT id FROM sa_admins WHERE player_steamid = @adminSteamId";

    public string GetInsertUnmuteQuery(bool includeReason)
    {
        if (includeReason)
        {
            return "INSERT INTO sa_unmutes (mute_id, admin_id, reason) VALUES (@muteId, @adminId, @reason); SELECT last_insert_rowid();";
        }

        return "INSERT INTO sa_unmutes (mute_id, admin_id) VALUES (@muteId, @adminId); SELECT last_insert_rowid();";
    }

    public string GetUpdateMuteStatusQuery() =>
        "UPDATE sa_mutes SET status = 'UNMUTED', unmute_id = @unmuteId WHERE id = @muteId";

    public string GetExpireMutesQuery(bool multiServer, int timeMode)
    {
        if (multiServer)
        {
            if (timeMode == 1)
            {
                return "UPDATE sa_mutes SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND duration > 0 AND ends <= @CurrentTime";
            }
            else
            {
                return "UPDATE sa_mutes SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND duration > 0 AND passed >= duration";
            }
        }

        if (timeMode == 1)
        {
            return "UPDATE sa_mutes SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND duration > 0 AND ends <= @CurrentTime AND server_id = @serverid";
        }

        return "UPDATE sa_mutes SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND duration > 0 AND passed >= duration AND server_id = @serverid";
    }

    public string GetAddWarnQuery(bool includePlayerName)
    {
        if (includePlayerName)
        {
            return """
              INSERT INTO sa_warns
              (player_steamid, player_name, admin_steamid, admin_name, reason, duration, ends, created, server_id)
              VALUES
              (@playerSteamid, @playerName, @adminSteamid, @adminName, @warnReason, @duration, @ends, @created, @serverid);
              SELECT last_insert_rowid();
              """;
        }

        return """
          INSERT INTO sa_warns
          (player_steamid, admin_steamid, admin_name, reason, duration, ends, created, server_id)
          VALUES
          (@playerSteamid, @adminSteamid, @adminName, @warnReason, @duration, @ends, @created, @serverid);
          SELECT last_insert_rowid();
          """;
    }

    public string GetPlayerWarnsQuery(bool multiServer, bool active)
    {
        if (multiServer)
        {
            if (active)
            {
                return "SELECT * FROM sa_warns WHERE player_steamid = @PlayerSteamID AND status = 'ACTIVE' ORDER BY id DESC";
            }
            else
            {
                return "SELECT * FROM sa_warns WHERE player_steamid = @PlayerSteamID ORDER BY id DESC";
            }
        }

        if (active)
        {
            return "SELECT * FROM sa_warns WHERE player_steamid = @PlayerSteamID AND server_id = @serverid AND status = 'ACTIVE' ORDER BY id DESC";
        }

        return "SELECT * FROM sa_warns WHERE player_steamid = @PlayerSteamID AND server_id = @serverid ORDER BY id DESC";
    }

    public string GetPlayerWarnsCountQuery(bool multiServer, bool active)
    {
        if (multiServer)
        {
            if (active)
            {
                return "SELECT COUNT(*) FROM sa_warns WHERE player_steamid = @PlayerSteamID AND status = 'ACTIVE'";
            }
            else
            {
                return "SELECT COUNT(*) FROM sa_warns WHERE player_steamid = @PlayerSteamID";
            }
        }

        if (active)
        {
            return "SELECT COUNT(*) FROM sa_warns WHERE player_steamid = @PlayerSteamID AND server_id = @serverid AND status = 'ACTIVE'";
        }

        return "SELECT COUNT(*) FROM sa_warns WHERE player_steamid = @PlayerSteamID AND server_id = @serverid";
    }

    public string GetUnwarnByIdQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "UPDATE sa_warns SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND player_steamid = @steamid AND id = @warnId";
        }

        return "UPDATE sa_warns SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND player_steamid = @steamid AND id = @warnId AND server_id = @serverid";
    }

    public string GetUnwarnLastQuery(bool multiServer)
    {
        if (multiServer)
        {
            return """
              UPDATE sa_warns
              SET status = 'EXPIRED'
              WHERE status = 'ACTIVE'
              AND player_steamid = @steamid
              ORDER BY id DESC
              LIMIT 1
              """;
        }

        return """
          UPDATE sa_warns
          SET status = 'EXPIRED'
          WHERE status = 'ACTIVE'
          AND player_steamid = @steamid
          AND server_id = @serverid
          ORDER BY id DESC
          LIMIT 1
          """;
    }

    public string GetExpireWarnsQuery(bool multiServer)
    {
        if (multiServer)
        {
            return "UPDATE sa_warns SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND duration > 0 AND ends <= @CurrentTime";
        }

        return "UPDATE sa_warns SET status = 'EXPIRED' WHERE status = 'ACTIVE' AND duration > 0 AND ends <= @CurrentTime AND server_id = @serverid";
    }

    public string GetPenaltyHistoryQuery(bool multiServer)
    {
        string banServerFilter = string.Empty;
        string muteServerFilter = string.Empty;
        string warnServerFilter = string.Empty;

        if (!multiServer)
        {
            banServerFilter = "AND b.server_id = @serverid";
            muteServerFilter = "AND m.server_id = @serverid";
            warnServerFilter = "AND w.server_id = @serverid";
        }

        return $"""
            SELECT b.id, 'BAN' AS type, b.player_name, b.admin_name, b.reason, b.duration, b.created, b.ends, b.status,
                   ub.reason AS lift_reason, ub.date AS lift_date, ua.player_name AS lift_admin
            FROM sa_bans b
            LEFT JOIN sa_unbans ub ON ub.id = b.unban_id
            LEFT JOIN sa_admins ua ON ua.id = ub.admin_id
            WHERE b.player_steamid = @PlayerSteamID {banServerFilter}
            UNION ALL
            SELECT m.id, m.type, m.player_name, m.admin_name, m.reason, m.duration, m.created, m.ends, m.status,
                   um.reason AS lift_reason, um.date AS lift_date, ua.player_name AS lift_admin
            FROM sa_mutes m
            LEFT JOIN sa_unmutes um ON um.id = m.unmute_id
            LEFT JOIN sa_admins ua ON ua.id = um.admin_id
            WHERE m.player_steamid = @PlayerSteamID {muteServerFilter}
            UNION ALL
            SELECT w.id, 'WARN' AS type, w.player_name, w.admin_name, w.reason, w.duration, w.created, w.ends, w.status,
                   NULL AS lift_reason, NULL AS lift_date, NULL AS lift_admin
            FROM sa_warns w
            WHERE w.player_steamid = @PlayerSteamID {warnServerFilter}
            ORDER BY created DESC
            """;
    }
}
