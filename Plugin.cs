using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace TurnChime
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.cojoco.turnchime";
        public const string PluginName = "Turn Chime";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> OnHeroTurn;
        internal static ConfigEntry<bool> OnCardRewards;
        internal static ConfigEntry<bool> OnMapChoice;
        internal static ConfigEntry<bool> OnlyWhenUnfocused;
        internal static ConfigEntry<float> Volume;
        internal static ConfigEntry<string> SoundFile;

        private void Awake()
        {
            Log = Logger;
            OnHeroTurn = Config.Bind("Events", "HeroTurn", true,
                "Chime when one of your heroes starts their turn in combat.");
            OnCardRewards = Config.Bind("Events", "CardRewards", true,
                "Chime when the card rewards screen opens after a combat.");
            OnMapChoice = Config.Bind("Events", "MapChoice", true,
                "Chime when the map is ready for you to pick the next node.");
            OnlyWhenUnfocused = Config.Bind("Sound", "OnlyWhenUnfocused", false,
                "Only chime while the game window is not focused.");
            Volume = Config.Bind("Sound", "Volume", 1f,
                new ConfigDescription("Chime volume.", new AcceptableValueRange<float>(0f, 1f)));
            SoundFile = Config.Bind("Sound", "SoundFile", "",
                "Optional .wav or .ogg file to play instead of the game's own \"your turn\" sound. " +
                "A relative path is looked up in the BepInEx config folder.");

            new Harmony(PluginGuid).PatchAll();

            // The BepInEx plugin object gets destroyed by the game right after startup, so the AudioSource lives on our own object.
            var runner = new GameObject("TurnChimeRunner");
            runner.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(runner);
            runner.AddComponent<Chime>();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }
    }

    internal class Chime : MonoBehaviour
    {
        private static Chime instance;

        private AudioSource source;
        private AudioClip customClip;
        private float lastPlayed = -10f;

        internal static bool HasCustomClip => instance != null && instance.customClip != null;

        private void Awake()
        {
            instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.ignoreListenerPause = true;
            source.spatialBlend = 0f;
            StartCoroutine(LoadCustomClip());
        }

        internal static void Play(string reason)
        {
            if (instance == null)
            {
                return;
            }
            if (Plugin.OnlyWhenUnfocused.Value && Application.isFocused)
            {
                return;
            }
            // Several triggers can land in the same moment (e.g. a hero turn right as combat starts); one chime is enough.
            if (Time.unscaledTime - instance.lastPlayed < 0.5f)
            {
                return;
            }

            AudioClip clip = instance.customClip;
            if (clip == null && AudioManager.Instance != null)
            {
                clip = AudioManager.Instance.soundCombatIsYourTurn;
            }
            if (clip == null)
            {
                Plugin.Log.LogWarning($"No sound to play for {reason}.");
                return;
            }

            instance.lastPlayed = Time.unscaledTime;
            instance.source.PlayOneShot(clip, Plugin.Volume.Value);
            Plugin.Log.LogInfo($"Chime: {reason}");
        }

        private IEnumerator LoadCustomClip()
        {
            string path = Plugin.SoundFile.Value.Trim();
            if (path.Length == 0)
            {
                yield break;
            }
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Paths.ConfigPath, path);
            }
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"SoundFile not found: {path}. Using the game's sound.");
                yield break;
            }

            AudioType type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                _ => AudioType.UNKNOWN,
            };
            if (type == AudioType.UNKNOWN)
            {
                Plugin.Log.LogWarning($"SoundFile must be .wav or .ogg: {path}. Using the game's sound.");
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, type))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning($"Could not load SoundFile {path}: {request.error}. Using the game's sound.");
                    yield break;
                }
                customClip = DownloadHandlerAudioClip.GetContent(request);
                Plugin.Log.LogInfo($"Loaded SoundFile {path}.");
            }
        }
    }

    // Runs at the start of every character's turn; theHero is set only for hero turns.
    [HarmonyPatch(typeof(MatchManager), "SetActiveCharacter")]
    internal static class HeroTurnPatch
    {
        private static void Postfix(MatchManager __instance)
        {
            Hero hero = __instance.CurrentHero;
            // IsYourTurn is always true in single player; in co-op it checks that you own this hero.
            if (!Plugin.OnHeroTurn.Value || hero == null || !hero.Alive || !__instance.IsYourTurn())
            {
                return;
            }
            // In co-op the game already plays this sound unless it is muted in the background; don't double it.
            GameManager game = GameManager.Instance;
            bool vanillaPlayed = game.IsMultiplayer() && (game.gameIsOnFocus || !game.ConfigBackgroundMute);
            if (vanillaPlayed && !Chime.HasCustomClip)
            {
                return;
            }
            Chime.Play($"{hero.SourceName}'s turn");
        }
    }

    // Covers both reward scenes (RewardsManagerMultiple inherits it).
    [HarmonyPatch(typeof(RewardsManager), "ShowRewards")]
    internal static class CardRewardsPatch
    {
        private static void Postfix()
        {
            if (Plugin.OnCardRewards.Value)
            {
                Chime.Play("card rewards");
            }
        }
    }

    // DoMapTips is only called from BeginMapContinueEnd, the point where the map becomes clickable
    // (single player and co-op). Patching it instead of BeginMapContinueEnd avoids the HarmonyX
    // try/leave bug, since BeginMapContinueEnd has foreach loops.
    [HarmonyPatch(typeof(MapManager), "DoMapTips")]
    internal static class MapChoicePatch
    {
        private static void Postfix()
        {
            if (Plugin.OnMapChoice.Value)
            {
                Chime.Play("map choice");
            }
        }
    }
}
