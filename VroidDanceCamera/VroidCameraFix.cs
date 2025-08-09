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

        public void InitMod(Mod mod)
        {
            var harmony = new HarmonyLib.Harmony("vroidmod.camera.fix");
            harmony.PatchAll();
            // 使用条件编译，略微减少日志输出
#if VROIDCAMERA_DEBUG
            Debug.Log("[VroidMod] Camera patch initialized.");
#endif
        }
    }

    [HarmonyPatch(typeof(VRoid_Gestures), nameof(VRoid_Gestures.StartDance))]
    public class Patch_StartDance
    {
        public static void Prefix(Animator animator, EntityPlayerLocal player)
        {
            if (animator == null || player == null) return;
#if  VROIDCAMERA_DEBUG
            Debug.Log("[VroidFix] Starting dance animation for player: " + player.EntityName);
            Debug.Log("[VroidFix] Animator: " + animator.name);
#endif

            // 仅为本地玩家处理
            bool isLocalPlayer = player == GameManager.Instance.World.GetPrimaryPlayer();
            if (!isLocalPlayer)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Non-local player detected, skipping processing.");
#endif
                return;
            }
            // 查找 Camera_root/Camera_root_1/Camera 路径
            Transform cameraNode = animator.transform.Find("Camera_root/Camera_root_1/Camera");

            // 如果找到了原有的镜头层级，先删除整个 Camera_root
            if (cameraNode != null)
            {
                Transform cameraRoot = cameraNode.parent?.parent;
                if (cameraRoot != null && cameraRoot.name == "Camera_root")
                {
                    UnityEngine.Object.DestroyImmediate(cameraRoot.gameObject);
#if  VROIDCAMERA_DEBUG
                    Debug.Log("[VroidFix] Removed existing Camera_root hierarchy to ensure clean initialization.");
#endif
                }
                cameraNode = null; // 置空，后续统一创建
            }

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
                //cameraComponent.fieldOfView = 60f; // 设置默认 FOV，与 Unity 默认相机一致

#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Created Camera_root/Camera_root_1/Camera hierarchy with disabled Camera component.");
#endif
            }

            // 禁用 Camera 组件（如果存在），避免干扰外部镜头
            Camera cameraComponentExisting = cameraNode.GetComponent<Camera>();
            if (cameraComponentExisting != null)
            {
                cameraComponentExisting.enabled = false;
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Disabled Camera component on Camera_root/Camera_root_1/Camera.");
#endif
            }

            // 禁用 cameraNode 上的 AudioListener（如果存在），兼容前人错误代码
            AudioListener cameraNodeListener = cameraNode.GetComponent<AudioListener>();
            if (cameraNodeListener != null)
            {
                cameraNodeListener.enabled = false;
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Disabled AudioListener on Camera_root/Camera_root_1/Camera.");
#endif
            }

        }
        public static void Postfix(Animator animator, EntityPlayerLocal player)
        {
            if (animator == null || player == null) return;

            // 仅为本地玩家处理
            bool isLocalPlayer = player == GameManager.Instance.World.GetPrimaryPlayer();
            if (!isLocalPlayer)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Non-local player detected, skipping processing.");
#endif
                return;
            }
            // 设置 DanceAudio 的 AudioSource 为 2D 音效
            Transform danceAudio = animator.transform.Find("DanceAudio");
            if (danceAudio != null)
            {
                AudioSource audioSource = danceAudio.GetComponent<AudioSource>();
                if (audioSource != null)
                {
                    audioSource.spatialBlend = 0f; // 设置为 2D 音效，音量不受距离影响
#if  VROIDCAMERA_DEBUG
                    Debug.Log($"[VroidFix] Set AudioSource on {danceAudio.gameObject.name} to 2D (spatialBlend = 0), clip: {audioSource.clip?.name}");
#endif
                }
                else
                {
#if  VROIDCAMERA_DEBUG
                    Debug.LogWarning("[VroidFix] No AudioSource found on DanceAudio.");
#endif
                }
            }
            else
            {
#if  VROIDCAMERA_DEBUG
                Debug.LogWarning("[VroidFix] DanceAudio transform not found under animator.");
#endif
            }
        }
    }


    [HarmonyPatch(typeof(vp_FPCamera), "Update3rdPerson")]
    [HarmonyAfter("Harmony.VRoidMod")] // 确保在VRoidMod之后执行，覆盖掉CameraFix的逻辑
    public class Patch_CorrectCameraLate
    {
        static void Postfix(vp_FPCamera __instance)
        {
            // 基本检查，确保玩家和 VRoid 数据有效
            // 获取本地玩家
            EntityPlayerLocal player = GameManager.Instance.World.GetPrimaryPlayer();
            if (player == null || !player.Spawned || !player.HasVRoid() || player.bFirstPersonView)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Player invalid, not spawned, no VRoid, or in first-person view, skipping camera sync.");
#endif
                return;
            }

            var mgr = player.VRoidManager();
            if (mgr == null || mgr.NET_DanceIndex <= 0)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] VRoid manager null or not dancing, skipping camera sync.");
#endif
                return;
            }

            Animator animator = mgr.DancerTransform?.GetComponent<Animator>();
            if (animator == null)
            {
#if  VROIDCAMERA_DEBUG
                Debug.LogWarning("[VroidFix] Animator is null or disabled, skipping camera sync.");
#endif
                return;
            }
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
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Camera_root transform is modified, indicating animation control.");
#endif
            }
            if (cameraRoot1 != null && (cameraRoot1.localPosition != Vector3.zero || cameraRoot1.localRotation != Quaternion.identity))
            {
                isDefaultResolved = false;
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Camera_root_1 transform is modified, indicating animation control.");
#endif
            }
            if (cameraNode.localPosition != Vector3.zero || cameraNode.localRotation != Quaternion.identity)
            {
                isDefaultResolved = false;
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Camera transform is modified, indicating animation control.");
#endif
            }

            if (isDefaultResolved)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] All camera nodes are at default transform, skipping sync.");
#endif
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
            __instance.transform.SetPositionAndRotation(worldPos, worldRot);

            //// 同步视野（Field of View）
            //if (cameraComponent != null)
            //{
            //    Camera playerCamera = __instance.GetComponent<Camera>();
            //    if (playerCamera != null)

            //    {
            //        __instance.GetComponent<Camera>().fieldOfView = cameraComponent.fieldOfView;

            // #if  VROIDCAMERA_DEBUG
            //            Debug.Log($"[VroidFix] Synced vp_FPCamera FOV to {playerCamera.fieldOfView}.");
            //        }
            // #endif
            //}

#if VROIDCAMERA_DEBUG
            Debug.Log($"[VroidFix] Synced vp_FPCamera to Camera_root/Camera_root_1/Camera world transform at position: {worldPos}.");
#endif
        }
    }
    // 按理来说视野和位置旋转应该在 LateUpdate 中处理，但似乎重生之后会导致CameraFix的优先级变了，然后会覆盖掉这个函数，导致无法运镜，这里先暂时拆开处理变换和视野
    [HarmonyPatch(typeof(EntityPlayerLocal), "LateUpdate")]
    public class Patch_CorrectCameraFieldOfViewLate
    {
        static void Postfix(EntityPlayerLocal __instance)
        {
            // 基本检查，确保玩家和 VRoid 数据有效
            if (__instance == null || !__instance.Spawned || !__instance.HasVRoid() || __instance.bFirstPersonView)
                return;

            var mgr = __instance.VRoidManager();
            if (mgr == null || mgr.NET_DanceIndex <= 0)
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
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Camera_root transform is modified, indicating animation control.");
#endif
            }
            if (cameraRoot1 != null && (cameraRoot1.localPosition != Vector3.zero || cameraRoot1.localRotation != Quaternion.identity))
            {
                isDefaultResolved = false;
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Camera_root_1 transform is modified, indicating animation control.");
#endif
            }
            if (cameraNode.localPosition != Vector3.zero || cameraNode.localRotation != Quaternion.identity)
            {
                isDefaultResolved = false;
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Camera transform is modified, indicating animation control.");
#endif
            }

            if (isDefaultResolved)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] All camera nodes are at default transform, skipping sync.");
#endif
                return;
            }

            // 确保 Camera 组件被禁用
            Camera cameraComponent = cameraNode.GetComponent<Camera>();
            if (cameraComponent != null)
            {
                cameraComponent.enabled = false;
            }


            // 同步视野（Field of View）
            if (cameraComponent != null)
            {
                Camera playerCamera = __instance.vp_FPCamera.GetComponent<Camera>();
                if (playerCamera != null)
                {
                    playerCamera.fieldOfView = cameraComponent.fieldOfView;
#if  VROIDCAMERA_DEBUG
                    Debug.Log($"[VroidFix] Synced vp_FPCamera FOV to {cameraComponent.fieldOfView}.");
#endif
                }
            }
        }
    }
}