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

            // We completely skip this method, to never create a key, and to not set user.AvatarAccessKey
            __result = Task.CompletedTask;
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UserRoot), "OnStart")]
        public static void UserRootPostfix(UserRoot __instance)
        {
            if (!config.GetValue(ENABLED)) return;
            // If we're not the active user of the userroot don't run.
            if (__instance.ActiveUser != __instance.World.LocalUser) return;

            // if the world is userspace or if we're the host don't run.
            World world = __instance.World;
            if (world.IsUserspace() || world.IsAuthority) return;

            // Check if the CommonAvatarBuilder Exists, and if You're allowed to load Cloud Avatars.
            CommonAvatarBuilder builder = world.RootSlot.GetComponentInChildren<CommonAvatarBuilder>();
            if (builder == null || !builder.LoadCloudAvatars) return;
            
            // Maybe switch this between RunInUpdates/RunInSeconds to stop edge cases
            __instance.Slot.RunInSeconds(3, () =>
            {
                AvatarObjectSlot objSlot = __instance.Slot.GetComponent<AvatarObjectSlot>();
                AvatarManager manager = __instance.Slot.GetComponent<AvatarManager>();

                // Try to find if the Avatar we currently have equipped is one of the CustomAvatarTemplates
                CommonAvatarBuilder.AvatarTemplate template = null;
                if (builder.CustomAvatarTemplates.Count > 0 && objSlot.Equipped.Target is AvatarRoot comp)
                {
                    // Maybe check if any of the templates block the cloud avatar for safety?
                    template = builder.CustomAvatarTemplates.FirstOrDefault(x => x.TemplateRoot.Name == comp.Slot.Name) ?? builder.CustomAvatarTemplates.FirstOrDefault(x => x.TemplateRoot.Name == comp.Slot.GetObjectRoot().Name);
                }

                // if you're in a template avatar, check if it blocks the cloud avatar
                // or if you're defaulted spectator. Don't load the cloud avatar
                if (template != null && template.BlockCloudAvatar.Value || __instance.LocalUser.DefaultSpectator) return;

                // This is basically a copy of what the host does to spawn the avatar - CommonAvatarBuilder.SpawnCloudAvatar() ~ L54
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