using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace BuildingInspector
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    public class BuildingInspector : BaseUnityPlugin
    {
        public const string PluginGUID = "Lanoz.BuildingInspector";
        public const string PluginName = "Building Inspector";
        public const string PluginVersion = "0.2.2";

        private const int FlagVariantCount = 6;
        private const string NoteRpcName = "BuildingInspector_SetFlagNote";
        private const string OwnerRpcName = "BuildingInspector_SetFlagOwner";
        private const string NoteZdoKey = "BuildingInspector_FlagNote";
        private const string OwnerZdoKey = "BuildingInspector_FlagOwner";
        private const string OwnerPeerZdoKey = "BuildingInspector_FlagOwnerPeer";

        private static readonly List<Texture2D> IconTextures = new List<Texture2D>();
        private static readonly List<FlagNoteReceiver> FlagReceivers = new List<FlagNoteReceiver>();
        private static Texture2D clothTexture;
        private static Sprite clothSprite;
        private static ConfigEntry<KeyboardShortcut> tiltForwardKey;
        private static ConfigEntry<KeyboardShortcut> tiltBackwardKey;
        private static ConfigEntry<KeyboardShortcut> tiltLeftKey;
        private static ConfigEntry<KeyboardShortcut> tiltRightKey;
        private static ConfigEntry<KeyboardShortcut> resetTiltKey;
        private static ConfigEntry<float> tiltStepDegrees;
        private static ConfigEntry<float> maxTiltDegrees;
        private static ConfigEntry<KeyboardShortcut> editNoteKey;
        private static ConfigEntry<float> flagBlockRadius;
        private static readonly List<ConfigEntry<string>> labelNames = new List<ConfigEntry<string>>();
        private static readonly List<ConfigEntry<string>> labelDescriptions = new List<ConfigEntry<string>>();
        private static readonly List<ConfigEntry<string>> labelColors = new List<ConfigEntry<string>>();
        private static FlagVariant[] flagVariants;
        private static FlagNoteReceiver noteInput;
        private static float tiltX;
        private static float tiltZ;
        private static GameObject lastTiltedGhost;
        private static Quaternion lastGhostBaseRotation;
        private static Quaternion lastGhostRotation;
        private static bool hasLastGhostRotation;

        private void Awake()
        {
            Logger.LogInfo("Building Inspector loading...");
            tiltForwardKey = Config.Bind("Flag Rotation", "TiltForwardKey", new KeyboardShortcut(KeyCode.I), "Tilt the flag forward.");
            tiltBackwardKey = Config.Bind("Flag Rotation", "TiltBackwardKey", new KeyboardShortcut(KeyCode.K), "Tilt the flag backward.");
            tiltLeftKey = Config.Bind("Flag Rotation", "TiltLeftKey", new KeyboardShortcut(KeyCode.J), "Tilt the flag to the left.");
            tiltRightKey = Config.Bind("Flag Rotation", "TiltRightKey", new KeyboardShortcut(KeyCode.L), "Tilt the flag to the right.");
            resetTiltKey = Config.Bind("Flag Rotation", "ResetTiltKey", new KeyboardShortcut(KeyCode.Insert), "Reset the flag tilt.");
            tiltStepDegrees = Config.Bind("Flag Rotation", "TiltStepDegrees", 15f, "Tilt change per key press in degrees.");
            maxTiltDegrees = Config.Bind("Flag Rotation", "MaxTiltDegrees", 60f, "Maximum tilt angle in either direction.");
            editNoteKey = Config.Bind("Flag Notes", "EditNoteKey", new KeyboardShortcut(KeyCode.N), "Edit the note on the aimed inspection flag.");
            flagBlockRadius = Config.Bind("Flag Placement", "BlockRadius", 0.2f,
                new ConfigDescription("Minimum spacing between placed inspection flags in meters.", new AcceptableValueRange<float>(0.05f, 1f)));
            BindFlagVariants();
            noteInput = gameObject.AddComponent<FlagNoteReceiver>();
            new Harmony(PluginGUID).PatchAll(typeof(BuildingInspector).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += RegisterInspectionFlag;
        }

        private void BindFlagVariants()
        {
            int[] configSlots = { 1, 2, 3, 4, 5, 6 };
            string[] defaultNames = { "Problem", "Review", "Verified", "Other", "Structural", "Finish" };
            string[] defaultDescriptions =
            {
                "A building issue was found.", "This part needs review.", "This part was inspected.", "Another inspection category.",
                "Structural work needs attention.", "Finishing work needs attention."
            };
            string[] defaultColors = { "#FA2E1F", "#FFE014", "#1FE633", "#1F80FF", "#A64DFF", "#F2F2F2" };
            string[] prefabNames = { "InspectionFlagRed", "InspectionFlagYellow", "InspectionFlagGreen", "InspectionFlagBlue", "InspectionFlagCustom5", "InspectionFlagCustom8" };
            flagVariants = new FlagVariant[FlagVariantCount];

            for (int index = 0; index < FlagVariantCount; index++)
            {
                int number = configSlots[index];
                labelNames.Add(Config.Bind("Flag Labels", $"Label{number}Name", defaultNames[index], "Display name for this flag category."));
                labelDescriptions.Add(Config.Bind("Flag Labels", $"Label{number}Description", defaultDescriptions[index], "Description shown in the build menu."));
                labelColors.Add(Config.Bind("Flag Labels", $"Label{number}Color", defaultColors[index], "Flag color as a hex value, for example #FF0000."));

                Color color;
                if (!ColorUtility.TryParseHtmlString(labelColors[index].Value, out color))
                {
                    color = Color.white;
                    Logger.LogWarning($"Invalid color for Label{number}Color; using white.");
                }

                string name = string.IsNullOrWhiteSpace(labelNames[index].Value) ? defaultNames[index] : labelNames[index].Value.Trim();
                string description = labelDescriptions[index].Value ?? string.Empty;
                flagVariants[index] = new FlagVariant($"Inspection Flag - {name}", description, prefabNames[index], color);
            }

        }

        private void RegisterInspectionFlag()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterInspectionFlag;
            try
            {
                int registeredCount = AddInspectionFlags();
                Logger.LogInfo($"Inspection Flags registered: {registeredCount}.");
                Logger.LogInfo("Building Inspector loaded!");
            }
            catch (Exception exception)
            {
                Logger.LogError($"Failed to register Inspection Flag: {exception}");
            }
        }

        private void Update()
        {
            if (editNoteKey == null || !editNoteKey.Value.IsDown() || TextInput.IsVisible())
            {
                return;
            }

            Camera camera = Camera.main;
            if (!camera)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            if (!Physics.Raycast(ray, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            FlagNoteReceiver receiver = hit.collider.GetComponentInParent<FlagNoteReceiver>();
            if (receiver)
            {
                noteInput.Open(receiver.GetComponent<ZNetView>());
            }
        }

        private int AddInspectionFlags()
        {
            GameObject materialSource = PrefabManager.Cache.GetPrefab<GameObject>("wood_pole");
            Renderer sourceRenderer = materialSource ? materialSource.GetComponentInChildren<Renderer>() : null;
            if (!sourceRenderer || !sourceRenderer.sharedMaterial)
            {
                throw new InvalidOperationException("Could not find the vanilla wood_pole material.");
            }

            Piece sourcePiece = materialSource.GetComponent<Piece>();
            if (!sourcePiece)
            {
                throw new InvalidOperationException("Could not find the Piece component on vanilla wood_pole.");
            }

            WearNTear sourceWearNTear = materialSource.GetComponent<WearNTear>();
            if (!sourceWearNTear)
            {
                throw new InvalidOperationException("Could not find the WearNTear component on vanilla wood_pole.");
            }

            Material woodMaterial = sourceRenderer.sharedMaterial;
            int pieceLayer = LayerMask.NameToLayer("piece");
            int registeredCount = 0;

            foreach (FlagVariant variant in flagVariants)
            {
                if (AddInspectionFlag(variant, woodMaterial, sourcePiece.m_placeEffect, sourceWearNTear, pieceLayer))
                {
                    registeredCount++;
                }
            }

            if (registeredCount != flagVariants.Length)
            {
                throw new InvalidOperationException($"Only {registeredCount} of {flagVariants.Length} Inspection Flags were registered.");
            }

            var blockingFlags = new List<Piece>();
            foreach (FlagVariant variant in flagVariants)
            {
                GameObject flagPrefab = PrefabManager.Cache.GetPrefab<GameObject>(variant.PrefabName);
                Piece flagPiece = flagPrefab ? flagPrefab.GetComponent<Piece>() : null;
                if (flagPiece)
                {
                    blockingFlags.Add(flagPiece);
                }
            }

            foreach (Piece flagPiece in blockingFlags)
            {
                flagPiece.m_blockingPieces = blockingFlags;
                flagPiece.m_blockRadius = Mathf.Clamp(flagBlockRadius.Value, 0.05f, 1f);
            }

            return registeredCount;
        }

        private bool AddInspectionFlag(FlagVariant variant, Material woodMaterial,
            EffectList placeEffect, WearNTear sourceWearNTear, int pieceLayer)
        {
            GameObject prefab = PrefabManager.Instance.CreateEmptyPrefab(variant.PrefabName, true);
            if (!prefab)
            {
                throw new InvalidOperationException($"Jotunn could not create the {variant.PrefabName} prefab.");
            }

            prefab.transform.localScale = Vector3.one * 0.5f;
            prefab.AddComponent<Piece>();
            prefab.AddComponent<FlagNoteReceiver>();
            GameObject visuals = new GameObject("InspectionFlagVisuals");
            visuals.transform.SetParent(prefab.transform, false);

            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            if (!collider)
            {
                throw new InvalidOperationException("The InspectionFlag prefab has no root BoxCollider.");
            }

            MeshRenderer rootRenderer = prefab.GetComponent<MeshRenderer>();
            if (rootRenderer)
            {
                rootRenderer.enabled = false;
            }

            if (pieceLayer >= 0)
            {
                prefab.layer = pieceLayer;
            }

            collider.center = new Vector3(0f, 0.68f, 0f);
            collider.size = new Vector3(0.55f, 1.4f, 0.16f);
            collider.isTrigger = false;

            AddPrimitive(visuals, PrimitiveType.Cylinder, "InspectionFlagPole",
                new Vector3(0f, 0.58f, 0f), new Vector3(0.027f, 0.68f, 0.027f),
                woodMaterial, pieceLayer);
            AddClothSprite(visuals, variant, pieceLayer);

            WearNTear wearNTear = prefab.AddComponent<WearNTear>();
            wearNTear.m_new = visuals;
            wearNTear.m_worn = visuals;
            wearNTear.m_broken = visuals;
            wearNTear.m_noRoofWear = true;
            wearNTear.m_noSupportWear = sourceWearNTear.m_noSupportWear;
            wearNTear.m_supports = false;
            wearNTear.m_staticPosition = sourceWearNTear.m_staticPosition;
            wearNTear.m_materialType = sourceWearNTear.m_materialType;
            wearNTear.m_health = sourceWearNTear.m_health;
            wearNTear.m_autoCreateFragments = false;
            wearNTear.m_fragmentRoots = Array.Empty<GameObject>();

            var config = new PieceConfig
            {
                Name = variant.DisplayName,
                Description = variant.Description,
                PieceTable = "Hammer",
                Category = "Inspection"
            };

            config.Icon = CreateFlagIcon(variant.Color, variant.PrefabName);
            if (!config.Icon)
            {
                throw new InvalidOperationException($"Could not create an icon for {variant.PrefabName}.");
            }

            var customPiece = new CustomPiece(prefab, false, config);
            customPiece.Piece.m_resources = Array.Empty<Piece.Requirement>();
            customPiece.Piece.m_placeEffect = placeEffect;
            customPiece.Piece.m_canRotate = true;

            if (!PieceManager.Instance.AddPiece(customPiece))
            {
                throw new InvalidOperationException($"Jotunn's PieceManager rejected {variant.PrefabName}.");
            }

            return true;
        }

        private static void AddPrimitive(GameObject parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 localScale, Material material, int pieceLayer)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            if (pieceLayer >= 0)
            {
                part.layer = pieceLayer;
            }

            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider)
            {
                partCollider.enabled = false;
            }

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void AddClothSprite(GameObject parent, FlagVariant variant, int pieceLayer)
        {
            if (!clothTexture)
            {
                const int textureSize = 4;
                clothTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[textureSize * textureSize];
                for (int index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = new Color32(255, 255, 255, 255);
                }

                clothTexture.SetPixels32(pixels);
                clothTexture.Apply(false, true);
                clothSprite = Sprite.Create(clothTexture, new Rect(0f, 0f, textureSize, textureSize),
                    new Vector2(0.5f, 0.5f), textureSize);
                clothSprite.name = "InspectionFlagClothSprite";
            }

            GameObject cloth = new GameObject("InspectionFlagCloth");
            cloth.transform.SetParent(parent.transform, false);
            cloth.transform.localPosition = new Vector3(0.2f, 1.15f, 0f);
            cloth.transform.localScale = new Vector3(0.39f, 0.24f, 1f);
            if (pieceLayer >= 0)
            {
                cloth.layer = pieceLayer;
            }

            SpriteRenderer spriteRenderer = cloth.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = clothSprite;
            Color clothColor = variant.Color;
            clothColor.a = Mathf.Min(clothColor.a, 0.5f);
            spriteRenderer.color = clothColor;
        }

        private static void AlignFlagToSurface(Player player)
        {
            GameObject ghost = AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player) as GameObject;
            if (!ghost || !ghost.name.StartsWith("InspectionFlag", StringComparison.Ordinal))
            {
                lastTiltedGhost = null;
                hasLastGhostRotation = false;
                return;
            }

            Camera camera = Camera.main;
            if (!camera)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            FieldInfo placeRayMaskField = AccessTools.Field(typeof(Player), "m_placeRayMask");
            int placeRayMask = placeRayMaskField != null
                ? (int)placeRayMaskField.GetValue(player)
                : Physics.DefaultRaycastLayers;

            if (!Physics.Raycast(ray, out RaycastHit hit, 50f, placeRayMask, QueryTriggerInteraction.Ignore) ||
                Vector3.Distance(hit.point, ghost.transform.position) > 1.75f)
            {
                return;
            }

            bool isNewGhost = ghost != lastTiltedGhost;
            if (isNewGhost)
            {
                tiltX = 0f;
                tiltZ = 0f;
                hasLastGhostRotation = false;
            }

            Quaternion currentRotation = ghost.transform.rotation;
            Quaternion baseRotation = !isNewGhost && hasLastGhostRotation &&
                Quaternion.Angle(currentRotation, lastGhostRotation) < 0.1f
                ? lastGhostBaseRotation
                : currentRotation;

            Vector3 forward = Vector3.ProjectOnPlane(baseRotation * Vector3.forward, hit.normal);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(camera.transform.forward, hit.normal);
            }

            if (forward.sqrMagnitude > 0.001f)
            {
                Quaternion surfaceRotation = Quaternion.LookRotation(forward.normalized, hit.normal);
                if (tiltForwardKey.Value.IsDown())
                {
                    tiltX = Mathf.Clamp(tiltX + tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (tiltBackwardKey.Value.IsDown())
                {
                    tiltX = Mathf.Clamp(tiltX - tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (tiltLeftKey.Value.IsDown())
                {
                    tiltZ = Mathf.Clamp(tiltZ - tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (tiltRightKey.Value.IsDown())
                {
                    tiltZ = Mathf.Clamp(tiltZ + tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (resetTiltKey.Value.IsDown())
                {
                    tiltX = 0f;
                    tiltZ = 0f;
                }

                ghost.transform.rotation = surfaceRotation * Quaternion.Euler(tiltX, 0f, tiltZ);
                Vector3 ghostToSurface = ghost.transform.position - hit.point;
                Vector3 tangentOffset = Vector3.ProjectOnPlane(ghostToSurface, hit.normal);
                ghost.transform.position = hit.point + tangentOffset - hit.normal * 0.12f;
                lastTiltedGhost = ghost;
                lastGhostBaseRotation = surfaceRotation;
                lastGhostRotation = ghost.transform.rotation;
                hasLastGhostRotation = true;
            }
        }

        private static Sprite CreateFlagIcon(Color flagColor, string iconName)
        {
            const int size = 64;
            Color32[] pixels = new Color32[size * size];
            Color32 wood = new Color32(112, 75, 42, 255);
            Color32 cloth = flagColor;

            DrawRectangle(pixels, size, 29, 13, 34, 54, wood);
            DrawRectangle(pixels, size, 19, 8, 44, 13, wood);
            DrawRectangle(pixels, size, 34, 43, 53, 55, cloth);
            DrawRectangle(pixels, size, 34, 39, 49, 43, cloth);

            Texture2D iconTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            iconTexture.SetPixels32(pixels);
            iconTexture.Apply(false, true);
            IconTextures.Add(iconTexture);
            Sprite iconSprite = Sprite.Create(iconTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            iconSprite.name = $"{iconName}Icon";
            return iconSprite;
        }

        private static void DrawRectangle(Color32[] pixels, int textureSize,
            int xMin, int yMin, int xMax, int yMax, Color32 color)
        {
            for (int y = yMin; y < yMax; y++)
            {
                for (int x = xMin; x < xMax; x++)
                {
                    pixels[y * textureSize + x] = color;
                }
            }
        }

        private sealed class FlagVariant
        {
            public string DisplayName { get; }
            public string Description { get; }
            public string PrefabName { get; }
            public Color Color { get; }

            public FlagVariant(string displayName, string description, string prefabName, Color color)
            {
                DisplayName = displayName;
                Description = description;
                PrefabName = prefabName;
                Color = color;
            }
        }

        private sealed class FlagNoteReceiver : MonoBehaviour, TextReceiver, Hoverable
        {
            private ZNetView nview;

            private void Awake()
            {
                if (!FlagReceivers.Contains(this))
                {
                    FlagReceivers.Add(this);
                }

                nview = GetComponent<ZNetView>();
                if (nview)
                {
                    nview.Register<string>(NoteRpcName, SetNoteRpc);
                    nview.Register<long, long>(OwnerRpcName, SetOwnerRpc);
                }

                ApplyPlayerCollisionIgnores();
            }

            private void OnDestroy()
            {
                FlagReceivers.Remove(this);
            }

            public void IgnoreCollisionWith(Player player)
            {
                if (!player)
                {
                    return;
                }

                Collider flagCollider = GetComponent<Collider>();
                if (!flagCollider)
                {
                    return;
                }

                Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider && playerCollider != flagCollider)
                    {
                        Physics.IgnoreCollision(flagCollider, playerCollider, true);
                    }
                }
            }

            private void ApplyPlayerCollisionIgnores()
            {
                Player[] players = FindObjectsOfType<Player>();
                foreach (Player player in players)
                {
                    IgnoreCollisionWith(player);
                }
            }

            public void Open(ZNetView target)
            {
                nview = target;
                if (!nview || nview.GetZDO() == null || !TextInput.instance || Player.m_localPlayer == null)
                {
                    return;
                }

                long ownerId = nview.GetZDO().GetLong(OwnerZdoKey, 0L);
                long playerId = Player.m_localPlayer.GetPlayerID();
                if (ownerId == 0L)
                {
                    SetPlacer(playerId, ZNet.instance ? ZNet.GetUID() : 0L);
                    ownerId = nview.GetZDO().GetLong(OwnerZdoKey, 0L);
                }

                if (ownerId == playerId)
                {
                    if (nview.GetZDO().GetLong(OwnerPeerZdoKey, 0L) == 0L && ZNet.instance)
                    {
                        SetPlacer(playerId, ZNet.GetUID());
                    }

                    TextInput.instance.RequestText(this, "Inspection flag note", 160);
                }
            }

            public string GetText()
            {
                return nview && nview.GetZDO() != null ? nview.GetZDO().GetString(NoteZdoKey, string.Empty) : string.Empty;
            }

            public string GetHoverText()
            {
                Piece piece = GetComponent<Piece>();
                string text = piece ? piece.m_name : "Inspection Flag";
                string note = GetText();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    text += $"\n<color=#FFE080>Note:</color> {note}";
                }

                bool canEdit = nview && nview.GetZDO() != null && Player.m_localPlayer != null &&
                    nview.GetZDO().GetLong(OwnerZdoKey, 0L) == Player.m_localPlayer.GetPlayerID();
                return canEdit
                    ? text + $"\nPress [<color=yellow><b>{editNoteKey.Value}</b></color>] to edit note"
                    : text + "\n<color=#AAAAAA>Only the player who placed this flag can edit its note.</color>";
            }

            public string GetHoverName()
            {
                Piece piece = GetComponent<Piece>();
                return piece ? piece.m_name : "Inspection Flag";
            }

            public float GetHoverOffset()
            {
                return 0f;
            }

            public void SetText(string text)
            {
                if (nview && nview.GetZDO() != null && Player.m_localPlayer != null &&
                    nview.GetZDO().GetLong(OwnerZdoKey, 0L) == Player.m_localPlayer.GetPlayerID())
                {
                    nview.InvokeRPC(NoteRpcName, (text ?? string.Empty).Trim());
                }
            }

            private void SetNoteRpc(long sender, string text)
            {
                if (nview && nview.IsOwner() && nview.GetZDO() != null &&
                    sender == nview.GetZDO().GetLong(OwnerPeerZdoKey, 0L))
                {
                    nview.GetZDO().Set(NoteZdoKey, text ?? string.Empty);
                }
            }

            public void SetPlacer(long playerId, long peerId)
            {
                if (nview && nview.GetZDO() != null && playerId != 0L && peerId != 0L)
                {
                    if (nview.IsOwner())
                    {
                        long currentOwnerId = nview.GetZDO().GetLong(OwnerZdoKey, 0L);
                        if (currentOwnerId == 0L || currentOwnerId == playerId)
                        {
                            nview.GetZDO().Set(OwnerZdoKey, playerId);
                            nview.GetZDO().Set(OwnerPeerZdoKey, peerId);
                        }
                    }
                    else
                    {
                        nview.InvokeRPC(OwnerRpcName, playerId, peerId);
                    }
                }
            }

            private void SetOwnerRpc(long sender, long playerId, long peerId)
            {
                if (nview && nview.IsOwner() && nview.GetZDO() != null && playerId != 0L &&
                    peerId != 0L && sender == peerId)
                {
                    long currentOwnerId = nview.GetZDO().GetLong(OwnerZdoKey, 0L);
                    if (currentOwnerId == 0L || currentOwnerId == playerId)
                    {
                        nview.GetZDO().Set(OwnerZdoKey, playerId);
                        nview.GetZDO().Set(OwnerPeerZdoKey, peerId);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Player), "Start")]
        private static class PlayerStartFlagCollisionPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                foreach (FlagNoteReceiver receiver in FlagReceivers)
                {
                    if (receiver)
                    {
                        receiver.IgnoreCollisionWith(__instance);
                    }
                }
            }
        }

        [HarmonyPatch]
        private static class PlayerPlaceFlagPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Player), "PlacePiece");
            }

            [HarmonyPostfix]
            private static void Postfix(Player __instance, object[] __args)
            {
                if (__instance == null || __args == null)
                {
                    return;
                }

                Piece placedPiece = null;
                foreach (object argument in __args)
                {
                    placedPiece = argument as Piece;
                    if (placedPiece)
                    {
                        break;
                    }
                }

                if (!placedPiece || !IsInspectionFlag(placedPiece))
                {
                    return;
                }

                FlagNoteReceiver receiver = placedPiece.GetComponent<FlagNoteReceiver>();
                if (receiver && ZNet.instance)
                {
                    receiver.SetPlacer(__instance.GetPlayerID(), ZNet.GetUID());
                }
            }
        }

        private static bool IsInspectionFlag(Piece piece)
        {
            if (!piece)
            {
                return false;
            }

            foreach (FlagVariant variant in flagVariants ?? Array.Empty<FlagVariant>())
            {
                if (piece.gameObject.name.StartsWith(variant.PrefabName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class PlacementGhostTiltPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                AlignFlagToSurface(__instance);
            }
        }
    }
}
