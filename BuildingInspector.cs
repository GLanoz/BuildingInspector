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
        public const string PluginVersion = "0.1.10";

        private static readonly List<Texture2D> IconTextures = new List<Texture2D>();
        private static Texture2D clothTexture;
        private static Sprite clothSprite;
        private static ConfigEntry<KeyCode> tiltForwardKey;
        private static ConfigEntry<KeyCode> tiltBackwardKey;
        private static ConfigEntry<KeyCode> tiltLeftKey;
        private static ConfigEntry<KeyCode> tiltRightKey;
        private static ConfigEntry<float> tiltStepDegrees;
        private static ConfigEntry<float> maxTiltDegrees;
        private static float tiltX;
        private static float tiltZ;
        private static GameObject lastTiltedGhost;
        private static Quaternion lastGhostBaseRotation;
        private static Quaternion lastGhostRotation;
        private static bool hasLastGhostRotation;

        private static readonly FlagVariant[] FlagVariants =
        {
            new FlagVariant("Inspection Flag - Problem", "Red flag: a building issue was found.", "InspectionFlagRed", new Color(0.98f, 0.18f, 0.12f)),
            new FlagVariant("Inspection Flag - Review", "Yellow flag: this part of the building needs review.", "InspectionFlagYellow", new Color(1f, 0.88f, 0.08f)),
            new FlagVariant("Inspection Flag - Verified", "Green flag: this part of the building was inspected.", "InspectionFlagGreen", new Color(0.12f, 0.9f, 0.2f)),
            new FlagVariant("Inspection Flag - Other", "Blue flag: another inspection category.", "InspectionFlagBlue", new Color(0.12f, 0.5f, 1f))
        };

        private void Awake()
        {
            Logger.LogInfo("Building Inspector loading...");
            tiltForwardKey = Config.Bind("Flag Rotation", "TiltForwardKey", KeyCode.I, "Tilt the flag forward.");
            tiltBackwardKey = Config.Bind("Flag Rotation", "TiltBackwardKey", KeyCode.K, "Tilt the flag backward.");
            tiltLeftKey = Config.Bind("Flag Rotation", "TiltLeftKey", KeyCode.J, "Tilt the flag to the left.");
            tiltRightKey = Config.Bind("Flag Rotation", "TiltRightKey", KeyCode.L, "Tilt the flag to the right.");
            tiltStepDegrees = Config.Bind("Flag Rotation", "TiltStepDegrees", 15f, "Tilt change per key press in degrees.");
            maxTiltDegrees = Config.Bind("Flag Rotation", "MaxTiltDegrees", 60f, "Maximum tilt angle in either direction.");
            new Harmony(PluginGUID).PatchAll(typeof(BuildingInspector).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += RegisterInspectionFlag;
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

            Material woodMaterial = sourceRenderer.sharedMaterial;
            int pieceLayer = LayerMask.NameToLayer("piece");
            int registeredCount = 0;

            foreach (FlagVariant variant in FlagVariants)
            {
                if (AddInspectionFlag(variant, woodMaterial, sourcePiece.m_placeEffect, pieceLayer))
                {
                    registeredCount++;
                }
            }

            if (registeredCount != FlagVariants.Length)
            {
                throw new InvalidOperationException($"Only {registeredCount} of {FlagVariants.Length} Inspection Flags were registered.");
            }

            return registeredCount;
        }

        private bool AddInspectionFlag(FlagVariant variant, Material woodMaterial,
            EffectList placeEffect, int pieceLayer)
        {
            GameObject prefab = PrefabManager.Instance.CreateEmptyPrefab(variant.PrefabName, true);
            if (!prefab)
            {
                throw new InvalidOperationException($"Jotunn could not create the {variant.PrefabName} prefab.");
            }

            prefab.transform.localScale = Vector3.one * 0.5f;
            prefab.AddComponent<Piece>();

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

            AddPrimitive(prefab, PrimitiveType.Cylinder, "InspectionFlagPole",
                new Vector3(0f, 0.58f, 0f), new Vector3(0.027f, 0.68f, 0.027f),
                woodMaterial, pieceLayer);
            AddClothSprite(prefab, variant, pieceLayer);

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
            spriteRenderer.color = variant.Color;
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
                if (Input.GetKeyDown(tiltForwardKey.Value))
                {
                    tiltX = Mathf.Clamp(tiltX + tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (Input.GetKeyDown(tiltBackwardKey.Value))
                {
                    tiltX = Mathf.Clamp(tiltX - tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (Input.GetKeyDown(tiltLeftKey.Value))
                {
                    tiltZ = Mathf.Clamp(tiltZ - tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (Input.GetKeyDown(tiltRightKey.Value))
                {
                    tiltZ = Mathf.Clamp(tiltZ + tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }

                ghost.transform.rotation = surfaceRotation * Quaternion.Euler(tiltX, 0f, tiltZ);
                ghost.transform.position -= hit.normal * 0.12f;
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