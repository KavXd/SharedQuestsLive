// Shared Quests Live: shows a friend's quest progress in your game within a few seconds, without a raid
// or restart. Needs the Shared Quests server mod (1.1.0 or newer).
//
// While you're in the menus, it sends the server what your game has (quest statuses, finished steps and
// counters) every few seconds. The server answers with your quests as it has them, which includes
// progress from your group, and this puts anything newer into the game's quest list.
//
// It only ever moves quests forward: it raises counters, adds finished steps and marks quests ready to
// turn in. It never turns in, starts, fails or removes a quest, and it doesn't run during a raid.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Quests;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Utils;
using UnityEngine;

namespace SharedQuestsLive;

[BepInPlugin(Guid, "Shared Quests Live", "1.0.1")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "akvxxx16.sharedquests.live";
    const string Route = "/sharedquests/sync";

    internal static ManualLogSource Log;

    // Quest controllers the menus use (main menu, hideout), with the profile each was made for.
    static readonly List<(QuestControllerClientBackend Controller, Profile Profile)> Controllers = new();

    ConfigEntry<bool> _enabled;
    ConfigEntry<float> _interval;
    ConfigEntry<bool> _notify;

    float _nextCheck;
    Task<string> _pending;
    Profile _pendingProfile;
    bool _reportAgain;
    bool _warnedMismatch;

    void Awake()
    {
        Log = Logger;
        if (Chainloader.PluginInfos.ContainsKey("com.fika.headless"))
        {
            Log.LogInfo("Fika headless client: not needed here, staying off");
            return;
        }

        _enabled = Config.Bind("General", "Enabled", true, "Show quest progress from your Shared Quests group straight away.");
        _interval = Config.Bind("General", "Check every (seconds)", 5f,
            new ConfigDescription("How often to ask the server for your group's progress while you're in the menus.", new AcceptableValueRange<float>(2f, 60f)));
        _notify = Config.Bind("General", "Show a message", true, "Show a message when a friend's progress is added to your quests.");

        var harmony = new Harmony(Guid);
        harmony.Patch(AccessTools.Constructor(typeof(QuestControllerClientBackend), new[] { typeof(Profile), typeof(EFT.InventoryLogic.InventoryController), typeof(IQuestSession) }),
            postfix: new HarmonyMethod(typeof(Plugin), nameof(ControllerCreated)));
        harmony.Patch(AccessTools.Method(typeof(QuestControllerClient), nameof(QuestControllerClient.Dispose)),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(ControllerDisposed)));
        Log.LogInfo("Loaded");
    }

    static void ControllerCreated(QuestControllerClientBackend __instance, Profile profile)
    {
        if (profile == null || profile.Side == EPlayerSide.Savage) return;
        lock (Controllers) Controllers.Add((__instance, profile));
    }

    static void ControllerDisposed(QuestControllerClient __instance)
    {
        lock (Controllers) Controllers.RemoveAll(c => ReferenceEquals(c.Controller, __instance));
    }

    //====================[ Asking the server ]====================
    void Update()
    {
        if (_enabled == null) return;

        if (_pending != null)
        {
            if (!_pending.IsCompleted) return;
            var task = _pending;
            _pending = null;
            Handle(task, _pendingProfile);
            return;
        }

        if (!_enabled.Value || Time.unscaledTime < _nextCheck && !_reportAgain) return;
        _nextCheck = Time.unscaledTime + _interval.Value;
        _reportAgain = false;

        // Not during a raid (or in the hideout): the game sends its quests itself at the end of a raid.
        if (Singleton<GameWorld>.Instantiated) return;

        var profile = PatchConstants.BackEndSession?.Profile;
        if (profile == null) return;
        var controllers = Current(profile);
        if (controllers.Count == 0)
        {
            if (!_warnedMismatch && Controllers.Count > 0)
            {
                _warnedMismatch = true;
                Log.LogWarning("The menu's quest list isn't for the current profile yet; waiting");
            }
            return;
        }
        if (_warnedMismatch)
        {
            _warnedMismatch = false;
            Log.LogInfo("Found the quest list for the current profile");
        }

        var request = Report(profile, controllers);
        _pendingProfile = profile;
        _pending = RequestHandler.PostJsonAsync(Route, JsonConvert.SerializeObject(request));
    }

    // The quest lists for this profile. Normally the ones made for this exact profile object. If there are
    // none (some menus and mods hand the game a fresh copy of the profile), the newest list made for the
    // same player is used. Lists made for an older copy are dropped so they don't pile up.
    static List<(QuestControllerClientBackend Controller, Profile Profile)> Current(Profile profile)
    {
        lock (Controllers)
        {
            var exact = Controllers.Where(c => ReferenceEquals(c.Profile, profile)).ToList();
            if (exact.Count > 0)
            {
                Controllers.RemoveAll(c => !ReferenceEquals(c.Profile, profile) && c.Profile.ProfileId == profile.ProfileId);
                return exact;
            }
            var same = Controllers.LastOrDefault(c => c.Profile.ProfileId == profile.ProfileId);
            return same.Controller == null ? new() : new() { same };
        }
    }

    // What this game has right now. The server keeps it so it knows which progress the game already has.
    static SyncRequest Report(Profile profile, List<(QuestControllerClientBackend Controller, Profile Profile)> controllers)
    {
        var request = new SyncRequest();
        foreach (var pair in profile.TaskConditionCounters ?? new Dictionary<MongoID, TaskConditionCounter>())
            if (pair.Value != null) request.Counters[pair.Key.ToString()] = pair.Value.Value;

        foreach (var (controller, _) in controllers)
        {
            if (controller.Quests == null) continue;
            foreach (var quest in controller.Quests)
            {
                if (quest == null || quest.Id == null) continue;
                var status = (int)quest.QuestStatus;
                var conditions = quest.CompletedConditions?.Select(c => c.ToString()) ?? Enumerable.Empty<string>();
                if (request.Quests.TryGetValue(quest.Id, out var have))
                {
                    have.Status = Math.Max(have.Status, status);
                    have.Conditions = have.Conditions.Union(conditions).ToList();
                }
                else request.Quests[quest.Id] = new QuestReport { Status = status, Conditions = conditions.ToList() };
            }
        }
        return request;
    }

    void Handle(Task<string> task, Profile profile)
    {
        SyncReply reply;
        try
        {
            if (task.IsFaulted || string.IsNullOrEmpty(task.Result)) throw task.Exception?.GetBaseException() ?? new Exception("no answer");
            reply = JsonConvert.DeserializeObject<SyncReply>(task.Result);
        }
        catch (Exception e)
        {
            // An older Shared Quests (or none) on the server: try again much later.
            Log.LogWarning($"The server didn't answer ({e.Message}). Is Shared Quests 1.1.0 or newer installed on the server? Trying again in 2 minutes");
            _nextCheck = Time.unscaledTime + 120f;
            return;
        }

        if (reply == null || !reply.Enabled)
        {
            _nextCheck = Time.unscaledTime + 60f;
            return;
        }

        if (!ReferenceEquals(profile, PatchConstants.BackEndSession?.Profile) || Singleton<GameWorld>.Instantiated) return;
        try
        {
            var updated = Apply(reply, Current(profile));
            if (updated.Count == 0) return;
            // Tell the server straight away what the game has now.
            _reportAgain = true;
            Log.LogInfo($"Added progress from your group: {string.Join(", ", updated)}");
            if (_notify.Value)
                NotificationManager.DisplayMessageNotification($"Shared Quests: progress from your group on {string.Join(", ", updated)}",
                    ENotificationDurationType.Default, ENotificationIconType.Quest, null);
        }
        catch (Exception e)
        {
            Log.LogError($"Couldn't add your group's progress: {e}");
            _nextCheck = Time.unscaledTime + 60f;
        }
    }

    //====================[ Putting it into the game ]====================
    // Returns the names of the quests that changed.
    static List<string> Apply(SyncReply reply, List<(QuestControllerClientBackend Controller, Profile Profile)> controllers)
    {
        var updated = new List<string>();
        var changedIds = new HashSet<string>();

        foreach (var (controller, profile) in controllers)
        {
            if (controller.Quests == null) continue;
            foreach (var quest in controller.Quests.ToList())
            {
                if (quest?.Id == null || !reply.Quests.TryGetValue(quest.Id, out var server)) continue;
                if (quest.QuestStatus != EQuestStatus.Started && quest.QuestStatus != EQuestStatus.AvailableForFinish) continue;
                var changed = changedIds.Contains(quest.Id);

                // Counters. Kill-style counters live in the quest's counter list; items handed in, weapons
                // built and items sold to traders live on the profile, keyed by the step they count for.
                // Both are shared by all of the game's quest lists.
                var counters = quest.ConditionCountersManager;
                var steps = new List<Condition>();
                foreach (var pair in reply.Counters)
                {
                    if (pair.Value.Quest != quest.Id) continue;
                    var id = new MongoID(pair.Key);
                    var value = (int)Math.Round(pair.Value.Value);
                    var counter = counters?.GetCounter(id);
                    Condition step = null;
                    if (counter == null)
                    {
                        step = quest.ProgressCheckers?.Keys.FirstOrDefault(c => c != null && c.id == id);
                        if (step == null) continue;
                        counter = profile.GetTaskConditionCounter(quest, id);
                    }
                    if (counter == null || counter.Value >= value) continue;
                    counter.Value = value;
                    if (step != null) steps.Add(step);
                    changed = true;
                }

                // Finished steps.
                if (server.Conditions != null)
                {
                    quest.CompletedConditions ??= new HashSet<MongoID>();
                    foreach (var condition in server.Conditions)
                        if (quest.CompletedConditions.Add(new MongoID(condition))) changed = true;
                }

                if (!changed) continue;
                // The same refresh the game does after a hand-in.
                quest.CheckForStatusChange(true, false);
                foreach (var step in steps)
                    if (quest.ProgressCheckers.TryGetValue(step, out var checker)) checker?.CallConditionChanged();
                quest.CallOnConditionChangedEvent(quest);

                // Every step is done: ready to turn in.
                if ((EQuestStatus)server.Status == EQuestStatus.AvailableForFinish && quest.QuestStatus == EQuestStatus.Started)
                    quest.SetStatus(EQuestStatus.AvailableForFinish, true, true);

                if (changedIds.Add(quest.Id)) updated.Add(quest.Template?.Name ?? quest.Id);
            }
        }
        return updated;
    }
}

//====================[ What goes to and from the server ]====================
public class QuestReport
{
    [JsonProperty("status")] public int Status;
    [JsonProperty("conditions")] public List<string> Conditions = new();
}

public class CounterReport
{
    [JsonProperty("value")] public double Value;
    [JsonProperty("quest")] public string Quest = "";
}

public class SyncRequest
{
    [JsonProperty("counters")] public Dictionary<string, int> Counters = new();
    [JsonProperty("quests")] public Dictionary<string, QuestReport> Quests = new();
}

public class SyncReply
{
    [JsonProperty("enabled")] public bool Enabled;
    [JsonProperty("counters")] public Dictionary<string, CounterReport> Counters = new();
    [JsonProperty("quests")] public Dictionary<string, QuestReport> Quests = new();
}
