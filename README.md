# Building Inspector 0.1.9

A minimal BepInEx plugin for Valheim. It adds four free flags to Hammer → `Inspection`: red marks a problem, yellow marks something to review, green marks an inspected area, and blue marks another category. Rotate around the surface normal with the mouse wheel. Flags automatically align to the aimed surface; the round base is removed and the pole extends slightly into the surface. Their cloth is rendered as a white runtime sprite tinted with the selected color. The mod does not scan or analyze buildings automatically.

While a flag placement ghost is active, the default `I`/`K` keys tilt it forward/backward and `J`/`L` tilt it sideways. Each key press applies the configured tilt step, limited by the configured maximum angle. The bindings and angles can be changed in `BepInEx/config/Lanoz.BuildingInspector.cfg` under `[Flag Rotation]`.

## Requirements

- Valheim for Windows.
- An active Thunderstore/r2modman profile with BepInExPack Valheim and Jötunn 2.30.2 or a compatible newer release.
- The .NET SDK for `dotnet build`.
- NuGet packages `BepInEx.Core 5.4.21` and `JotunnLib 2.30.2`, restored from the sources configured in `NuGet.Config`.
- Valheim assemblies for compilation: `Assembly-CSharp.dll`, `assembly_valheim.dll`, `assembly_utils.dll`, `UnityEngine.CoreModule.dll`, and `UnityEngine.PhysicsModule.dll`.

The plugin uses Jötunn's `CustomPiece(GameObject, bool, PieceConfig)`, `PieceManager.Instance.AddPiece`, `PrefabManager.Instance.CreateEmptyPrefab`, and `PrefabManager.Cache.GetPrefab` APIs. Each generated prefab has a persistent `ZNetView`, so placed flags are saved with the world and can be removed with the regular Hammer.

## Setup in VS Code

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) in the Thunderstore/r2modman profile used to launch the game.
2. Open the project folder in VS Code.
3. Create `BuildingInspector.local.props` next to `BuildingInspector.csproj` and set your local game and plugin paths:

   ```xml
   <Project>
     <PropertyGroup>
       <ValheimPath>C:\path\to\Valheim</ValheimPath>
       <PluginDeployPath>C:\path\to\profile\BepInEx\plugins\Lanoz-BuildingInspector</PluginDeployPath>
     </PropertyGroup>
   </Project>
   ```

   `ValheimPath` is the game root containing `valheim.exe` and `valheim_Data`. `PluginDeployPath` is the active profile's Building Inspector plugin folder. Both values are machine-specific, so the local props file is ignored by Git.

4. From the project folder, run:

   ```powershell
   dotnet build
   ```

The build restores compile-time packages, writes `bin/Debug/net472/BuildingInspector.dll`, and copies the DLL to `PluginDeployPath`. BepInEx and Jötunn must be installed in the same active profile when running the game.

## Test In Game

1. Launch Valheim through Thunderstore/r2modman with the profile containing BepInEx, Jötunn, and Building Inspector. Enter a world and equip the regular Hammer.
2. Open the build menu and choose `Inspection`, then select `Inspection Flag - Problem`, `Inspection Flag - Review`, `Inspection Flag - Verified`, or `Inspection Flag - Other`.
3. Aim at flat ground, a sloped roof, or a wall. The flag aligns to the targeted surface. Use the mouse wheel to rotate around the surface normal, or use `I`/`K` and `J`/`L` to adjust its tilt. `Insert` resets the tilt.
4. Place the flag. All four variants are free. Use the regular Hammer removal action to remove one.
5. To change controls or tilt angles, edit `BepInEx/config/Lanoz.BuildingInspector.cfg` under `[Flag Rotation]`.
6. Check `BepInEx/LogOutput.log` in the active profile. The plugin logs `Building Inspector loading...`, `Inspection Flags registered: 4.`, and `Building Inspector loaded!`; registration errors include the cause.

For multiplayer, install BepInEx, Jötunn, and this mod on the server and every connecting client.

## API References

- [Jötunn: Pieces and PieceTables](https://valheim-modding.github.io/Jotunn/tutorials/pieces.html)
- [Jötunn: CustomPiece](https://valheim-modding.github.io/Jotunn/api/Jotunn.Entities.CustomPiece.html)
- [Jötunn: PieceManager](https://valheim-modding.github.io/Jotunn/api/Jotunn.Managers.PieceManager.html)
- [Jötunn releases](https://github.com/Valheim-Modding/Jotunn/releases)
