using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using PanoramaUiKit.Shared.Api;
using PanoramaUiKit.Shared.Banners;
using PanoramaUiKit.Shared.Common;
using PanoramaUiKit.Shared.Menus;
using PanoramaUiKit.Shared.Toasts;
using PanoramaUiKit.Shared.Votes;

namespace CS2_SimpleAdmin.Menus;

/// <summary>
/// Access to the Panorama UI Kit plugin and the layouts SimpleAdmin draws on.
/// </summary>
internal static class UiKit
{
    private static readonly PluginCapability<IPanoramaUiKit> Capability = new(IPanoramaUiKit.CapabilityName);

    internal static MenuLayout MenuLayout { get; private set; } = new() { Name = "uikit_menu" };

    internal static VoteLayout VoteLayout { get; private set; } = new() { Name = "uikit_vote" };

    internal static BannerLayout BannerLayout { get; private set; } = new() { Name = "uikit_banner" };

    internal static ToastLayout ToastLayout { get; private set; } = new() { Name = "uikit_toast" };

    /// <summary>
    /// Resolved on every use so a hot reload of the kit is picked up.
    /// </summary>
    internal static IPanoramaUiKit? Instance
    {
        get
        {
            try
            {
                return Capability.Get();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    internal static void Configure(MenuConfig config)
    {
        MenuLayout = new MenuLayout { Name = config.MenuLayout };
        VoteLayout = new VoteLayout { Name = config.VoteLayout };
        BannerLayout = new BannerLayout { Name = config.BannerLayout };
        ToastLayout = new ToastLayout { Name = config.ToastLayout };
    }

    /// <summary>
    /// Spawns the layouts up front: creating one on first use sends the client one big burst.
    /// </summary>
    internal static bool Preload(ILogger logger)
    {
        IPanoramaUiKit? kit = Instance;
        if (kit == null)
        {
            logger.LogError("Panorama UI Kit not found: install PanoramaUiKit and shared/PanoramaUiKit.Shared.");
            return false;
        }

        if (kit.Version != IPanoramaUiKit.ApiVersion)
        {
            logger.LogWarning("Panorama UI Kit API v{Running}, SimpleAdmin built against v{Built}.", kit.Version, IPanoramaUiKit.ApiVersion);
        }

        UiLayout[] layouts = [MenuLayout, VoteLayout, BannerLayout, ToastLayout];
        foreach (UiLayout layout in layouts)
        {
            if (!kit.Preload(layout))
            {
                logger.LogError("Layout {Layout} was refused, see the Panorama UI Kit log.", layout);
            }
        }

        return true;
    }
}
