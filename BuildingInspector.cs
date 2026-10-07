using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuildingInspector
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    public class BuildingInspector : BaseUnityPlugin
    {
        public const string PluginGUID = "Lanoz.BuildingInspector";
        public const string PluginName = "Building Inspector";
        public const string PluginVersion = "0.2.7";

        private const int FlagVariantCount = 6;
        private const int MaxNoteLength = 160;
        private const int MaxNoteLines = 5;
        private const string NoteRpcName = "BuildingInspector_SetFlagNote";
        private const string NoteZdoKey = "BuildingInspector_FlagNote";
        private const string ChangeCategoryRpcName = "BuildingInspector_ChangeFlagCategory";
        private const string FlagCategoryZdoKey = "BuildingInspector_FlagCategory";
        private const string CreatorNameZdoKey = "BuildingInspector_FlagCreatorName";
        private const string MultiplayerWorldGlobalKey = "BuildingInspector_MultiplayerWorld";

        private static ManualLogSource log;
        private static readonly List<Texture2D> IconTextures = new List<Texture2D>();
        private static readonly List<FlagNoteReceiver> FlagReceivers = new List<FlagNoteReceiver>();
        private static Texture2D clothTexture;
        private static ConfigEntry<KeyboardShortcut> tiltForwardKey;
        private static ConfigEntry<KeyboardShortcut> tiltBackwardKey;
        private static ConfigEntry<KeyboardShortcut> tiltLeftKey;
        private static ConfigEntry<KeyboardShortcut> tiltRightKey;
        private static ConfigEntry<KeyboardShortcut> resetTiltKey;
        private static ConfigEntry<float> tiltStepDegrees;
        private static ConfigEntry<float> maxTiltDegrees;
        private static ConfigEntry<KeyboardShortcut> editNoteKey;
        private static ConfigEntry<float> flagBlockRadius;
        private static ConfigEntry<bool> testFlagEnabled;
        private static readonly List<ConfigEntry<string>> labelNames = new List<ConfigEntry<string>>();
        private static readonly List<ConfigEntry<string>> labelDescriptions = new List<ConfigEntry<string>>();
        private static readonly List<ConfigEntry<string>> labelColors = new List<ConfigEntry<string>>();
        private static FlagVariant[] flagVariants;
        private static FlagVariant testFlagVariant;
        private static float tiltX;
        private static float tiltZ;
        private static float surfaceWheelRotation;
        private static GameObject lastTiltedGhost;
        private static bool flagEditorOpen;
        private static bool flagEditorInputBlocked;
        private static bool flagEditorInputUnblockPending;
        private static int flagEditorUnblockFrame;
        private static FlagNoteReceiver flagEditorTarget;
        private static GameObject flagEditorCanvas;
        private static GameObject flagEditorPanel;
        private static InputField flagEditorNoteField;
        private static Button[] flagEditorCategoryButtons;
        private static Image[] flagEditorCategoryButtonImages;
        private static string originalNote;
        private static int originalCategory;
        private static int draftCategory;
        private static CursorLockMode editorPreviousCursorLock;
        private static bool editorPreviousCursorVisible;

        private void Awake()
        {
            log = Logger;
            Logger.LogInfo("Building Inspector loading...");
            tiltForwardKey = Config.Bind("Flag Rotation", "TiltForwardKey", new KeyboardShortcut(KeyCode.I), "Tilt the flag forward.");
            tiltBackwardKey = Config.Bind("Flag Rotation", "TiltBackwardKey", new KeyboardShortcut(KeyCode.K), "Tilt the flag backward.");
            tiltLeftKey = Config.Bind("Flag Rotation", "TiltLeftKey", new KeyboardShortcut(KeyCode.J), "Tilt the flag to the left.");
            tiltRightKey = Config.Bind("Flag Rotation", "TiltRightKey", new KeyboardShortcut(KeyCode.L), "Tilt the flag to the right.");
            resetTiltKey = Config.Bind("Flag Rotation", "ResetTiltKey", new KeyboardShortcut(KeyCode.Insert), "Reset the flag tilt.");
            tiltStepDegrees = Config.Bind("Flag Rotation", "TiltStepDegrees", 15f, "Tilt change per key press in degrees.");
            maxTiltDegrees = Config.Bind("Flag Rotation", "MaxTiltDegrees", 60f, "Maximum tilt angle in either direction.");
            editNoteKey = Config.Bind("Flag Notes", "EditNoteKey", new KeyboardShortcut(KeyCode.N), "Edit the note on the aimed inspection flag.");
            if (Config.Remove(new ConfigDefinition("Flag Categories", "ChangeCategoryKey")))
            {
                Config.Save();
            }

            flagBlockRadius = Config.Bind("Flag Placement", "BlockRadius", 0.2f,
                new ConfigDescription("Minimum spacing between placed inspection flags in meters.", new AcceptableValueRange<float>(0.05f, 1f)));
            BindFlagVariants();
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
            string[] prefabNames = { "InspectionFlagRed", "InspectionFlagYellow", "InspectionFlagGreen", "InspectionFlagBlue", "InspectionFlagPurple", "InspectionFlagWhite" };
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

            testFlagEnabled = Config.Bind("Test Flag", "Enabled", false,
                "Register an isolated test flag for checking label configuration. Restart the game after changing this setting.");
            string testName = Config.Bind("Test Flag", "Name", "Configuration Test",
                "Display name for the optional test flag.").Value;
            string testDescription = Config.Bind("Test Flag", "Description", "Temporary flag for testing label settings.",
                "Description for the optional test flag.").Value;
            string testColorValue = Config.Bind("Test Flag", "Color", "#FF00FF",
                "Hex color for the optional test flag, for example #FF00FF.").Value;
            Color testColor;
            if (!ColorUtility.TryParseHtmlString(testColorValue, out testColor))
            {
                testColor = Color.magenta;
                Logger.LogWarning($"Invalid Test Flag Color '{testColorValue}'; using #FF00FF.");
            }

            testName = string.IsNullOrWhiteSpace(testName) ? "Configuration Test" : testName.Trim();
            testFlagVariant = new FlagVariant($"[TEST] Inspection Flag - {testName}",
                testDescription ?? string.Empty, "InspectionFlagConfigTest", testColor);
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
            if (flagEditorOpen)
            {
                if (!flagEditorTarget || !flagEditorTarget.CanEditCategory() || Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseFlagEditor();
                }
                return;
            }

            if (flagEditorInputUnblockPending)
            {
                if (Time.frameCount <= flagEditorUnblockFrame || Input.GetMouseButton(0) || Input.GetMouseButton(1))
                {
                    return;
                }

                GUIManager.BlockInput(false);
                flagEditorInputBlocked = false;
                flagEditorInputUnblockPending = false;
            }

            if (!editNoteKey.Value.IsDown() || TextInput.IsVisible() || InventoryGui.IsVisible() ||
                Menu.IsVisible() || Console.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen())
            {
                return;
            }

            Camera camera = Camera.main;
            if (!camera)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            float cameraOffset = Player.m_localPlayer
                ? Vector3.Distance(camera.transform.position, Player.m_localPlayer.transform.position)
                : 0f;
            float maxDistance = 6f + cameraOffset;
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
            foreach (RaycastHit hit in hits)
            {
                FlagNoteReceiver receiver = hit.collider.GetComponentInParent<FlagNoteReceiver>();
                if (receiver)
                {
                    if (!receiver.CanEditCategory())
                    {
                        return;
                    }

                    OpenFlagEditor(receiver);
                    return;
                }

                if (BlocksNoteRay(hit.collider))
                {
                    return;
                }
            }
        }

        private void LateUpdate()
        {
            if (!flagEditorInputBlocked)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer)
            {
                localPlayer.SetControls(Vector3.zero, false, false, false, false, false, false,
                    false, false, false, false, false);
                localPlayer.SetMoveDir(Vector3.zero);
                localPlayer.StopMovement();
            }
        }

        private void OpenFlagEditor(FlagNoteReceiver receiver)
        {
            if (!receiver || !receiver.CanEditCategory() || !EnsureFlagEditorUi())
            {
                return;
            }

            flagEditorTarget = receiver;
            originalNote = receiver.GetText();
            originalCategory = receiver.GetEditorCategoryIndex();
            draftCategory = originalCategory;
            flagEditorNoteField.text = originalNote;
            UpdateFlagEditorCategorySelection();
            flagEditorCanvas.SetActive(true);
            flagEditorOpen = true;
            flagEditorInputBlocked = true;
            editorPreviousCursorLock = Cursor.lockState;
            editorPreviousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GUIManager.BlockInput(true);

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem)
            {
                eventSystem.SetSelectedGameObject(flagEditorNoteField.gameObject);
            }

            flagEditorNoteField.ActivateInputField();
        }

        private bool EnsureFlagEditorUi()
        {
            if (flagEditorCanvas)
            {
                return true;
            }

            GUIManager gui = GUIManager.Instance;
            GameObject customGui = GUIManager.CustomGUIFront;
            if (gui == null || !customGui)
            {
                return false;
            }

            flagEditorCanvas = new GameObject("BuildingInspectorFlagEditor", typeof(RectTransform));
            flagEditorCanvas.transform.SetParent(customGui.transform, false);
            RectTransform rootRect = flagEditorCanvas.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            flagEditorCanvas.SetActive(false);

            GameObject backdropObject = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdropObject.transform.SetParent(flagEditorCanvas.transform, false);
            RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            Image backdrop = backdropObject.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.58f);
            backdrop.raycastTarget = true;

            flagEditorPanel = gui.CreateWoodpanel(flagEditorCanvas.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, 740f, 560f, false);
            RectTransform panelRect = flagEditorPanel.GetComponent<RectTransform>();
            panelRect.SetAsLastSibling();

            CreateEditorText(gui, "INSPECTION FLAG", flagEditorPanel.transform, new Vector2(0f, 238f),
                new Vector2(650f, 40f), 30, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.15f));
            CreateEditorText(gui, "Note", flagEditorPanel.transform, new Vector2(0, 185f),
                new Vector2(620f, 28f), 22, TextAnchor.MiddleLeft, Color.white);

            GameObject inputObject = gui.CreateInputField(flagEditorPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 98f),
                InputField.ContentType.Standard, "Add an inspection note...", 18, 630f, 125f);
            flagEditorNoteField = inputObject.GetComponent<InputField>();
            flagEditorNoteField.onValidateInput += ValidateNoteInput;
            flagEditorNoteField.lineType = InputField.LineType.MultiLineNewline;
            flagEditorNoteField.characterLimit = 0;
            flagEditorNoteField.text = string.Empty;
            flagEditorNoteField.textComponent.alignment = TextAnchor.UpperLeft;
            flagEditorNoteField.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            flagEditorNoteField.textComponent.verticalOverflow = VerticalWrapMode.Overflow;

            CreateEditorText(gui, "Category", flagEditorPanel.transform, new Vector2(0, 10f),
                new Vector2(620f, 28f), 22, TextAnchor.MiddleLeft, Color.white);
            flagEditorCategoryButtons = new Button[FlagVariantCount];
            flagEditorCategoryButtonImages = new Image[FlagVariantCount];
            for (int index = 0; index < FlagVariantCount; index++)
            {
                int row = index / 3;
                int column = index % 3;
                Vector2 position = new Vector2((column - 1) * 222f, -55f - row * 75f);
                GameObject buttonObject = gui.CreateButton(string.Empty, flagEditorPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, 210f, 62f);
                Button button = buttonObject.GetComponent<Button>();
                Image buttonImage = buttonObject.GetComponent<Image>();
                button.transition = Selectable.Transition.None;
                flagEditorCategoryButtons[index] = button;
                flagEditorCategoryButtonImages[index] = buttonImage;
                int categoryIndex = index;
                button.onClick.AddListener(() => SetDraftCategory(categoryIndex));

                GameObject iconObject = new GameObject("FlagIcon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(buttonObject.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                iconRect.sizeDelta = new Vector2(45f, 45f);
                Image icon = iconObject.GetComponent<Image>();
                icon.sprite = flagVariants[index].Icon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                string categoryName = string.IsNullOrWhiteSpace(labelNames[index].Value)
                    ? $"Category {index + 1}"
                    : labelNames[index].Value.Trim();
                CreateEditorText(gui, categoryName, buttonObject.transform, new Vector2(25f, 0f),
                    new Vector2(135f, 52f), 16, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.15f));
            }

            GameObject saveObject = gui.CreateButton("Save", flagEditorPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(100f, -230f), 180f, 48f);
            saveObject.GetComponent<Button>().onClick.AddListener(SaveFlagEditor);
            GameObject cancelObject = gui.CreateButton("Cancel", flagEditorPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100f, -230f), 180f, 48f);
            cancelObject.GetComponent<Button>().onClick.AddListener(CloseFlagEditor);
            UpdateFlagEditorCategorySelection();
            return true;
        }

        private static void CreateEditorText(GUIManager gui, string text, Transform parent, Vector2 position,
            Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            GameObject textObject = gui.CreateText(text, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, gui.AveriaSerif, fontSize, Color.white,
                true, Color.black, size.x, size.y, false);
            Text label = textObject.GetComponent<Text>();
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
        }

        private static void SetDraftCategory(int categoryIndex)
        {
            if (categoryIndex < 0 || categoryIndex >= FlagVariantCount)
            {
                return;
            }

            draftCategory = categoryIndex;
            UpdateFlagEditorCategorySelection();
        }

        private static void UpdateFlagEditorCategorySelection()
        {
            if (flagEditorCategoryButtons == null)
            {
                return;
            }

            for (int index = 0; index < flagEditorCategoryButtons.Length; index++)
            {
                bool selected = index == draftCategory;
                Image image = flagEditorCategoryButtonImages[index];
                if (image)
                {
                    image.color = selected ? new Color(0.62f, 0.43f, 0.2f, 1f) : Color.white;
                }

                flagEditorCategoryButtons[index].interactable = true;
            }
        }

        private static string LimitNoteLength(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int characterCount = 0;
            System.Text.StringBuilder result = new System.Text.StringBuilder(text.Length);

            foreach (char character in text)
            {
                if (character == '\r' || character == '\n')
                {
                    result.Append(character);
                    continue;
                }

                if (characterCount >= MaxNoteLength)
                {
                    continue;
                }

                characterCount++;
                result.Append(character);
            }

            return result.ToString();
        }
        private static void SaveFlagEditor()
        {
            if (!flagEditorTarget || !flagEditorTarget.CanEditCategory())
            {
                CloseFlagEditor();
                return;
            }

            string note = LimitNoteLength((flagEditorNoteField.text ?? string.Empty).Trim());
            if (!string.Equals(note, originalNote, StringComparison.Ordinal))
            {
                flagEditorTarget.SetText(note);
            }

            if (draftCategory != originalCategory)
            {
                flagEditorTarget.RequestCategoryChange(draftCategory);
            }

            CloseFlagEditor();
        }

        private static void CloseFlagEditor()
        {
            if (!flagEditorOpen)
            {
                return;
            }

            flagEditorOpen = false;
            flagEditorTarget = null;
            if (flagEditorCanvas)
            {
                flagEditorCanvas.SetActive(false);
            }

            flagEditorInputUnblockPending = true;
            flagEditorUnblockFrame = Time.frameCount;
            Cursor.lockState = editorPreviousCursorLock;
            Cursor.visible = editorPreviousCursorVisible;
            originalNote = string.Empty;
            originalCategory = -1;
            draftCategory = -1;
        }

        private static bool ShouldShowInspectedBy()
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem && zoneSystem.GetGlobalKey(MultiplayerWorldGlobalKey))
            {
                return true;
            }

            ZNet net = ZNet.instance;
            if (!net)
            {
                return false;
            }

            if (net.IsServer() && ZNet.IsOpenServer())
            {
                MarkMultiplayerWorldIfNeeded(net);
                return true;
            }

            bool connectedClient = !net.IsServer() && net.GetServerPeer() != null;
            return connectedClient && zoneSystem && zoneSystem.GetGlobalKey(MultiplayerWorldGlobalKey);
        }

        private static void MarkMultiplayerWorldIfNeeded(ZNet net)
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem && net && net.IsServer() && ZNet.IsOpenServer() &&
                !zoneSystem.GetGlobalKey(MultiplayerWorldGlobalKey))
            {
                zoneSystem.SetGlobalKey(MultiplayerWorldGlobalKey);
            }
        }

        private static bool BlocksNoteRay(Collider collider)
        {
            if (!collider)
            {
                return false;
            }

            if (collider.GetComponentInParent<Piece>() || collider.GetComponentInParent<WearNTear>() ||
                collider.GetComponentInParent<Heightmap>() || collider.GetComponentInParent<Character>() ||
                collider.GetComponentInParent<TreeLog>())
            {
                return true;
            }

            TreeBase tree = collider.GetComponentInParent<TreeBase>();
            return tree && collider.gameObject == tree.gameObject;
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

            if (testFlagEnabled.Value)
            {
                if (AddInspectionFlag(testFlagVariant, woodMaterial, sourcePiece.m_placeEffect, sourceWearNTear, pieceLayer))
                {
                    Logger.LogInfo("Optional configuration test flag registered.");
                }
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

            // Root origin is the surface attachment point; the pole starts at local Y = 0.
            GameObject pole = AddPrimitive(prefab, PrimitiveType.Cylinder, "InspectionFlagPole",
                new Vector3(0f, 0.34f, 0f), new Vector3(0.027f, 0.68f, 0.027f),
                woodMaterial, pieceLayer);
            AddClothSprite(pole, woodMaterial, variant, pieceLayer);

            WearNTear wearNTear = prefab.AddComponent<WearNTear>();
            wearNTear.m_new = pole;
            wearNTear.m_worn = pole;
            wearNTear.m_broken = pole;
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
            variant.Icon = config.Icon;

            var customPiece = new CustomPiece(prefab, false, config);
            customPiece.Piece.m_resources = Array.Empty<Piece.Requirement>();
            customPiece.Piece.m_placeEffect = placeEffect;
            // Keep native rotation enabled so Valheim does not treat the wheel as camera zoom.
            // AlignFlagToSurface replaces its world-axis rotation with a surface-relative one.
            customPiece.Piece.m_canRotate = true;

            if (!PieceManager.Instance.AddPiece(customPiece))
            {
                throw new InvalidOperationException($"Jotunn's PieceManager rejected {variant.PrefabName}.");
            }

            return true;
        }

        private static GameObject AddPrimitive(GameObject parent, PrimitiveType type, string name,
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

            return part;
        }

        private static void AddClothSprite(GameObject pole, Material woodMaterial, FlagVariant variant, int pieceLayer)
        {
            if (!clothTexture)
            {
                const int textureSize = 16;
                clothTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[textureSize * textureSize];
                for (int y = 0; y < textureSize; y++)
                {
                    for (int x = 0; x < textureSize; x++)
                    {
                        bool warpThread = x % 4 == 0;
                        bool weftThread = y % 4 == 0;
                        byte shade = warpThread && weftThread ? (byte)236 :
                            warpThread || weftThread ? (byte)246 : (byte)255;
                        pixels[y * textureSize + x] = new Color32(shade, shade, shade, 255);
                    }
                }

                clothTexture.SetPixels32(pixels);
                clothTexture.Apply(false, true);
                clothTexture.filterMode = FilterMode.Bilinear;
            }

            Material clothMaterial = new Material(woodMaterial);
            clothMaterial.name = $"{variant.PrefabName}_ClothMaterial";
            clothMaterial.mainTexture = clothTexture;
            Color clothColor = variant.Color;
            clothColor.a = Mathf.Min(clothColor.a, 0.85f);
            clothMaterial.color = clothColor;
            if (clothMaterial.HasProperty("_Mode"))
            {
                clothMaterial.SetFloat("_Mode", 2f);
            }
            if (clothMaterial.HasProperty("_SrcBlend"))
            {
                clothMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }
            if (clothMaterial.HasProperty("_DstBlend"))
            {
                clothMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (clothMaterial.HasProperty("_ZWrite"))
            {
                clothMaterial.SetInt("_ZWrite", 0);
            }
            if (clothMaterial.HasProperty("_Cull"))
            {
                clothMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            if (clothMaterial.HasProperty("_Metallic"))
            {
                clothMaterial.SetFloat("_Metallic", 0f);
            }
            if (clothMaterial.HasProperty("_Glossiness"))
            {
                clothMaterial.SetFloat("_Glossiness", 0f);
            }
            if (clothMaterial.HasProperty("_EmissionColor"))
            {
                clothMaterial.SetColor("_EmissionColor", Color.black);
            }
            clothMaterial.DisableKeyword("_EMISSION");
            clothMaterial.DisableKeyword("_ALPHATEST_ON");
            clothMaterial.EnableKeyword("_ALPHABLEND_ON");
            clothMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            clothMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            GameObject cloth = new GameObject("InspectionFlagCloth");
            // Keep the cloth geometry in pole-local space so every pole rotation carries it rigidly.
            cloth.transform.SetParent(pole.transform, false);
            const float clothWidth = 0.39f;
            const float clothHeight = 0.24f;
            MeshFilter poleMesh = pole ? pole.GetComponent<MeshFilter>() : null;
            Vector3 poleScale = pole.transform.localScale;
            float poleScaleX = Mathf.Max(Mathf.Abs(poleScale.x), 0.0001f);
            float poleScaleY = Mathf.Max(Mathf.Abs(poleScale.y), 0.0001f);
            Bounds poleBounds = poleMesh && poleMesh.sharedMesh
                ? poleMesh.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            float clothWidthInPoleSpace = clothWidth / poleScaleX;
            float clothHeightInPoleSpace = clothHeight / poleScaleY;
            float halfClothHeightInPoleSpace = clothHeightInPoleSpace * 0.5f;
            const float clothThickness = 0.003f;
            float poleWorldScaleZ = Mathf.Max(Mathf.Abs(pole.transform.lossyScale.z), 0.0001f);
            float halfClothDepthInPoleSpace = clothThickness / poleWorldScaleZ * 0.5f;
            cloth.transform.localPosition = new Vector3(
                -poleBounds.extents.x,
                poleBounds.max.y - halfClothHeightInPoleSpace,
                0f);
            if (pieceLayer >= 0)
            {
                cloth.layer = pieceLayer;
            }

            Mesh clothMesh = new Mesh();
            clothMesh.name = "InspectionFlagClothMesh";
            clothMesh.vertices = new[]
            {
                new Vector3(0f, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace),
                new Vector3(0f, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, halfClothDepthInPoleSpace),
                new Vector3(clothWidthInPoleSpace, -halfClothHeightInPoleSpace, -halfClothDepthInPoleSpace)
            };
            clothMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            clothMesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23
            };
            clothMesh.RecalculateNormals();
            clothMesh.RecalculateBounds();
            cloth.AddComponent<MeshFilter>().sharedMesh = clothMesh;
            MeshRenderer clothRenderer = cloth.AddComponent<MeshRenderer>();
            clothRenderer.sharedMaterial = clothMaterial;
        }

        private static void AlignFlagToSurface(Player player)
        {
            GameObject ghost = AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player) as GameObject;
            if (!ghost || !ghost.name.StartsWith("InspectionFlag", StringComparison.Ordinal))
            {
                lastTiltedGhost = null;
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
                surfaceWheelRotation = 0f;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                surfaceWheelRotation += Mathf.Sign(scroll) * 22.5f;
            }

            // Face the cloth toward the player; mouse-wheel rotation is applied around the surface normal.
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.position - hit.point, hit.normal);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(-ray.direction, hit.normal);
            }

            if (forward.sqrMagnitude > 0.001f)
            {
                Quaternion surfaceRotation = Quaternion.LookRotation(forward.normalized, hit.normal);
                surfaceRotation = Quaternion.AngleAxis(surfaceWheelRotation, hit.normal) * surfaceRotation;
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
                // Root pivot is the pole base, so rotation leaves the surface anchor in place.
                ghost.transform.position = hit.point;
                lastTiltedGhost = ghost;
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
            public Sprite Icon { get; set; }

            public FlagVariant(string displayName, string description, string prefabName, Color color)
            {
                DisplayName = displayName;
                Description = description;
                PrefabName = prefabName;
                Color = color;
            }
        }

        private sealed class FlagNoteReceiver : MonoBehaviour, Hoverable
        {
            private ZNetView nview;
            private MeshRenderer clothRenderer;
            private int appliedCategory = -1;

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
                    nview.Register<int>(ChangeCategoryRpcName, ChangeCategoryRpc);
                }

                Transform clothTransform = transform.Find("InspectionFlagPole/InspectionFlagCloth");
                clothRenderer = clothTransform ? clothTransform.GetComponent<MeshRenderer>() : null;
                ApplyPlayerCollisionIgnores();
            }

            private void Update()
            {
                int categoryIndex = GetCategoryIndex();
                if (categoryIndex < 0 || categoryIndex == appliedCategory)
                {
                    return;
                }

                int prefabCategoryIndex = GetPrefabCategoryIndex();
                if (appliedCategory < 0 && prefabCategoryIndex == categoryIndex)
                {
                    appliedCategory = categoryIndex;
                    return;
                }

                ApplyCategoryAppearance(categoryIndex);
                appliedCategory = categoryIndex;
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
                Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (Player player in players)
                {
                    IgnoreCollisionWith(player);
                }
            }

            private long GetCreatorId()
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                return piece ? piece.GetCreator() : 0L;
            }

            private int GetCategoryIndex()
            {
                ZDO zdo = nview ? nview.GetZDO() : null;
                if (zdo == null)
                {
                    return -1;
                }

                int storedCategory = zdo.GetInt(FlagCategoryZdoKey, -1);
                return storedCategory >= 0 && storedCategory < flagVariants.Length
                    ? storedCategory
                    : GetPrefabCategoryIndex();
            }

            public int GetEditorCategoryIndex()
            {
                return GetCategoryIndex();
            }

            private int GetPrefabCategoryIndex()
            {
                ZDO zdo = nview ? nview.GetZDO() : null;
                ZNetScene scene = ZNetScene.instance;
                GameObject prefab = zdo != null && scene ? scene.GetPrefab(zdo.GetPrefab()) : null;
                if (!prefab || flagVariants == null)
                {
                    return -1;
                }

                for (int index = 0; index < flagVariants.Length; index++)
                {
                    if (string.Equals(prefab.name, flagVariants[index].PrefabName, StringComparison.Ordinal))
                    {
                        return index;
                    }
                }

                return -1;
            }

            private string GetCategoryDisplayName()
            {
                int categoryIndex = GetCategoryIndex();
                if (categoryIndex >= 0 && categoryIndex < labelNames.Count)
                {
                    string categoryName = string.IsNullOrWhiteSpace(labelNames[categoryIndex].Value)
                        ? $"Category {categoryIndex + 1}"
                        : labelNames[categoryIndex].Value.Trim();
                    return $"Inspection Flag - {categoryName}";
                }

                Piece piece = GetComponent<Piece>();
                return piece ? piece.m_name : "Inspection Flag";
            }

            private void ApplyCategoryAppearance(int categoryIndex)
            {
                if (clothRenderer && flagVariants != null && categoryIndex < flagVariants.Length)
                {
                    Color color = flagVariants[categoryIndex].Color;
                    color.a = Mathf.Min(color.a, 0.85f);
                    clothRenderer.material.color = color;
                }
            }

            public bool CanEditCategory()
            {
                if (!nview)
                {
                    nview = GetComponent<ZNetView>();
                }

                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                ZDO zdo = nview ? nview.GetZDO() : null;
                long creatorId = GetCreatorId();
                bool isCreator = piece && piece.IsCreator();
                return nview && zdo != null && piece && creatorId != 0L && isCreator;
            }

            public void RequestCategoryChange(int categoryIndex)
            {
                if (categoryIndex < 0 || categoryIndex >= flagVariants.Length || !CanEditCategory())
                {
                    return;
                }

                nview.InvokeRPC(ChangeCategoryRpcName, categoryIndex);
            }

            private void ChangeCategoryRpc(long sender, int categoryIndex)
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                long creatorId = GetCreatorId();
                bool isOwner = nview && nview.IsOwner();
                bool validSender = piece && IsSenderPlayer(sender, piece, creatorId);
                ZDO zdo = nview ? nview.GetZDO() : null;

                if (categoryIndex >= 0 && categoryIndex < flagVariants.Length &&
                    isOwner && zdo != null && validSender)
                {
                    zdo.Set(FlagCategoryZdoKey, categoryIndex);
                }
                else
                {
                    log.LogWarning("Rejected inspection flag category update.");
                }
            }

            public void SaveCreatorName(string creatorName)
            {
                Piece piece = GetComponent<Piece>();
                ZDO zdo = nview ? nview.GetZDO() : null;
                long creatorId = piece ? piece.GetCreator() : 0L;

                if (piece && zdo != null && !string.IsNullOrWhiteSpace(creatorName) &&
                    Player.m_localPlayer && Player.m_localPlayer.GetPlayerID() == creatorId)
                {
                    zdo.Set(CreatorNameZdoKey, creatorName);
                }

            }

            private string GetCreatorName()
            {
                ZDO zdo = nview ? nview.GetZDO() : null;
                string savedName = zdo != null ? zdo.GetString(CreatorNameZdoKey, string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(savedName))
                {
                    return savedName;
                }

                long creatorId = GetCreatorId();
                Player localPlayer = Player.m_localPlayer;
                bool localPlayerFound = localPlayer && localPlayer.GetPlayerID() == creatorId;

                if (creatorId == 0L)
                {
                    return "Odin";
                }

                string resolvedName = string.Empty;
                if (localPlayerFound)
                {
                    string localName = localPlayer.GetPlayerName();
                    if (!string.IsNullOrWhiteSpace(localName))
                    {
                        resolvedName = localName;
                    }
                }

                Player creatorPlayer = Player.GetPlayer(creatorId);
                if (string.IsNullOrWhiteSpace(resolvedName) && creatorPlayer)
                {
                    string playerName = creatorPlayer.GetPlayerName();
                    if (!string.IsNullOrWhiteSpace(playerName))
                    {
                        resolvedName = playerName;
                    }
                }

                if (ZNet.instance)
                {
                    foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    {
                        if (peer.m_playerID != creatorId)
                        {
                            continue;
                        }

                        foreach (ZNet.PlayerInfo playerInfo in ZNet.instance.GetPlayerList())
                        {
                            if (!playerInfo.m_characterID.Equals(peer.m_characterID))
                            {
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(resolvedName) && !string.IsNullOrWhiteSpace(playerInfo.m_name))
                            {
                                resolvedName = playerInfo.m_name;
                            }
                        }

                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    return "Odin";
                }

                if (zdo != null && nview && nview.IsOwner())
                {
                    zdo.Set(CreatorNameZdoKey, resolvedName);
                }

                return resolvedName;
            }

            public string GetText()
            {
                string note = nview && nview.GetZDO() != null
                    ? nview.GetZDO().GetString(NoteZdoKey, string.Empty)
                    : string.Empty;
                return LimitNoteLength(note);
            }

            public string GetHoverText()
            {
                Piece piece = GetComponent<Piece>();
                string text = GetCategoryDisplayName();

                string note = GetText();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    text += $"\n<color=#FFE080>Note:</color> {note}";
                }

                if (ShouldShowInspectedBy())
                {
                    text += $"\nInspected by: {GetCreatorName()}";
                }



                long creatorId = GetCreatorId();
                bool canEdit = nview && nview.GetZDO() != null && piece &&
                    creatorId != 0L && piece.IsCreator();
                return canEdit
                    ? text + $"\nPress [<color=yellow><b>{editNoteKey.Value}</b></color>] to edit flag"
                    : text + "\n<color=#AAAAAA>Only the player who placed this flag can edit its note.</color>";
            }

            public string GetHoverName()
            {
                return GetCategoryDisplayName();
            }

            public float GetHoverOffset()
            {
                return 0f;
            }

            public void SetText(string text)
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                if (nview && nview.GetZDO() != null && piece && piece.IsCreator())
                {
                    nview.InvokeRPC(NoteRpcName, LimitNoteLength((text ?? string.Empty).Trim()));
                }
            }

            private void SetNoteRpc(long sender, string text)
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                long creatorId = GetCreatorId();
                bool isOwner = nview && nview.IsOwner();
                bool validSender = piece && IsSenderPlayer(sender, piece, creatorId);

                if (isOwner && nview.GetZDO() != null && validSender)
                {
                    nview.GetZDO().Set(NoteZdoKey, LimitNoteLength(text));
                }
                else
                {
                    log.LogWarning("Rejected inspection flag note update.");
                }
            }
        }

        private bool WouldExceedNoteLimits(string text)
        {
            int characterCount = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];

                if (character != '\n' && character != '\r')
                {
                    characterCount++;
                }

                if (characterCount > MaxNoteLength)
                {
                    return true;
                }
            }

            Text textComponent = flagEditorNoteField.textComponent;
            if (!textComponent)
            {
                return false;
            }

            TextGenerator generator = new TextGenerator();

            TextGenerationSettings settings = textComponent.GetGenerationSettings(
                textComponent.rectTransform.rect.size
            );

            settings.generateOutOfBounds = true;
            settings.updateBounds = true;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;

            generator.Populate(text, settings);

            int lineCount = generator.lineCount;

            return lineCount > MaxNoteLines;
        }

        private char ValidateNoteInput(string text, int charIndex, char addedChar)
        {
            string newText = text.Insert(charIndex, addedChar.ToString());

            if (WouldExceedNoteLimits(newText))
            {
                return '\0';
            }

            return addedChar;
        }

        private static bool IsSenderPlayer(long sender, Piece piece, long playerId)
        {
            if (!piece || playerId == 0L || !ZNet.instance)
            {
                return false;
            }

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer != null)
            {
                return peer.m_playerID == playerId || IsCreatorPlatformUser(peer, piece);
            }

            return sender == ZNet.GetUID() && piece.IsCreator();
        }

        private static bool IsCreatorPlatformUser(ZNetPeer peer, Piece piece)
        {
            World world = ZNet.instance ? ZNet.instance.GetWorld() : null;
            int creatorIndex = piece.GetCreatorPlatformUserIdIndex();
            if (peer == null || world == null || creatorIndex < 0 ||
                creatorIndex >= world.m_playerHistory.Count)
            {
                return false;
            }

            Splatform.PlatformUserID creatorPlatformId = world.m_playerHistory[creatorIndex].m_id;
            foreach (ZNet.PlayerInfo playerInfo in ZNet.instance.GetPlayerList())
            {
                if (playerInfo.m_characterID.Equals(peer.m_characterID) &&
                    playerInfo.m_userInfo.m_id == creatorPlatformId)
                {
                    return true;
                }
            }

            return false;
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        private static class InspectionFlagCreatorNamePatch
        {
            [HarmonyPostfix]
            private static void Postfix(Piece __instance)
            {
                if (!__instance)
                {
                    return;
                }

                FlagNoteReceiver receiver = __instance.GetComponent<FlagNoteReceiver>();
                if (!receiver)
                {
                    return;
                }

                long creatorId = __instance.GetCreator();
                Player localPlayer = Player.m_localPlayer;
                string creatorName = localPlayer && localPlayer.GetPlayerID() == creatorId
                    ? localPlayer.GetPlayerName()
                    : string.Empty;
                receiver.SaveCreatorName(creatorName);
            }
        }

        [HarmonyPatch(typeof(ZNet), "OpenServer")]
        private static class MultiplayerWorldOpenServerPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ZNet __instance)
            {
                MarkMultiplayerWorldIfNeeded(__instance);
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

        [HarmonyPatch(typeof(Player), "TakeInput")]
        private static class FlagEditorTakeInputPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (!flagEditorInputBlocked || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
        private static class FlagEditorAttackInputPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance)
            {
                if (!flagEditorInputBlocked || __instance != Player.m_localPlayer)
                {
                    return true;
                }

                return false;
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
