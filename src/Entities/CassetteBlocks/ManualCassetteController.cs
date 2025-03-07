﻿using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.CommunalHelper.Entities;

public class ManualCassetteController : AbstractInputController
{
    private readonly int startIndex;

    private int roomBeats;
    private int currentIndex;

    public ManualCassetteController(EntityData data)
    {
        startIndex = data.Int("startIndex", 0);

        Visible = Collidable = false;
    }

    public override void Awake(Scene scene)
    {
        base.Awake(scene);

        if (Scene.Tracker.GetEntity<CassetteBlockManager>() != null)
            throw new Exception("CassetteBlockManager detected in same room as ManualCassetteController");

        roomBeats = SceneAs<Level>().CassetteBlockBeats;

        if (!(startIndex >= 0 && startIndex < roomBeats))
            throw new IndexOutOfRangeException("ManualCassetteController startIndex is outside of the number of CassetteBlock indices present");
        currentIndex = startIndex;

        SetActiveIndex(currentIndex, true);
    }

    public override void Update()
    {

        base.Update();
        if (CommunalHelperModule.Settings.CycleCassetteBlocks.Pressed)
        {
            Tick();
        }

    }

    public override void FrozenUpdate()
    {
        if (CommunalHelperModule.Settings.CycleCassetteBlocks.Pressed)
        {
            Tick();
        }
    }

    public void Tick()
    {
        currentIndex++;
        currentIndex %= roomBeats;
        SetActiveIndex(currentIndex);
        Audio.Play("event:/game/general/cassette_block_switch_" + ((currentIndex % 2) + 1));
        Input.Rumble(RumbleStrength.Medium, RumbleLength.Short);
    }

    public void SetActiveIndex(int index, bool silent = false)
    {
        foreach (CassetteBlock entity in Scene.Tracker.GetEntities<CassetteBlock>())
        {
            entity.Activated = entity.Index == index;
            bool activated = entity.Index == index;
            if (silent)
                entity.SetActivatedSilently(activated);
            else
                entity.Activated = activated;
        }
    }

    private static IDetour hook_Level_orig_LoadLevel;
    private static IDetour hook_TransitionListener_OnOutBegin_Closure;

    internal static new void Load()
    {
        MethodInfo m_TransitionListener_Closure = typeof(CassetteBlockManager).GetMethod("<.ctor>b__10_0", BindingFlags.NonPublic | BindingFlags.Instance);

        hook_TransitionListener_OnOutBegin_Closure = new ILHook(m_TransitionListener_Closure, TransitionListener_OnOutBegin_Closure);
        hook_Level_orig_LoadLevel = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), Level_orig_LoadLevel);
    }

    internal static new void Unload()
    {
        hook_TransitionListener_OnOutBegin_Closure.Dispose();
        hook_Level_orig_LoadLevel.Dispose();
    }

    private static void Level_orig_LoadLevel(ILContext il)
    {
        /*
        if (HasCassetteBlocks && ShouldCreateCassetteManager)
            // We're just after the `base`
            base.Tracker.GetEntity<CassetteBlockManager>()?.OnLevelStart();
        */

        ILCursor cursor = new(il);
        cursor.GotoNext(instr => instr.MatchCallvirt<CassetteBlockManager>("OnLevelStart"));
        cursor.GotoPrev(instr => instr.MatchCall<Scene>("get_Tracker"));

        // Just gonna borrow the level object for a bit
        cursor.EmitDelegate<Func<Level, Level>>(level =>
        {
            // This could be checked for as part of `Everest.Events.Level.OnLoadEntity` but meh
            EntityData data = level.Session.LevelData.Entities.FirstOrDefault(entityData => entityData.Name == "CommunalHelper/ManualCassetteController");
            if (data != null)
            {
                level.Tracker.GetEntity<CassetteBlockManager>()?.RemoveSelf();
                level.Add(new ManualCassetteController(data));
                // Lists were just updated so there's no harm in doing it again (hopefully)
                level.Entities.UpdateLists();
            }
            return level;
        });
    }

    private static void TransitionListener_OnOutBegin_Closure(ILContext il)
    {
        // we need to add a null check over Scene before doing anything
        ILCursor cursor = new(il);

        var m_getScene = typeof(Entity).GetProperty("Scene").GetGetMethod(true);

        ILLabel afterReturnLabel = cursor.DefineLabel();

        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Callvirt, m_getScene);
        cursor.Emit(OpCodes.Brtrue, afterReturnLabel);
        cursor.Emit(OpCodes.Ret);

        cursor.MarkLabel(afterReturnLabel);
    }
}
