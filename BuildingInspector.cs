using BepInEx;
using System;
using System.Collections.Generic;
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
        public const string PluginVersion = "0.1.7";

        private static readonly List<Texture2D> IconTextures = new List<Texture2D>();
        private static float flagTiltX;
        private static float flagTiltZ;
        private static GameObject lastTiltedGhost;
        private static Quaternion lastGhostBaseRotation;
        private static Quaternion lastGhostRotation;
        private static bool hasLastGhostRotation;

        private static readonly FlagVariant[] FlagVariants =
        {
            new FlagVariant("Inspection Flag - Problem", "Red flag: a building issue was found.", "InspectionFlagRed", "piece_banner04", new Color(0.98f, 0.18f, 0.12f)),
            new FlagVariant("Inspection Flag - Review", "Yellow flag: this part of the building needs review.", "InspectionFlagYellow", "piece_banner08", new Color(1f, 0.88f, 0.08f)),
            new FlagVariant("Inspection Flag - Verified", "Green flag: this part of the building was inspected.", "InspectionFlagGreen", "piece_banner05", new Color(0.12f, 0.9f, 0.2f)),
            new FlagVariant("Inspection Flag - Other", "Blue flag: another inspection category.", "InspectionFlagBlue", "piece_banner02", new Color(0.12f, 0.5f, 1f))
        };

        private void Awake()
        {
            Logger.LogInfo("Building Inspector loading...");
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

            Material woodMaterial = CreateTintedMaterial(sourceRenderer.sharedMaterial, new Color(0.72f, 0.55f, 0.34f));
            GameObject whiteBannerSource = PrefabManager.Cache.GetPrefab<GameObject>("piece_banner11");
            Renderer whiteBannerRenderer = whiteBannerSource ? whiteBannerSource.GetComponentInChildren<Renderer>() : null;
            Material fallbackClothMaterial = whiteBannerRenderer && whiteBannerRenderer.sharedMaterial
                ? whiteBannerRenderer.sharedMaterial
                : sourceRenderer.sharedMaterial;
            int pieceLayer = LayerMask.NameToLayer("piece");
            int registeredCount = 0;

            foreach (FlagVariant variant in FlagVariants)
            {
                if (AddInspectionFlag(variant, woodMaterial, fallbackClothMaterial, sourcePiece.m_placeEffect, pieceLayer))
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
            Material fallbackClothMaterial, EffectList placeEffect, int pieceLayer)
        {
            GameObject prefab = PrefabManager.Instance.CreateEmptyPrefab(variant.PrefabName, true);
            if (!prefab)
            {
                throw new InvalidOperationException($"Jotunn could not create the {variant.PrefabName} prefab.");
            }

            prefab.transform.localScale = Vector3.one * 0.65f;
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

            AddPrimitive(prefab, PrimitiveType.Cylinder, "InspectionFlagBase",
                new Vector3(0f, 0.04f, 0f), new Vector3(0.21f, 0.04f, 0.21f),
                woodMaterial, pieceLayer);
            AddPrimitive(prefab, PrimitiveType.Cylinder, "InspectionFlagPole",
                new Vector3(0f, 0.72f, 0f), new Vector3(0.027f, 0.68f, 0.027f),
                woodMaterial, pieceLayer);
            GameObject bannerSource = PrefabManager.Cache.GetPrefab<GameObject>(variant.BannerPrefabName);
            Renderer bannerRenderer = bannerSource ? bannerSource.GetComponentInChildren<Renderer>() : null;
            Material clothMaterial = bannerRenderer && bannerRenderer.sharedMaterial
                ? new Material(bannerRenderer.sharedMaterial)
                : CreateTintedMaterial(fallbackClothMaterial, variant.Color);
            MaterialPropertyBlock clothProperties = null;
            if (bannerRenderer)
            {
                clothProperties = new MaterialPropertyBlock();
                bannerRenderer.GetPropertyBlock(clothProperties);
            }

            AddPrimitive(prefab, PrimitiveType.Cube, "InspectionFlagCloth",
                new Vector3(0.2f, 1.15f, 0f), new Vector3(0.39f, 0.24f, 0.027f),
                clothMaterial, pieceLayer, clothProperties);

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
            Vector3 localPosition, Vector3 localScale, Material material, int pieceLayer,
            MaterialPropertyBlock materialProperties = null)
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
                if (materialProperties != null)
                {
                    renderer.SetPropertyBlock(materialProperties);
                }
            }
        }

        private static void UpdateFlagTilt(Player player)
        {
            GameObject ghost = AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player) as GameObject;
            if (!ghost || !ghost.name.StartsWith("InspectionFlag", StringComparison.Ordinal))
            {
                lastTiltedGhost = null;
                hasLastGhostRotation = false;
                return;
            }

            bool isNewGhost = ghost != lastTiltedGhost;
            if (isNewGhost)
            {
                flagTiltX = 0f;
                flagTiltZ = 0f;
                hasLastGhostRotation = false;
            }

            Quaternion currentRotation = ghost.transform.rotation;
            Quaternion baseRotation = !isNewGhost && hasLastGhostRotation &&
                Quaternion.Angle(currentRotation, lastGhostRotation) < 0.1f
                ? lastGhostBaseRotation
                : currentRotation;

            const float tiltStep = 15f;
            const float maxTilt = 60f;
            if (ZInput.GetKeyDown(KeyCode.PageUp, false))
            {
                flagTiltX = Mathf.Clamp(flagTiltX + tiltStep, -maxTilt, maxTilt);
            }
            if (ZInput.GetKeyDown(KeyCode.PageDown, false))
            {
                flagTiltX = Mathf.Clamp(flagTiltX - tiltStep, -maxTilt, maxTilt);
            }
            if (ZInput.GetKeyDown(KeyCode.Home, false))
            {
                flagTiltZ = Mathf.Clamp(flagTiltZ - tiltStep, -maxTilt, maxTilt);
            }
            if (ZInput.GetKeyDown(KeyCode.End, false))
            {
                flagTiltZ = Mathf.Clamp(flagTiltZ + tiltStep, -maxTilt, maxTilt);
            }
            if (ZInput.GetKeyDown(KeyCode.Insert, false))
            {
                flagTiltX = 0f;
                flagTiltZ = 0f;
            }

            ghost.transform.rotation = baseRotation * Quaternion.Euler(flagTiltX, 0f, flagTiltZ);
            lastTiltedGhost = ghost;
            lastGhostBaseRotation = baseRotation;
            lastGhostRotation = ghost.transform.rotation;
            hasLastGhostRotation = true;
        }

        private static Material CreateTintedMaterial(Material source, Color tint)
        {
            Material material = new Material(source);
            if (material.HasProperty("_Color"))
            {
                material.color = tint;
            }
            else if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", tint);
            }

            return material;
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
            public string BannerPrefabName { get; }
            public Color Color { get; }

            public FlagVariant(string displayName, string description, string prefabName,
                string bannerPrefabName, Color color)
            {
                DisplayName = displayName;
                Description = description;
                PrefabName = prefabName;
                BannerPrefabName = bannerPrefabName;
                Color = color;
            }
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class PlacementGhostTiltPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                UpdateFlagTilt(__instance);
            }
        }
    }
}