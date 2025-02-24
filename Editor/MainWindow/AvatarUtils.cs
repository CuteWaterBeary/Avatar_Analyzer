﻿#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using lilAvatarUtils.Analyzer;
using lilAvatarUtils.Utils;
using UnityEditor;
using UnityEngine;
#if LIL_VRCSDK3_AVATARS
using VRCAvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
#endif

namespace lilAvatarUtils.MainWindow
{
    internal class AvatarUtils
    {
        private const string menuPathGameObject     = "GameObject/";
        private const string menuPathAnalyzeAvatar  = menuPathGameObject + "Analyze this Object...";
        private const int menuPriorityGameObject    = 21; // This must be 21 or less
        private const int menuPriorityAnalyzeAvatar = menuPriorityGameObject;
        [MenuItem(menuPathAnalyzeAvatar, false, menuPriorityAnalyzeAvatar)]
        internal static void AnalyzeAvatar()
        {
            var window = (AvatarUtilsWindow)EditorWindow.GetWindow(typeof(AvatarUtilsWindow), false, AvatarUtilsWindow.TEXT_WINDOW_NAME);
            window.gameObject = Selection.activeGameObject;
            window.Analyze();
            window.Show();
        }

        [MenuItem(menuPathAnalyzeAvatar, true, menuPriorityAnalyzeAvatar)]
        internal static bool CheckObject()
        {
            return Selection.activeGameObject != null;
        }
    }

    internal class AvatarUtilsWindow : EditorWindow
    {
        internal const string TEXT_WINDOW_NAME = "Object Analyzer";

        public EditorMode editorMode = EditorMode.Textures;
        public GameObject gameObject;
        public TexturesGUI texturesGUI = new TexturesGUI();
        public MaterialsGUI materialsGUI = new MaterialsGUI();
        public RenderersGUI renderersGUI = new RenderersGUI();
        #if LIL_VRCSDK3_AVATARS
        public PhysBonesGUI physBonesGUI = new PhysBonesGUI();
        #endif
        public LightingTestGUI lightingTestGUI = new LightingTestGUI();


        [NonSerialized] private bool isAnalyzed = false;
        [NonSerialized] Vector3 prevPosition = Vector3.zero;
        [NonSerialized] Quaternion prevRotation = Quaternion.identity;
        [NonSerialized] float prevFov = 0;
        [NonSerialized] float prevNear = 0;
        [NonSerialized] float prevFar = 0;

        [MenuItem("Tools/Object Analyzer")]
        internal static void Init()
        {
            string windowName = $"{TEXT_WINDOW_NAME}";
            var window = (AvatarUtilsWindow)GetWindow(typeof(AvatarUtilsWindow), false, windowName);
            window.Show();
        }

        internal void OnGUI()
        {
            GUIUtils.Initialize();

            GameObjectSelectionGUI();

            string[] sEditorModeList =
            {
                "Textures",
                "Materials",
                "Renderers",
                #if LIL_VRCSDK3_AVATARS
                "PhysBones",
                #endif
                "LightBox",
                "Utilities"
            };
            editorMode = (EditorMode)GUILayout.Toolbar((int)editorMode, sEditorModeList);
            if(editorMode == EditorMode.Textures)
            {
                texturesGUI.Draw(this);
                return;
            }
            if(editorMode == EditorMode.Materials)
            {
                materialsGUI.Draw(this);
                return;
            }
            if(editorMode == EditorMode.Renderers)
            {
                renderersGUI.Draw(this);
                return;
            }
            #if LIL_VRCSDK3_AVATARS
            if(editorMode == EditorMode.PhysBones)
            {
                physBonesGUI.Draw(this);
                return;
            }
            #endif
            if(editorMode == EditorMode.LightBox)
            {
                lightingTestGUI.Draw(this);
                return;
            }
            if(editorMode == EditorMode.Utilities)
            {
                if(gameObject == null) return;
                if(GUILayout.Button("Clean up Materials"))
                {
                    var cleanedMaterials = MaterialCleaner.RemoveUnusedProperties(materialsGUI.mds.Keys);
                    EditorUtility.DisplayDialog(
                        "AvatarUtils",
                        $"Removed unused properties on {cleanedMaterials.Count} materials.",
                        "OK"
                    );
                }
                if(GUILayout.Button("Clean up AnimatorControllers"))
                {
                    var controllers = new HashSet<RuntimeAnimatorController>(
                        gameObject.GetBuildComponents<Animator>().Select(a => a.runtimeAnimatorController).Where(ac => ac != null)
                    );

                    #if LIL_VRCSDK3_AVATARS
                    foreach(var descriptor in gameObject.GetBuildComponents<VRCAvatarDescriptor>())
                    {
                        controllers.UnionWith(descriptor.specialAnimationLayers.Select(layer => layer.animatorController).Where(ac => ac != null));
                        controllers.UnionWith(descriptor.baseAnimationLayers.Select(layer => layer.animatorController).Where(ac => ac != null));
                    }
                    #endif
                    var cleanedControllers = SubAssetCleaner.RemoveUnusedSubAssets(controllers);
                    EditorUtility.DisplayDialog(
                        "AvatarUtils",
                        $"Removed unused sub-assets in {cleanedControllers.Count} AnimatorControllers.",
                        "OK"
                    );
                }
                if(GUILayout.Button("Remove Missing Components"))
                {
                    int count = 0;
                    foreach(var t in gameObject.GetComponentsInChildren<Transform>(true))
                    {
                        count += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    }
                    EditorUtility.DisplayDialog(
                        "AvatarUtils",
                        $"Removed {count} missing components.",
                        "OK"
                    );
                }
            }
        }

        private void GameObjectSelectionGUI()
        {
            var rect = EditorGUILayout.GetControlRect(GUILayout.Height(32));
            var rectButton = new Rect(rect.x, rect.y, 32, rect.height);
            var rectField = new Rect(rectButton.xMax + 2, rect.y, rect.width - rectButton.width - 2, rect.height);

            EditorStyles.objectField.fontSize = 20;
            GameObject gameObject2 = (GameObject)EditorGUI.ObjectField(rectField, gameObject, typeof(GameObject), true);
            EditorStyles.objectField.fontSize = GUIUtils.objectFieldFontSize;

            GUI.enabled = gameObject != null;
            bool shouldRefresh = GUI.Button(rectButton, GUIUtils.iconRefresh);
            GUI.enabled = true;

            if((gameObject2 != null && gameObject2 != gameObject) || (gameObject != null && !isAnalyzed) || shouldRefresh)
            {
                if(gameObject2 != null) gameObject = gameObject2;
                Analyze();
                isAnalyzed = true;
            }

            gameObject = gameObject2;
        }

        private void Update()
        {
            if(editorMode != EditorMode.LightBox) return;
            var sceneView = SceneView.lastActiveSceneView;
            if(sceneView == null || sceneView.camera == null) return;

            bool shouldRepaint = false;
            var sceneCamera = sceneView.camera;
            if(prevPosition != sceneCamera.transform.position){prevPosition = sceneCamera.transform.position; shouldRepaint = true;};
            if(prevRotation != sceneCamera.transform.rotation){prevRotation = sceneCamera.transform.rotation; shouldRepaint = true;};
            if(prevFov      != sceneCamera.fieldOfView       ){prevFov      = sceneCamera.fieldOfView       ; shouldRepaint = true;};
            if(prevNear     != sceneCamera.nearClipPlane     ){prevNear     = sceneCamera.nearClipPlane     ; shouldRepaint = true;};
            if(prevFar      != sceneCamera.farClipPlane      ){prevFar      = sceneCamera.farClipPlane      ; shouldRepaint = true;};
            if(shouldRepaint) Repaint();
        }

        internal void Analyze()
        {
            SetGameObject();
            TextureAnalyzer.Analyze(gameObject, out texturesGUI.tds, out materialsGUI.mds);
            RendererAnalyzer.Analyze(gameObject, out renderersGUI.smrs, out renderersGUI.mrs, out renderersGUI.psrs);

            texturesGUI.Set();
            materialsGUI.Set();
            renderersGUI.Set();
            lightingTestGUI.Set(true);

            #if LIL_VRCSDK3_AVATARS
            PhysBonesAnalyzer.Analyze(gameObject, out physBonesGUI.pbs, out physBonesGUI.pbcs);
            physBonesGUI.Set();
            #endif
        }

        private void SetGameObject()
        {
            texturesGUI.gameObject = gameObject;
            materialsGUI.gameObject = gameObject;
            renderersGUI.gameObject = gameObject;
            lightingTestGUI.gameObject = gameObject;
            #if LIL_VRCSDK3_AVATARS
            physBonesGUI.gameObject = gameObject;
            #endif
        }

        internal void OnDisable()
        {
            lightingTestGUI.OnDisable();
        }

        internal enum EditorMode
        {
            Textures,
            Materials,
            Renderers,
            #if LIL_VRCSDK3_AVATARS
            PhysBones,
            #endif
            LightBox,
            Utilities
        }
    }
}
#endif
