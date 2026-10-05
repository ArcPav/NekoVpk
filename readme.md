<h1 align="center">
  <br>
  <img src="image/NekoVpk-128.png" alt="NekoVpk" width="128">
  <br>
  NekoVpk
  <br>
</h1>
<h4 align="center">🧰 Left4Dead2 addon manager </h4>
<h1 align="center">
  <img src="image/0.1.6.0_Preview.png" width="800">
</h1>

## 🐈feature
* Intelligently identify tags based on addon content
* Keyword search
* Read addoninfo
* Enable or disable addon
* Disable certain content in the addon, which is useful for survivor mods.
* ![](image/{5B7F3754-AEAB-478f-90C3-D0D2934D8D8A}.png)
* ![](image/change_neko7z.png)


## Special Dependencies
https://github.com/Starfelll/ValvePak/tree/nekovpk
https://github.com/Starfelll/ValveKeyValue/tree/nekovpk


## changelog
#### v0.1.6.3
- **Check for Updates**: Added an update checker for Workshop addons (toolbar button / context menu), usable on the whole library, the current selection, or selected folders. It compares the update date stored in each addon's `addoninfo.txt` with Steam's, lists outdated addons for confirmation, then replaces the vpk. Requires a Steam Web API key.
- **Folder Mode**: Added a folder view for subfolders under `addons`. Create / rename / delete folders, multi-select, and move addons (single or batch) between folders; the preview image and backup file move along with the vpk. `workshop` and SourceMod-related folders are reserved and hidden.
- **Workshop Browsing**: Added pagination (previous / next page) with a configurable page size (15 / 30 / 50 / 100), a grid / large-picture mode, and star ratings. A warning is shown when only part of a collection could be loaded.
- **Download**: Downloaded addons now get the Workshop link and update date written into `addoninfo.txt` (created from Workshop metadata if missing). A preview image is saved alongside when the vpk has none.
- **Settings**:
  - Added a "Validate" button for the Steam Web API key.
  - Added "Do not create backup when switching variant".
  - Added "Auto-resize columns on refresh".
  - Added theme color: presets, custom hex, or follow the system accent color.
  - Added window transparency (shown when no background image is set).
  - Settings are now stored in `%LocalAppData%\NekoVpk\settings.json`; the API key is encrypted with Windows DPAPI.
- **Tags**: Added mutually exclusive tags (e.g. `L4N-Survivor` conflicts with every other survivor tag); enabling one automatically disables the conflicting ones. Built-in survivor tags are used if `TaggedAssets.jsonc` is missing. VPKs with a non-standard version number now show it as a tag.
- **Improvements**:
  - Auto-detect the text encoding of `addoninfo.txt` when reading and rewriting.
  - Cached GIF previews are cleaned up on exit; leftovers older than one day are swept at startup.
  - Image downloads are capped at 6 concurrent requests.
  - Added a minimum window size (1100×500).
- **Fixes**:
  - Closing a dialog with the window's close button now counts as Cancel instead of leaving it hanging.
  - Choosing the game directory no longer crashes when the saved path is invalid.
#### v0.1.6
- **Localization**: Added multi-language support (English, Chinese, Japanese).
- **Conflict Detection**: Added addon conflict detection. Conflicting mods are highlighted in red; double-clicking displays specific conflicting files and priority.
- **Collection Mode**: Added Workshop Collection mode. Toggle to search for collections, single-click to view summaries, and double-click to browse the addons inside.
- **[Archive Management](docs/How-To-Create-Neko7z.md)**: Added support for handling multiple `.neko7z` packages (e.g., `1.neko7z`, `2.neko7z`) when selectively enabling/disabling addon contents, removing the previous limit of a single `0.neko7z` file.
- **Workshop Filtering**: Added sorting options (Trending, Top Rated, Most Recent, Recently Updated) to the online mode.
- **Workshop Search**: Pasting a full Workshop URL into the search bar now directly triggers the download.
- **Workshop UI**: Added support for rendering GIF previews and parsing BBCode in descriptions.
- **Workshop UI**: Relocated the download button to sit above the addon description for easier access.
- **Tag Filtering**: Improved tag filtering to allow selecting main category tags (not just specific sub-tags).
- **List Settings**: Columns now auto-resize. Added options to toggle the visibility of specific columns (Tag, Type, Added time, Size).
- **UI/UX**: Optimized various UI elements and confirmation dialogs.
#### v0.1.5
- Fixed the issue that files would be lost when only one disabled vpk content was left.
- Setting: added option "Compression level".
#### v0.1.4
- Store window size and position.
- Tag: merge zoey_light, francis_light, bill_death_pose, modify script to vscript.
- Fixed a bug that caused the window to freeze when vpk content was enabled.
#### v0.1.3
- Skip reading failed vpk files
#### v0.1.2
- Tag: Support for infected assets.
- Add a new column to display the type of addon.
#### v0.1.1
- Automatically back up files when they are modified.
#### v0.1.0
- Breaking updates, mods that disable some content need to be fully enabled in the old version to be recognized in the new version.
- Enable file compression for disabled content.
- New UI layout.
- Add tags: zoey_light,bill_deathpose,francis_light.
#### v0.0.8
- Supports searching file names.
- During the scanning process, the workshop directory is no longer required.
- Close the file handle after reading the image.
- TaggedAssets.jsonc: added weapon-related tags.
#### v0.0.7.1
- TaggedAssets.jsonc add tags: particle,sound,spr,xdr,skybox
- Identify the vpk with a numeric file name as the workshop id
- Fixed an issue where addon titles with the same characters as tags would not be displayed in the search list
