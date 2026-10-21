# CS2-SimpleAdmin (Panorama UI Kit fork)

> Fork of [daffyyyy/CS2-SimpleAdmin](https://github.com/daffyyyy/CS2-SimpleAdmin), the CounterStrikeSharp admin plugin for Counter-Strike 2.
> This fork replaces the chat / center HTML menus with [Panorama UI Kit](https://github.com/Salvatore-Als/cs2-panorama-ui-kit): admin menus and votes are drawn as real Panorama panels on the player's screen.

Every command, the penalty system, the database schema and the module API stay compatible with upstream. Existing servers and modules keep working once the new dependencies are installed.

---

## What changes compared to upstream

- **Panorama menus**: the admin menu (`css_admin`), the player / duration / reason pickers and module menus open as a Panorama UI Kit menu instead of a MenuManagerCS2 menu.
- **Panorama votes**: `css_vote` shows a vote screen to every player, collects one answer each, closes after 30 seconds and prints the results in chat.
- **Configurable layouts**: the menu, vote, banner and toast layout names are set in the config, so you can use your own Panorama layouts.
- **No more MenuManagerCS2 stack**: MenuManagerCS2, PlayerSettings and AnyBaseLibCS2 are no longer required.
- **Code style**: the whole codebase follows [CODING_CONVENTIONS.md](CODING_CONVENTIONS.md) (explicit types, no ternaries, braces everywhere), enforced by `.editorconfig`.

---

## Features (inherited from upstream)

- Bans, IP bans, offline bans, mutes, gags, silences and warnings with durations and reasons
- MySQL backend, with experimental SQLite support
- Multi-server: penalties and admins synced across servers sharing a database
- Admin groups, immunity and permission overrides
- Penalty history (`css_history`) and connect notifications
- Discord webhooks for penalties and admin actions
- Module API (`CS2-SimpleAdminApi`) to add commands, menus and penalty hooks from other plugins

---

## Requirements

- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) **1.0.376** or newer
- [Panorama UI Kit](https://github.com/Salvatore-Als/cs2-panorama-ui-kit): `plugins/PanoramaUiKit` and `shared/PanoramaUiKit.Shared`
- [PanoramaManager](https://github.com/Next-il/PanoramaManager), required by Panorama UI Kit
- The Panorama UI Kit Workshop addon mounted on the server (or your own layouts that follow its contracts)
- A MySQL database, or SQLite

If Panorama UI Kit is missing at startup, SimpleAdmin logs `Panorama UI Kit not found` and menus will not open.

---

## Installation

1. Install CounterStrikeSharp, PanoramaManager and Panorama UI Kit, and mount the UI Kit Workshop addon.
2. Build this repository (see [Building](#building)) or download a release, then copy `CS2-SimpleAdmin` into `addons/counterstrikesharp/plugins/` and `CS2-SimpleAdminApi` into `addons/counterstrikesharp/shared/`.
3. Start the server once. The config is generated at:
   ```
   addons/counterstrikesharp/configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json
   ```
4. Fill in the database settings and restart.

---

## Configuration

The generated config is the upstream one (`ConfigVersion` 26). The old `MenuType` key is replaced by layout names under `MenuConfig`:

```json
"MenuConfig": {
  "MenuLayout": "uikit_menu",
  "VoteLayout": "uikit_vote",
  "BannerLayout": "uikit_banner",
  "ToastLayout": "uikit_toast"
}
```

Keep the defaults to use the Workshop addon layouts, or point them at your own. The layouts are preloaded when the plugin starts, so they are not created on the first menu open.

---

## Building

```bash
dotnet build CS2-SimpleAdmin.sln -c Release
dotnet format CS2-SimpleAdmin.sln
```

The project targets .NET 10. `PanoramaUiKit.Shared.dll` is referenced from `CS2-SimpleAdmin/3rd_party/`.

---

## Modules

Upstream modules live in [`Modules/`](Modules). See [MODULE_DEVELOPMENT.md](Modules/MODULE_DEVELOPMENT.md) to write your own. Menus registered through the API are rendered with Panorama UI Kit automatically.

---

## Documentation

Commands, permissions and the general configuration are documented in the upstream wiki: [cs2-simpleadmin.daffyy.dev](https://cs2-simpleadmin.daffyy.dev).

---

## Credits

- [daffyyyy](https://github.com/daffyyyy) for CS2-SimpleAdmin, which this fork is based on
- [Hackmastr](https://github.com/Hackmastr/css-basic-admin/), whose work inspired the original plugin
- [Next-il](https://github.com/Next-il/PanoramaManager) for PanoramaManager

---

## License

GPL-3.0, like upstream. See [LICENSE](LICENSE).
