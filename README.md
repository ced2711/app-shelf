# AppShelf

An ~80 KB Windows app that shows every app in Start > All apps (desktop and Store apps) as an icon grid.

- Follows the Windows light/dark setting; A-Z sections, filters for All / Desktop / Store apps
- Search box (Ctrl+F, or just start typing; Esc clears), F5 refreshes
- Double-click an app (or press Enter) for details: path, version, publisher, install date, size, uninstall command,
  package info. Click any row to copy it
- Right-click (Ctrl/Shift multi-select works) or the details window: Open, Open file location, Add shortcut to desktop
- Desktop shortcuts copy the original Start menu shortcut when there is one; otherwise they link to the
  app's entry in `shell:AppsFolder`, the same way dragging it from Start does. Existing shortcuts are never overwritten.

No install and no runtime download: it uses .NET Framework 4.x, which ships with Windows 10/11.

## Build

```
build.cmd          rem -> AppShelf.exe
build.cmd test     rem -> AppShelfTest.exe <out folder>: console self-test that saves screenshots (light and dark) and sample shortcuts
```

## License

[MIT](LICENSE)
