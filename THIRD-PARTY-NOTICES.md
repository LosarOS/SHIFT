# Third-party software in SHIFT

SHIFT uses the following runtime libraries. The project's MIT license does not replace their licenses.

| Component | Attribution / license |
|---|---|
| .NET runtime / Windows Desktop | .NET Foundation and contributors; MIT and included notices |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | © Microsoft Corporation. All rights reserved.; MIT |
| SQLitePCLRaw packages | Copyright 2014–2024 SourceGear, LLC; Apache-2.0 |
| Native SQLite | Public domain; SQLitePCLRaw distribution notices apply |
| Windows App SDK / WinUI and dependencies | Microsoft and contributors; package license and NOTICE files |

`packaging/Build-Release.ps1` includes the original license/NOTICE files and NuGet metadata from resolved packages in `licenses/`, copies .NET's LICENSE and ThirdPartyNotices, and includes the Apache-2.0 text from `third-party/`. Exact versions and package hashes are pinned in `packages.lock.json` files. Development test tools are not part of the shipped application.

The portable bundle's project license is `SHIFT-LICENSE.txt`. Package license files retain their own names and attributions.
