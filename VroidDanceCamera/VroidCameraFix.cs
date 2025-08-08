using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using static Harmony.VRoidMod;

namespace VroidDanceCamera
{
    public class ModInitializer : IModApi
    {
        // 调试日志开关，默认关闭
        public static bool debugLogEnabled = false;

        public void InitMod(Mod mod)
        {
            var harmony = new HarmonyLib.Harmony("vroidmod.camera.fix");
            harmony.PatchAll();
            if (debugLogEnabled)
            {
                Debug.Log("[VroidMod] Camera patch initialized.");
            }
        }
    }

    [HarmonyPatch(typeof(VRoid_Gestures), nameof(VRoid_Gestures.StartDance))]
    public class Patch_StartDance
    {
        public static void Postfix(Animator animator, EntityPlayerLocal player)
        {
            if (animator == null || player == null) return;

            // 查找 Camera_root/Camera_root_1/Camera 路径
            Transform cameraNode = animator.transform.Find("Camera_root/Camera_root_1/Camera");

            // 情况 c：如果路径不存在，动态创建层级
            if (cameraNode == null)
            {
                Transform root = new GameObject("Camera_root").transform;
                root.SetParent(animator.transform);
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.Euler(0, 180, 0); // 初始旋转 180 度，与原始设计一致

                Transform child1 = new GameObject("Camera_root_1").transform;
                child1.SetParent(root);
                child1.localPosition = Vector3.zero;
                child1.localRotation = Quaternion.identity;

                cameraNode = new GameObject("Camera").transform;
                cameraNode.SetParent(child1);
                cameraNode.localPosition = Vector3.zero;
                cameraNode.localRotation = Quaternion.identity;

                // 为 Camera 节点添加禁用的 Camera 组件
                Camera cameraComponent = cameraNode.gameObject.AddComponent<Camera>();
                cameraComponent.enabled = false;
                cameraComponent.fieldOfView = 60f; // 设置默认 FOV，与 Unity 默认相机一致

                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Created Camera_root/Camera_root_1/Camera hierarchy with disabled Camera component.");
                }
            }

            // 禁用 Camera 组件（如果存在），避免干扰外部镜头
            Camera cameraComponentExisting = cameraNode.GetComponent<Camera>();
            if (cameraComponentExisting != null)
            {
                cameraComponentExisting.enabled = false;
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Disabled Camera component on Camera_root/Camera_root_1/Camera.");
                }
            }

            // 禁用 cameraNode 上的 AudioListener（如果存在），兼容前人错误代码
            AudioListener cameraNodeListener = cameraNode.GetComponent<AudioListener>();
            if (cameraNodeListener != null)
            {
                cameraNodeListener.enabled = false;
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Disabled AudioListener on Camera_root/Camera_root_1/Camera.");
                }
            }

            // 处理 AudioListener：禁用 vp_FPCamera 上的 AudioListener，将其绑定到角色根节点
            if (player.vp_FPCamera != null)
            {
                AudioListener cameraListener = player.vp_FPCamera.GetComponent<AudioListener>();
                if (cameraListener != null)
                {
                    cameraListener.enabled = false;
                    if (ModInitializer.debugLogEnabled)
                    {
                        Debug.Log("[VroidFix] Disabled AudioListener on vp_FPCamera.");
                    }
                }
            }

            AudioListener playerListener = animator.gameObject.GetComponent<AudioListener>();
            if (playerListener == null)
            {
                playerListener = animator.gameObject.AddComponent<AudioListener>();
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Added AudioListener to animator root: " + animator.gameObject.name);
                }
            }
            else
            {
                playerListener.enabled = true;
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Enabled existing AudioListener on animator root: " + animator.gameObject.name);
                }
            }
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), "LateUpdate")]
    public class Patch_CorrectCameraLate
    {
        static void Postfix(EntityPlayerLocal __instance)
        {
            // 基本检查，确保玩家和 VRoid 数据有效
            if (__instance == null || !__instance.Spawned || !__instance.HasVRoid() || __instance.bFirstPersonView)
                return;

            var mgr = __instance.VRoidManager();
            if (mgr == null || mgr.NET_DanceIndex <= 0) // 未在跳舞时，使用默认镜头机制
                return;

            Animator animator = mgr.DancerTransform?.GetComponent<Animator>();
            if (animator == null) return;

            // 查找 Camera_root/Camera_root_1/Camera 路径
            Transform cameraNode = animator.transform.Find("Camera_root/Camera_root_1/Camera");
            if (cameraNode == null) // 情况 b：路径不存在，使用默认镜头机制
                return;

            // 查找父节点
            Transform cameraRoot1 = cameraNode.parent; // Camera_root_1
            Transform cameraRoot = cameraRoot1 != null ? cameraRoot1.parent : null; // Camera_root

            // 情况 b 或 d：检查 Camera_root, Camera_root_1, Camera 是否都为默认变换
            bool isDefaultResolved = true;
            if (cameraRoot != null && (cameraRoot.localPosition != Vector3.zero || cameraRoot.localRotation != Quaternion.Euler(0, 180, 0)))
            {
                isDefaultResolved = false;
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Camera_root transform is modified, indicating animation control.");
                }
            }
            if (cameraRoot1 != null && (cameraRoot1.localPosition != Vector3.zero || cameraRoot1.localRotation != Quaternion.identity))
            {
                isDefaultResolved = false;
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Camera_root_1 transform is modified, indicating animation control.");
                }
            }
            if (cameraNode.localPosition != Vector3.zero || cameraNode.localRotation != Quaternion.identity)
            {
                isDefaultResolved = false;
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] Camera transform is modified, indicating animation control.");
                }
            }

            if (isDefaultResolved)
            {
                if (ModInitializer.debugLogEnabled)
                {
                    Debug.Log("[VroidFix] All camera nodes are at default transform, skipping sync.");
                }
                return;
            }

            // 确保 Camera 组件被禁用
            Camera cameraComponent = cameraNode.GetComponent<Camera>();
            if (cameraComponent != null)
            {
                cameraComponent.enabled = false;
            }

            // 情况 a 或 c：同步 vp_FPCamera 的位置、旋转和视野
            var worldPos = cameraNode.position;
            var worldRot = cameraNode.rotation;
            __instance.vp_FPCamera.transform.SetPositionAndRotation(worldPos, worldRot);

            // 同步视野（Field of View）
            if (cameraComponent != null)
            {
                Camera playerCamera = __instance.vp_FPCamera.GetComponent<Camera>();
                if (playerCamera != null)
                {
                    playerCamera.fieldOfView = cameraComponent.fieldOfView;
                    if (ModInitializer.debugLogEnabled)
                    {
                        Debug.Log($"[VroidFix] Synced vp_FPCamera FOV to {cameraComponent.fieldOfView}.");
                    }
                }
            }

            if (ModInitializer.debugLogEnabled)
            {
                Debug.Log($"[VroidFix] Synced vp_FPCamera to Camera_root/Camera_root_1/Camera world transform at position: {worldPos}.");
            }
        }
    }
}