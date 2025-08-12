#define VROIDCAMERA_HAS_GUI
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
        public static void Prefix(Animator animator, EntityPlayer player)
        {
            if (animator == null || player == null) return;
#if  VROIDCAMERA_DEBUG
            Debug.Log("[VroidFix] Starting dance animation for player: " + player.EntityName);
            Debug.Log("[VroidFix] Animator: " + animator.name); // DancerTransform
#endif


            // 查找 DancerTransform下面的Camera_root/Camera_root_1/Camera 路径
            Transform cameraNode = animator.transform.Find("Camera_root/Camera_root_1/Camera");

            // 如果找到了原有的镜头层级，先删除整个 Camera_root，这里需要删除所有玩家客户端的 Camera_root 层级，避免锁镜头
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
            // 仅为本地玩家处理，这样只有本地玩家的镜头会被处理，避免其他玩家的镜头被锁定
            bool isLocalPlayer = player is EntityPlayerLocal;
            if (!isLocalPlayer)
            {
#if  VROIDCAMERA_DEBUG
                Debug.Log("[VroidFix] Non-local player detected, skipping processing.");
#endif
                return;
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


        }
        public static void Postfix(Animator animator, EntityPlayer player)
        {
            if (animator == null || player == null) return;

            // 仅为本地玩家处理
            bool isLocalPlayer = player is EntityPlayerLocal;
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

    public class Patch_CorrectCameraLate
    {
        [HarmonyAfter("Harmony.VRoidMod")] // 确保在VRoidMod之后执行，覆盖掉CameraFix的逻辑
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
            // 读取GUI参数，兼容无GUI版本
            float eyeHeight = 1.6f;
            bool enableDanceCamera = true;
#if VROIDCAMERA_HAS_GUI
            var gui = CameraControlsGUI.Current;
            if (gui != null)
            {
                eyeHeight = gui.eyeHeight;
                enableDanceCamera = gui.enableDanceCamera;
            }
#endif

            if (!enableDanceCamera)
            {
#if VROIDCAMERA_DEBUG
        Debug.Log("[VroidFix] Dance Camera disabled, skipping sync.");
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

            // 计算缩放比例
            float scale = eyeHeight / 1.6f;



            // 角色底部世界位置，用于相对偏移计算。  
            // 这里用角色根节点的世界位置或DancerTransform位置作为参考点
            Vector3 referencePos = animator.transform.position;

            // 计算相机相对于角色根节点的偏移
            Vector3 localOffset = cameraNode.position - referencePos;

            // 对偏移做缩放
            Vector3 scaledOffset = localOffset * scale;

            // 计算最终相机世界位置
            Vector3 finalCameraPos = referencePos + scaledOffset;

            // 相机旋转直接用动画驱动的旋转，不缩放旋转
            Quaternion finalCameraRot = cameraNode.rotation;

            // 设置玩家摄像机位置和旋转
            __instance.transform.SetPositionAndRotation(finalCameraPos, finalCameraRot);

#if VROIDCAMERA_DEBUG
            Debug.Log($"[VroidFix] Synced camera position with scaled offset: {scaledOffset}, eyeHeight scale: {scale}");
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
            // 读取GUI参数，兼容无GUI版本
            bool enableDanceCamera = true;
#if VROIDCAMERA_HAS_GUI
            var gui = CameraControlsGUI.Current;
            if (gui != null)
            {
                enableDanceCamera = gui.enableDanceCamera;
            }
#endif

            if (!enableDanceCamera)
            {
#if VROIDCAMERA_DEBUG
        Debug.Log("[VroidFix] Dance Camera disabled, skipping sync.");
#endif
                return;
            }
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