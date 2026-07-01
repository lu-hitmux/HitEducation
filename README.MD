# HitEducation

HitEducation is a Windows WPF desktop app for classroom homework display, reminders, history management, and automatic updates.

## Download

Latest public packages are published through Gitee Releases:

- App package: https://gitee.com/lu-hitmux/HitEducation/releases
- Update manifest: https://gitee.com/lu-hitmux/hiteducation/raw/main/update/version.json

## Source

This repository contains the application source code and release tooling. Local documentation and OpenCode configuration are intentionally not included in the repository.

## Build

```powershell
dotnet build app\HitEducation.App.csproj
dotnet publish app\HitEducation.App.csproj -c Release -r win-x64 --self-contained false
```