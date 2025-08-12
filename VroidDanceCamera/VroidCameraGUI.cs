// 这里使用VroidCameraFix.cs里面定义的 VROIDCAMERA_HAS_GUI 宏来判断是否启用 GUI 功能，需要引入
#define VROIDCAMERA_HAS_GUI
#if VROIDCAMERA_HAS_GUI
using HarmonyLib;
using RootMotion.Demos;
using System;
using UnityEngine;
using static UnityEngine.Input; // 添加此行以修复 Input 未定义的问题
// 修正为：


namespace VroidDanceCamera
{
    [HarmonyPatch(typeof(XUiC_VRoidPreview), "OnOpen")]
    [HarmonyAfter("VRoidMod")]
    public class Patch_VRoidPreviewOnOpen
    {
        static void Postfix(XUiC_VRoidPreview __instance)
        {
            var guiComponent = __instance.ViewComponent.UiTransform.gameObject.AddComponent<CameraControlsGUI>();
            guiComponent.previewInstance = __instance;
#if VROIDCAMERA_DEBUG
            Debug.Log("[VroidFix] Added CameraControlsGUI to VRoidPreview after OnOpen.");
#endif
        }
    }

    public class CameraControlsGUI : MonoBehaviour
    {
        public XUiC_VRoidPreview previewInstance;

        public static CameraControlsGUI Current;

        public float eyeHeight = 1.6f;
        public bool enableDanceCamera = true;
        public static bool CachedEnableDanceCamera = true;
        private bool metadataLoaded = false;
        private string lastModelName = "";
        private Rect windowRect = new Rect(Screen.width - 260f, 20f, 250f, 200f);
        private int windowID = 999;
        private VRoidAvatarMetadata metadata;
        private bool guiChanged = false;

        // new: manual dragging state
        private bool isDragging = false;
        private Vector2 dragOffset;

        private bool lastVisible = false;

        private void Start()
        {
            enableDanceCamera = CachedEnableDanceCamera;
            LoadEyeHeightFromMetadata();
        }

        private void OnEnable()
        {
            // 每次组件启用时重置拖动状态 & 清焦点，修复第一次打开被 XUi 吞事件的问题
            isDragging = false;
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
#if VROIDCAMERA_DEBUG
            Debug.Log("[VroidFix] Reset GUI focus & drag state on enable.");
#endif
        }
        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Update()
        {
            // 如果 previewInstance 未就绪，跳过
            if (previewInstance == null || previewInstance.xui == null) return;

            // GUI 只在预览可见时启用
            this.enabled = previewInstance.ViewComponent.IsVisible;

            // 模型切换逻辑（保留）
            var manager = GetAvatarManager();
            if (manager != null && manager.NET_ModelName != lastModelName)
            {
                lastModelName = manager.NET_ModelName;
                metadataLoaded = false;
                LoadEyeHeightFromMetadata();
#if VROIDCAMERA_DEBUG
                Debug.Log($"[VroidFix] Detected model switch to {lastModelName}, reloading EyeHeight.");
#endif
            }

            // 写回 metadata（只有 GUI 改动时）
            if (metadata != null && guiChanged)
            {
                metadata.EyeHeight = eyeHeight;
#if VROIDCAMERA_DEBUG
                Debug.Log($"[VroidFix] Applied EyeHeight to metadata: {eyeHeight}");
#endif
                guiChanged = false;
            }

            // --- Manual drag handling (使用低层 Input，避免 XUi 拦截 IMGUI 事件导致的不可拖动) ---
            // 仅在预览可见时响应拖动
            if (previewInstance.ViewComponent.IsVisible)
            {
                // 开始拖动（按下时判断是否在标题栏区域）
                if (!isDragging && Input.GetMouseButtonDown(0))
                {
                    Vector2 mouseGuiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                    // 标题栏高度（可根据需要调整）
                    float titleBarHeight = 24f;
                    Rect titleBar = new Rect(windowRect.x, windowRect.y, windowRect.width, titleBarHeight);

                    if (titleBar.Contains(mouseGuiPos))
                    {
                        isDragging = true;
                        dragOffset = mouseGuiPos - new Vector2(windowRect.x, windowRect.y);
#if VROIDCAMERA_DEBUG
                        Debug.Log("[VroidFix] Start manual dragging window.");
#endif
                    }
                }

                // 持续拖动
                if (isDragging)
                {
                    if (Input.GetMouseButton(0))
                    {
                        Vector2 mouseGuiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                        windowRect.x = mouseGuiPos.x - dragOffset.x;
                        windowRect.y = mouseGuiPos.y - dragOffset.y;

                        // 限制到屏幕范围
                        windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
                        windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
                    }
                    else
                    {
                        // 鼠标抬起，结束拖动
                        isDragging = false;
#if VROIDCAMERA_DEBUG
                        Debug.Log("[VroidFix] End manual dragging window.");
#endif
                    }
                }
            }
            else
            {
                // 面板不可见时确保不在拖动状态
                isDragging = false;
            }
        }

        private void OnGUI()
        {
            bool visible = previewInstance.ViewComponent.IsVisible;
            if (!visible) { lastVisible = false; return; }

            // 只在从不可见->可见的帧清一次焦点（避免每帧清 hotControl 打断拖拽）
            if (!lastVisible)
            {
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
#if VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Released GUI focus on panel open.");
#endif
            }
            lastVisible = true;

            GUI.depth = -9999; // 保证最上层绘制
            // 注意：我们用手动拖动替代 GUI.DragWindow()，因此这里不再调用 GUI.DragWindow()
            windowRect = GUILayout.Window(windowID, windowRect, DrawWindow, "Camera Controls");
        }

        private void DrawWindow(int id)
        {
            // Window header label (纯显示，拖动由外部实现)
            GUILayout.Label("EyeHeight: " + eyeHeight.ToString("F2"));

            float newEyeHeight = GUILayout.HorizontalSlider(eyeHeight, 1.0f, 2.0f);
            if (!Mathf.Approximately(newEyeHeight, eyeHeight))
            {
                eyeHeight = newEyeHeight;
                guiChanged = true;
#if VROIDCAMERA_DEBUG
                Debug.Log($"[VroidFix] EyeHeight adjusted to: {eyeHeight}");
#endif
            }

            bool newEnableDanceCamera = GUILayout.Toggle(enableDanceCamera, "Enable Dance Camera");
            if (newEnableDanceCamera != enableDanceCamera)
            {
                enableDanceCamera = newEnableDanceCamera;
                CachedEnableDanceCamera = enableDanceCamera; // 缓存设置
                guiChanged = true;
#if VROIDCAMERA_DEBUG
                Debug.Log($"[VroidFix] EnableDanceCamera adjusted to: {enableDanceCamera}");
#endif
            }

            // 不再使用 GUI.DragWindow(); 改为手动拖动
            GUILayout.Space(6f);
            GUILayout.Label("(按住标题栏可拖动)");
            GUILayout.Label("EyeHeight可调节镜头位移缩放，避免对不上头部。");
            GUILayout.Label("Scale = EyeHeight / 1.6m");
        }

        private void LoadEyeHeightFromMetadata()
        {
            var manager = GetAvatarManager();
            if (manager == null) return;

            Transform dancerTransform = null;
            try
            {
                dancerTransform = (Transform)AccessTools.Field(typeof(VRoid_AvatarManager), "DancerTransform").GetValue(manager);
            }
            catch
            {
#if VROIDCAMERA_DEBUG
                Debug.LogWarning("[VroidFix] Failed to access DancerTransform safely. Using default.");
#endif
                return;
            }

            if (dancerTransform == null)
            {
#if VROIDCAMERA_DEBUG
                Debug.LogWarning("[VroidFix] DancerTransform not found.");
#endif
                return;
            }

            metadata = dancerTransform.GetComponent<VRoidAvatarMetadata>();
            if (metadata != null)
            {
                try
                {
                    eyeHeight = (float)AccessTools.Field(typeof(VRoidAvatarMetadata), "EyeHeight").GetValue(metadata);
                    metadataLoaded = true;
                    guiChanged = false;
#if VROIDCAMERA_DEBUG
                    Debug.Log($"[VroidFix] Loaded EyeHeight from metadata: {eyeHeight}");
#endif
                }
                catch
                {
#if VROIDCAMERA_DEBUG
                    Debug.LogWarning("[VroidFix] Failed to access EyeHeight safely. Using default 1.6f.");
#endif
                }
            }
            else
            {
#if VROIDCAMERA_DEBUG
                Debug.LogWarning("[VroidFix] VRoidAvatarMetadata not found. Using default 1.6f.");
#endif
            }
        }

        private VRoid_AvatarManager GetAvatarManager()
        {
            if (previewInstance?.xui?.playerUI?.entityPlayer == null)
                return null;

            return previewInstance.xui.playerUI.entityPlayer.VRoidManager();
        }
    }
}
#endif