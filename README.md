# ADVR Mod Manager

An Android app written in C# for finding and managing mods for **Ancient Dungeon VR**. It searches Thunderstore and Nexus Mods, imports custom ZIP files, and copies mod archives to the game over Android's Wireless debugging connection using [OnDeviceADB](https://github.com/DanTheMan827/OnDeviceADB/tree/5d12ed02a03130fe3de9e5693ae1b061038673fc).

The app copies ZIPs as-is; it does not extract or modify them. The game folder is:

```text
/sdcard/Android/data/de.erthu.ancientdungeonfull/files/ADVR_Mods
```

## Features

- Search the Ancient Dungeon VR community on [Thunderstore](https://thunderstore.io/c/ancient-dungeon-vr/).
- Search [Nexus Mods](https://www.nexusmods.com/games/ancientdungeon) through its v2 GraphQL API with a personal API key.
- Import ZIPs from device storage or a document provider.
- Browse installed ZIPs, check source-linked mods for updates, and delete archives.
- See a “possibly outdated” notice for source packages that have not been updated in over four months. This is only an age signal, not a compatibility check.
- Pair with the headset once through Wireless debugging. The app saves the address and connection port and tries to reconnect on later launches. Pair again in Settings if either changes.

## ADB setup

On the headset, enable Developer options and **Wireless debugging**. Choose **Pair device with pairing code**. In the app's **Settings** tab, enter the headset IP address, the pairing port, the six-digit pairing code, and the separate connection port shown by Wireless debugging. Tap **Pair / reconnect ADB**. Pairing and connection use different ports.

After pairing, the app stores the headset IP and connection port in its private preferences. The ADB key is kept in the app's private files; pairing codes are not stored. Use Settings to pair again or forget the saved connection. Wireless debugging must be enabled and the device reachable for reconnects.

## Nexus Mods

Nexus search requires a personal API key from [Nexus API keys](https://www.nexusmods.com/settings/api-keys). Add it in the app's **Settings** tab. The key is stored in the app's private preferences. Nexus downloads open in a browser and can then be installed with **Import ZIP**.

## Build

Requirements:

- .NET 8 SDK with the Android workload
- Java Development Kit supported by the installed .NET Android workload (JDK 21 was used for the tested build)
- Android SDK with Android API 34 and its build tools

Install the workload if needed:

```sh
dotnet workload install android
```

Build a debug APK:

```sh
dotnet build ADVRModManager/ADVRModManager.csproj -c Debug
```

If the SDK or JDK is not in the standard location, set `ANDROID_HOME`/`ANDROID_SDK_ROOT` and `JAVA_HOME` for the build. The APK is generated under `ADVRModManager/bin/Debug/net8.0-android/`.

## Third-party code and licensing

The `OnDeviceADB/` directory contains the OnDeviceADB source and its bundled native ADB binaries from the upstream revision pinned in the link above. The source library is licensed under Apache-2.0; see [OnDeviceADB/LICENSE](OnDeviceADB/LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The ADVR Mod Manager application source does not currently include a project-level license file.
