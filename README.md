# Building Inspector 0.2.5

A minimal BepInEx plugin for Valheim. It adds six free flags to the regular Hammer's `Inspection` category: Problem, Review, Verified, Other, Structural, and Finish. Each label, description, and color can be customized in the mod configuration. Rotate around the surface normal with the mouse wheel. Flags automatically align to the aimed surface; the pole extends slightly into the surface. They use Valheim's building support system and collapse when unsupported, and another inspection flag blocks placement in the same spot. Their cloth is rendered as a semi-transparent runtime sprite tinted with the selected color. Equip the regular Hammer to place or dismantle flags. The mod does not scan or analyze buildings automatically.

While a flag placement ghost is active, the default `I`/`K` keys tilt it forward/backward and `J`/`L` tilt it sideways. Each key press applies the configured tilt step, limited by the configured maximum angle. `Insert` resets the tilt. Aim at a placed flag and press `N` to add or edit its note; only the player who placed the flag can change it. The note is saved with the flag and appears when you look at it. The bindings and angles can be changed in `BepInEx/config/Lanoz.BuildingInspector.cfg`.

## Requirements

- Valheim for Windows.
- An active Thunderstore/r2modman profile with BepInExPack Valheim and Jötunn 2.30.2 or a compatible newer release.
- The .NET SDK for `dotnet build`.
- NuGet packages `BepInEx.Core 5.4.21` and `JotunnLib 2.30.2`, restored from the sources configured in `NuGet.Config`.
- Valheim assemblies for compilation: `Assembly-CSharp.dll`, `assembly_valheim.dll`, `assembly_utils.dll`, `UnityEngine.CoreModule.dll`, and `UnityEngine.PhysicsModule.dll`.

Each generated prefab has a persistent `ZNetView`, so placed flags and their notes are saved with the world.

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
2. Open the build menu and choose `Inspection`, then select one of the six inspection labels.
3. Aim at flat ground, a sloped roof, or a wall. The flag aligns to the targeted surface. Use the mouse wheel to rotate around the surface normal, or use `I`/`K` and `J`/`L` to adjust its tilt. `Insert` resets the tilt.
4. Place the flag. All variants are free. Aim at a placed flag and press `N` to enter or update its note. Only its placer can edit the note. Use the regular Hammer removal mode to remove flags.
5. To change controls and tilt angles, edit `[Flag Rotation]`; to change note and spacing settings, edit `[Flag Notes]` and `[Flag Placement]` in `BepInEx/config/Lanoz.BuildingInspector.cfg`. Under `[Flag Labels]`, edit the names, descriptions, and hex colors (`#FF0000`) for slots 1–6. Restart the game after changing labels.
6. Check `BepInEx/LogOutput.log` in the active profile. The plugin logs `Building Inspector loading...`, `Inspection Flags registered: 6.`, and `Building Inspector loaded!`; registration errors include the cause.

For multiplayer, install BepInEx, Jötunn, and this mod on the server and every connecting client.

## API References

- [Jötunn: Pieces and PieceTables](https://valheim-modding.github.io/Jotunn/tutorials/pieces.html)
- [Jötunn: CustomPiece](https://valheim-modding.github.io/Jotunn/api/Jotunn.Entities.CustomPiece.html)
- [Jötunn: PieceManager](https://valheim-modding.github.io/Jotunn/api/Jotunn.Managers.PieceManager.html)
- [Jötunn releases](https://github.com/Valheim-Modding/Jotunn/releases)
