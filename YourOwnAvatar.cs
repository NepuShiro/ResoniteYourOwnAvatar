using System;
using System.Linq;
using System.Threading.Tasks;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using HarmonyLib;
using ResoniteModLoader;
using SkyFrost.Base;

namespace YourOwnAvatar;

public class YourOwnAvatar : ResoniteMod
{
    internal const string VERSION_CONSTANT = "1.0.0";
    public override string Name => "YourOwnAvatar";
    public override string Author => "NepuShiro";
    public override string Version => VERSION_CONSTANT;
    public override string Link => "https://github.com/NepuShiro/ResoniteStopAutoAvatar/";

    [AutoRegisterConfigKey] private static ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("enabled", "Should YourOwnAvatar be Enabled?", () => true);

    private static ModConfiguration config;

    public override void OnEngineInit()
    {
        config = GetConfiguration();

        Harmony harmony = new Harmony("net.NepuShiro.YourOwnAvatar");
        harmony.PatchAll();
    }

    [HarmonyPatch]
    class FunnyPatch
    {
        [HarmonyPrefix, HarmonyPatch(typeof(CommonAvatarBuilder), "SetupAvatarAccessKey")]
        private static bool SetupAvatarAccessKeyPrefix(ref Task __result)
        {
            if (!config.GetValue(ENABLED)) return true;

            __result = Task.CompletedTask;
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UserRoot), "OnStart")]
        public static void UserRootPostfix(UserRoot __instance)
        {
            if (__instance.ActiveUser != __instance.World.LocalUser) return;
            if (!config.GetValue(ENABLED)) return;

            World world = __instance.World;
            if (world.IsUserspace() || world.IsAuthority) return;

            CommonAvatarBuilder builder = world.RootSlot.GetComponentInChildren<CommonAvatarBuilder>();
            if (builder == null) return;

            if (!builder.LoadCloudAvatars) return;
            __instance.Slot.RunInUpdates(3, () =>
            {
                AvatarObjectSlot objSlot = __instance.Slot.GetComponent<AvatarObjectSlot>();
                AvatarManager manager = __instance.Slot.GetComponent<AvatarManager>();
                CommonAvatarBuilder.AvatarTemplate template = null;
                if (builder.CustomAvatarTemplates.Count > 0 && objSlot.Equipped.Target is AvatarRoot comp)
                {
                    template = builder.CustomAvatarTemplates.FirstOrDefault(x => x.TemplateRoot.Name == comp.Slot.Name) ?? builder.CustomAvatarTemplates.FirstOrDefault(x => x.TemplateRoot.Name == comp.Slot.GetObjectRoot().Name);
                }

                if (template != null && template.BlockCloudAvatar.Value || __instance.LocalUser.DefaultSpectator) return;

                Uri favoriteAvatar = world.Engine.Cloud.Profile.GetCurrentFavorite(FavoriteEntity.Avatar);
                if (favoriteAvatar != null)
                {
                    Slot avatar = __instance.World.AddSlot("Avatar");
                    avatar.StartTask(async () =>
                    {
                        bool cleanup = false;

                        try
                        {
                            await default(ToWorld);
                            await avatar.LoadObjectAsync(favoriteAvatar);
                            avatar = avatar.GetComponent<InventoryItem>()?.Unpack(true) ?? avatar;
                            await default(NextUpdate);
                            if (!manager.Equip(avatar, isManualEquip: false, forceDestroyOld: true))
                            {
                                cleanup = true;
                            }
                        }
                        catch (Exception e)
                        {
                            Error(e);
                            cleanup = true;
                        }

                        if (cleanup)
                        {
                            avatar?.Destroy();
                        }
                    });
                }
            });
        }
    }
}