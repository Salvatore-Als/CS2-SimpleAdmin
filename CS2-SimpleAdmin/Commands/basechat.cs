using System.Text;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Entities;
using CS2_SimpleAdmin.Menus;
using PanoramaUiKit.Shared.Api;
using PanoramaUiKit.Shared.Common;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    private const float AnnouncementSeconds = 7f;

    /// <summary>
    /// Sends a chat message only to admins that have chat permission.
    /// The message is encoded properly to handle UTF-8 characters.
    /// </summary>
    /// <param name="caller">The admin player sending the message, or null for console.</param>
    /// <param name="command">The command input containing the message.</param>
    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminToAdminSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        Helper.LogCommand(caller, command);

        byte[] utf8BytesString = Encoding.UTF8.GetBytes(command.GetCommandString[command.GetCommandString.IndexOf(' ')..]);
        string utf8String = Encoding.UTF8.GetString(utf8BytesString);

        foreach (CCSPlayerController? player in Helper.GetValidPlayers()
                     .Where(p => AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat")))
        {
            if (_localizer != null)
            {
                player.PrintToChat(_localizer["sa_adminchat_template_admin",
                    Helper.GetCallerName(caller),
                    utf8String]);
            }
        }
    }

    /// <summary>
    /// Sends a custom chat message to all players with color tags processed.
    /// </summary>
    /// <param name="caller">The admin or console sending the message.</param>
    /// <param name="command">The command input containing the message.</param>
    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminCustomSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.GetCommandString[command.GetCommandString.IndexOf(' ')..].Length == 0)
        {
            return;
        }

        byte[] utf8BytesString = Encoding.UTF8.GetBytes(command.GetCommandString[command.GetCommandString.IndexOf(' ')..]);
        string utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);

        foreach (CCSPlayerController player in Helper.GetValidPlayers())
        {
            player.PrintToChat(utf8String.ReplaceColorTags());
        }
    }

    /// <summary>
    /// Sends a chat message to all players with localization prefix and color tags handled.
    /// </summary>
    /// <param name="caller">The admin or console sending the message.</param>
    /// <param name="command">The command input containing the message.</param>
    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.GetCommandString[command.GetCommandString.IndexOf(' ')..].Length == 0)
        {
            return;
        }

        byte[] utf8BytesString = Encoding.UTF8.GetBytes(command.GetCommandString[command.GetCommandString.IndexOf(' ')..]);
        string utf8String = Encoding.UTF8.GetString(utf8BytesString);
        string? callerName = caller?.PlayerName;

        Helper.LogCommand(caller, command);
        foreach (CCSPlayerController player in Helper.GetValidPlayers())
        {
            // The message always goes out (it is a broadcast, not an activity notice),
            // but the admin's name follows ShowActivityType like every other activity message.
            player.SendLocalizedMessage(_localizer,
                "sa_adminsay_prefix",
                utf8String.ReplaceColorTags(), Helper.ResolveActivityName(player, callerName));
        }
    }

    /// <summary>
    /// Sends a private chat message from the caller to the specified target player(s).
    /// </summary>
    /// <param name="caller">The admin or console sending the private message.</param>
    /// <param name="command">The command input containing target and message.</param>
    [CommandHelper(2, "<#userid or name> <message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminPrivateSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string callerName = Helper.GetCallerName(caller);

        TargetResult? targets = GetTarget(command);
        if (targets == null)
        {
            return;
        }

        List<CCSPlayerController> playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

        //Helper.LogCommand(caller, command);

        int range = command.GetArg(0).Length + command.GetArg(1).Length + 2;
        string message = command.GetCommandString[range..];

        byte[] utf8BytesString = Encoding.UTF8.GetBytes(message);
        string utf8String = Encoding.UTF8.GetString(utf8BytesString);

        playersToTarget.ForEach(player =>
        {
            player.PrintToChat($"({callerName}) {utf8String}".ReplaceColorTags());
        });

        command.ReplyToCommand($" Private message sent!");
    }

    /// <summary>
    /// Broadcasts a message to all players in a Panorama UI Kit banner.
    /// </summary>
    /// <param name="caller">The admin or console sending the message.</param>
    /// <param name="command">The command input containing the message.</param>
    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminCenterSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        byte[] utf8BytesString = Encoding.UTF8.GetBytes(command.GetCommandString[command.GetCommandString.IndexOf(' ')..]);
        string utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);
        UiKit.Instance?.Banner(UiKit.BannerLayout, StripColorTags(utf8String), UiStyle.Info, AnnouncementSeconds);
    }

    /// <summary>
    /// Sends a message to all players in a Panorama UI Kit toast, titled with the admin's name.
    /// </summary>
    /// <param name="caller">The admin or console sending the message.</param>
    /// <param name="command">The command input containing the message.</param>
    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminHudSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        byte[] utf8BytesString = Encoding.UTF8.GetBytes(command.GetCommandString[command.GetCommandString.IndexOf(' ')..]);
        string utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);

        IPanoramaUiKit? kit = UiKit.Instance;
        if (kit == null)
        {
            return;
        }

        string message = StripColorTags(utf8String);
        string? callerName = caller?.PlayerName;

        foreach (CCSPlayerController player in Helper.GetValidPlayers())
        {
            kit.Toast(player, UiKit.ToastLayout, Helper.ResolveActivityName(player, callerName), message, UiStyle.Info, AnnouncementSeconds);
        }
    }

    /// <summary>
    /// Panorama does not render chat color tags such as {red}: drop them.
    /// </summary>
    private static string StripColorTags(string message)
    {
        return Regex.Replace(message, @"\{[A-Za-z]+\}", string.Empty).Trim();
    }
}
