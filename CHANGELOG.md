# Changelog

All notable changes to this package are documented in this file.

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
