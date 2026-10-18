# Changelog

All notable changes to this package are documented in this file.

## [1.0.1] - 2026-10-18

### Fixed
- Multi-line string values (e.g. JSON) no longer render as a clipped middle line. The row shows a
  read-only one-line preview with an **Edit…** button that opens a scrollable multi-line editor
  with Save and Cancel. Single-line strings are still edited inline.
- Unity internal keys from `unity_*` (e.g. `unity_connect.session_id`) and the Performance Testing
  package (`PT_Run`, `PT_Settings`) are now hidden while **Show Unity Keys** is off. Like other Unity
  keys, they are also left untouched by Delete All, snapshot restore, and Replace import.
- **Show Unity Keys** is now visibly highlighted while it's on.

## [1.0.0] - 2026-10-18

### Added
- PlayerPrefs Explorer window (**Tools > PlayerPrefs Explorer**), built with UI Toolkit.
- Key discovery from Editor storage on Windows (registry), macOS (plist), and Linux (XML), with a toggle for Unity's internal keys.
- Type detection (int / float / string) through the PlayerPrefs API.
- Search, sorting by key or type, inline value editing, and type conversion.
- Add Key form with validation; Delete and Delete All with confirmation.
- Copy as C# (Get line) and Copy Set as C# (row context menu).
- Named snapshots stored in `UserSettings/PlayerPrefsExplorer/Snapshots`: save, restore, rename, delete.
- JSON export and import (Merge or Replace).
- Auto-refresh on Play Mode changes and window focus; status bar with key count and company/product.
