using Godot;
using Project.Core;
using Project.Interface;
using Project.Interface.Menus;
using System.Collections.Generic;

namespace Project.Gameplay;

/// <summary>
/// Stage settings.
/// Must be the first thing loaded in a level.
/// </summary>
public partial class StageSettings : Node3D
{
	public static StageSettings Instance;
	public static PlayerController Player { get; private set; }
	public static void RegisterPlayer(PlayerController player) => Player = player;

	public bool IsControlTest => Data.LevelID == OptionsLevelId;

	private readonly string OptionsLevelId = "options";
	private readonly string ErazorLevelId = "np_boss";
	private readonly string LastBossLevelId = "np_last";

	private int probeTimer;

	private readonly int SkillSaverAchievementRequirement = 30;
	private readonly int SkillMasterAchievementRequirement = 20;
	private readonly int RebellionAchievementRequirement = 25;
	private readonly StringName SkillSaverAchievementName = "skill saver";
	private readonly StringName SkillQuintiAchievementName = "skill quinti";
	private readonly StringName FireMasterAchievementName = "flame master";
	private readonly StringName WindMasterAchievementName = "wind master";
	private readonly StringName DarkMasterAchievementName = "dark master";
	private readonly StringName HeroAchievementName = "hero";
	private readonly StringName TrueHeroAchievementName = "true hero";
	private readonly StringName RebellionAchievementName = "rebellion";

	public override void _EnterTree()
	{
		Instance = this; // Always override previous instance

		// Rebuild dialog libraries to account for modded locales
		dialogLibrary?.LocalizeAudioStreams(true);

		for (int i = 0; i < pathParent.GetChildCount(); i++)
		{
			Path3D path = pathParent.GetChildOrNull<Path3D>(i);
			if (path != null)
				pathList.Add(path);
		}

		CalculateTechnicalBonus();
		UpdateScore(0, MathModeEnum.Replace);
		UpdateQualitySettings();

		// Update gameplay sfx audio channel
		SoundManager.instance?.SetStageMusicVolume(0f);

		if (IsControlTest)
		{
			LevelState = LevelStateEnum.Ingame;
		}
		else
		{
			GetQualityNodesRecursively(this);
			LevelState = LevelStateEnum.Probes;
			if (!TransitionManager.Instance.IsReloadingScene)
				TransitionManager.Instance.UpdateLoadingText("load_probes");
		}

		EquipRequiredSkill();
	}

	private bool wasSkillForceEquipped;
	private SkillKey conflictingSkill = SkillKey.Count;
	private int conflictingSkillIndex = 0;
	/// <summary> For Lost Prologue: Force equip skills needed for tutorials. </summary>
	private void EquipRequiredSkill()
	{
		if (Data.RequiredSkill == null || SaveManager.ActiveSkillRing.IsSkillEquipped(Data.RequiredSkill))
			return;

		if (SaveManager.ActiveSkillRing.IsSkillEquipped(Data.RequiredSkill.Key)) // Player has a different augment equipped
			conflictingSkill = Data.RequiredSkill.Key;
		else
			conflictingSkill = SaveManager.ActiveSkillRing.IsConflictingSkillEquipped(Data.RequiredSkill.Key);

		if (conflictingSkill != SkillKey.Count)
		{
			conflictingSkillIndex = SaveManager.ActiveSkillRing.GetAugmentIndex(conflictingSkill);
			SaveManager.ActiveSkillRing.ForceUnequipSkill(conflictingSkill, conflictingSkillIndex);
			GD.Print($"Force unequipped {conflictingSkill} {conflictingSkillIndex}");
		}

		wasSkillForceEquipped = true;
		SaveManager.ActiveSkillRing.EquipSkill(Data.RequiredSkill.Key, Data.RequiredSkill.AugmentIndex, true);
		GD.Print($"Force equipped {Data.RequiredSkill.Key} {Data.RequiredSkill.AugmentIndex}");
	}

	/// <summary> Restores skills back to whatever we started with. </summary>
	private void RevertRequiredSkill()
	{
		if (!wasSkillForceEquipped) // Nothing to revert to.
			return;

		SaveManager.ActiveSkillRing.UnequipSkill(Data.RequiredSkill.Key, Data.RequiredSkill.AugmentIndex);
		GD.Print($"Unequipped {Data.RequiredSkill.Key} {Data.RequiredSkill.AugmentIndex}");

		if (conflictingSkill != SkillKey.Count)
		{
			SaveManager.ActiveSkillRing.EquipSkill(conflictingSkill, conflictingSkillIndex);
			GD.Print($"Requipped {conflictingSkill} {conflictingSkillIndex}");
		}
	}

	public override void _Ready()
	{
		if (IsControlTest)
			return;

		// Fixes obnoxious flickering when testing from the editor
		if (OS.IsDebugBuild() && !TransitionManager.IsTransitionActive)
		{
			TransitionManager.StartTransition(new()
			{
				outSpeed = .5f,
				color = Colors.Black,
				disableAutoTransition = true,
			});
		}

		SetEnvironmentFxFactor(environmentFxFactor, 0);

		CollisionRoot ??= GetNodeOrNull<Node3D>("Collision");
		if (CollisionRoot != null)
		{
			DebugManager.Instance.CollisionToggled += UpdateCollisionVisibility;
			UpdateCollisionVisibility();
		}

		string bgmID;
		currentBGM = null;
		if (SaveManager.ActiveGameData.selectedMusic?.ContainsKey(Data.LevelID) == true)
		{
			SaveManager.ActiveGameData.selectedMusic.TryGetValue(Data.LevelID, out bgmID);
			GD.Print("bgmID: " + bgmID);

			if (bgmID.GetExtension() != "wav" && bgmID.GetExtension() != "ogg" && bgmID.GetExtension() != "mp3")
			{
				string path = ResourceUid.GetIdPath(ResourceUid.TextToId(bgmID));
				path = path.Replace(".remap", string.Empty);
				currentBGM = (BGMResource)ResourceLoader.Load(path);
			}
			else
			{
				currentBGM = SaveManager.Instance.LoadPRM(bgmID);
				GD.Print("LOADING RESOURCE: " + bgmID.GetBaseName() + ".tres");
			}

		}

		if (currentBGM == null)
			SoundManager.instance.UpdateBgmResource(DefaultBgm);
		else
			SoundManager.instance.UpdateBgmResource(currentBGM);

		GD.Print("current level: " + Data.LevelID);
		GD.Print("selected music: " + SaveManager.ActiveGameData.selectedMusic);
	}

	public override void _ExitTree()
	{
		RevertRequiredSkill();
		EmitSignal(SignalName.Unloaded);
	}

	private void UpdateCollisionVisibility() => CollisionRoot.Visible = !DebugManager.IsCollisionCulled;

	public void UpdateQualitySettings()
	{
		bool postProcessingEnabled = SaveManager.Config.postProcessingQuality != SaveManager.QualitySetting.Disabled;
		Environment.Environment.SsaoEnabled = postProcessingEnabled;
		Environment.Environment.SsilEnabled = postProcessingEnabled;
		Environment.Environment.GlowEnabled = SaveManager.Config.bloomMode != SaveManager.QualitySetting.Disabled;

		switch (SaveManager.Config.softShadowQuality)
		{
			case SaveManager.QualitySetting.Disabled:
			case SaveManager.QualitySetting.Low:
				targetDirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
				break;
			case SaveManager.QualitySetting.Medium:
				targetBlendSplitMode = true;
				targetDirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
				break;
			case SaveManager.QualitySetting.High:
				targetBlendSplitMode = true;
				targetDirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
				break;
		}
	}

	#region Shader Compilation
	[Signal]
	public delegate void LevelStartedEventHandler();
	private Queue<ReflectionProbe> probes = [];
	private Queue<OmniLight3D> omniLights = [];
	private Queue<DirectionalLight3D> directionalLights = [];
	private bool targetBlendSplitMode = false;
	private DirectionalLight3D.ShadowMode targetDirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
	private OmniLight3D.ShadowMode targetOmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid;

	public override void _Process(double _)
	{
		if (LevelState == LevelStateEnum.Probes)
		{
			if (directionalLights.TryDequeue(out DirectionalLight3D dirLight) && dirLight.ShadowEnabled)
			{
				dirLight.DirectionalShadowMode = targetDirectionalShadowMode;
				dirLight.DirectionalShadowBlendSplits = targetBlendSplitMode;
				return;
			}

			if (omniLights.TryDequeue(out OmniLight3D omniLight) && omniLight.ShadowEnabled)
			{
				omniLight.OmniShadowMode = targetOmniShadowMode;
				omniLight.DistanceFadeLength = 10;
				omniLight.DistanceFadeEnabled = true;
				return;
			}

			if (probeTimer > 0)
			{
				probeTimer--;
				return;
			}

			if (probes.TryDequeue(out ReflectionProbe probe))
			{
				probe.EnableShadows = false;
				probe.MeshLodThreshold = 5;

				/*
				WORKAROUND
				Godot's reflection probes update really slowly when set to "Once" mode,
				so we need to wait until the probes finish calculating.

				TODO Replace this with proper update-mode once Godot fixes reflection probes
				*/

				probeTimer = 8; // Each probes takes about 8 frames to update
				probe.UpdateMode = ReflectionProbe.UpdateModeEnum.Once;
				probe.ProcessMode = ProcessModeEnum.Disabled;
				return;
			}

			StartLevel();
			return;
		}

		UpdateTime();
		UpdateEnvironmentFXFactor();
	}

	private void GetQualityNodesRecursively(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			GetQualityNodesRecursively(child);

			if (child is ReflectionProbe)
				probes.Enqueue(child as ReflectionProbe);
			else if (child is DirectionalLight3D)
				directionalLights.Enqueue(child as DirectionalLight3D);
			else if (child is OmniLight3D)
				omniLights.Enqueue(child as OmniLight3D);
		}
	}

	private void StartLevel()
	{
		LevelState = LevelStateEnum.Ingame;
		TransitionManager.FinishTransition();
		EmitSignal(SignalName.LevelStarted);
	}
	#endregion

	#region Level Settings
	/// <summary> Reference to the level's data. </summary>
	[Export] public LevelDataResource Data { get; private set; }
	[Export] public BGMResource DefaultBgm { get; private set; }
	private BGMResource currentBGM;
	[Export] private bool disableObjectiveAutocompletion;
	[Export] public Node3D CollisionRoot { get; private set; }
	[Export] public CameraSettingsResource InitialCameraSettings { get; private set; }
	[Export] public SFXLibraryResource dialogLibrary;

	/// <summary>
	/// Calculates the rank [Fail = -1, None = 0, Bronze = 1, Silver = 2, Gold = 3]
	/// </summary>
	public int CalculateRank(bool preCountBonuses = false)
	{
		if (LevelState == LevelStateEnum.Failed)
			return -1;

		int rank = 0; // DEFAULT - No rank
		float completionTime = Mathf.RoundToInt(CurrentTime * 100f) * 0.01f; // Round to nearest millisecond

		if (TimeAttackManager.Instance.IsRunActive)
		{
			if (completionTime <= Data.GoldTimeTA)
			{
				rank = 3;
			}
		}
		else
		{
			if (Data.SkipScore)
			{
				if (completionTime <= Data.GoldTime)
					rank = 3;
				else if (completionTime <= Data.SilverTime)
					rank = 2;
				else if (completionTime <= Data.BronzeTime)
					rank = 1;
			}
			else
			{
				int score = TotalScore;
				if (preCountBonuses)
					score += BonusManager.instance.QueuedScore;

				if (completionTime <= Data.GoldTime && score >= Data.Score) // Perfect run
					rank = 3;
				else if (completionTime <= Data.SilverTime && score >= Data.SilverScore) // Silver score reqs are always 3/4 of gold
					rank = 2;
				else if (completionTime <= Data.BronzeTime || score >= Data.SilverScore) // Bronze is easy to get
					rank = 1;
			}

			if (rank >= 3 && RespawnCount != 0) // Limit to silver if a respawn occured
				rank = 2;
		}


		return rank;
	}
	#endregion

	public string GetRequiredTime(int rank)
	{
		if (TimeAttackManager.Instance.IsRunActive)
			return ExtensionMethods.FormatTime(Data.GoldTimeTA);

		switch (rank)
		{
			case 0:
				return ExtensionMethods.FormatTime(Data.BronzeTime);
			case 1:
				return ExtensionMethods.FormatTime(Data.SilverTime);
			case 2:
				return ExtensionMethods.FormatTime(Data.GoldTime);
			default:
				return "00:00.00";
		}
	}

	#region Level Data
	public enum MathModeEnum // List of ways the score can be modified
	{
		Add,
		Subtract,
		Multiply,
		Replace
	}
	/// <summary> Calculates value based on provided MathMode. </summary>
	private static int CalculateMath(int value, int amount, MathModeEnum mode)
	{
		switch (mode)
		{
			case MathModeEnum.Add:
				value += amount;
				break;
			case MathModeEnum.Subtract:
				value -= amount;
				if (value < 0) // Clamp to zero
					value = 0;
				break;
			case MathModeEnum.Multiply:
				value *= amount;
				break;
			case MathModeEnum.Replace:
				value = amount;
				break;
		}
		return value;
	}

	[Signal]
	public delegate void ScoreChangedEventHandler(); // Score has changed, normally occours from a bonus
	public int CurrentScore { get; private set; } // How high is the current score?
	public string DisplayScore { get; private set; } // Current score formatted to eight zeros
	/// <summary> Total score, including ring and technical bonus. </summary>
	public int TotalScore => CurrentScore + Mathf.CeilToInt(RingBonus * TechnicalBonus);
	public void UpdateScore(int amount, MathModeEnum mode)
	{
		CurrentScore = CalculateMath(CurrentScore, amount, mode);
		DisplayScore = ExtensionMethods.FormatMenuNumber(CurrentScore);
		EmitSignal(SignalName.ScoreChanged);
	}

	/// <summary> How many times has the player taken damage? </summary>
	public int DamageCount { get; private set; }
	/// <summary> The number of times the player has respawned. </summary>
	public int RespawnCount { get; private set; } // How high many times did the player have to respawn?
	public void IncrementDamageCount()
	{
		DamageCount++;
		CalculateTechnicalBonus();
	}
	public void IncrementRespawnCount()
	{
		RespawnCount++;
		CalculateTechnicalBonus();
	}
	public float TechnicalBonus { get; private set; }

	private void CalculateTechnicalBonus()
	{
		if (LevelState == LevelStateEnum.Failed)
		{
			// Failing the level gives a technical bonus of .5
			TechnicalBonus = .5f;
			return;
		}

		if (RespawnCount != 0 || DamageCount >= 6)
		{
			// Respawning automatically means 1.0
			TechnicalBonus = 1.0f;
			return;
		}

		// Damage values
		if (DamageCount >= 4)
		{
			// 4-5
			TechnicalBonus = 1.1f;
			return;
		}

		if (DamageCount >= 2)
		{
			// 2-3
			TechnicalBonus = 1.2f;
			return;
		}

		if (DamageCount == 1)
		{
			// 1
			TechnicalBonus = 1.5f;
			return;
		}

		TechnicalBonus = 2.0f; // Perfect run
	}

	// Objectives
	public int CurrentObjectiveCount { get; private set; } // How much has the player currently completed?
	[Signal]
	public delegate void ObjectiveChangedEventHandler(); // Progress towards the objective has changed
	[Signal]
	public delegate void ObjectiveResetEventHandler(); // Progress towards the objective has changed
	public void IncrementObjective()
	{
		if (!IsObjectiveIncrementValid())
			return;

		CurrentObjectiveCount++;
		CurrentObjectiveCount = Mathf.Clamp(CurrentObjectiveCount, 0, Data.MissionObjectiveCount);
		HeadsUpDisplay.Instance.PlayObjectiveAnimation("good");
		EmitSignal(SignalName.ObjectiveChanged);

		if (disableObjectiveAutocompletion)
			return;

		if (Data.MissionObjectiveCount == 0) // i.e. Sand Oasis's "Don't break the jars!" mission.
		{
			FinishLevel(false);
		}
		else if (CurrentObjectiveCount >= Data.MissionObjectiveCount &&
				Data.MissionType != LevelDataResource.MissionTypeEnum.Chain)
		{
			FinishLevel(true);
		}
	}

	/// <summary> Check if the object should actually be incremented. </summary>
	private bool IsObjectiveIncrementValid()
	{
		if (Data.RequiredSkill == null)
			return true;

		if (Data.MissionType == LevelDataResource.MissionTypeEnum.Enemy) // Validate enemy defeats
		{
			if (Data.RequiredSkill.Key == SkillKey.SlideAttack && !Player.IsSliding)
				return false;

			if (Data.RequiredSkill.Key == SkillKey.PerfectHomingAttack && !Player.IsPerfectHomingAttacking)
				return false;

			return true;
		}

		if (Data.RequiredSkill.Key == SkillKey.StompAttack && !Player.IsStomping)
			return false;

		return true;
	}

	public void ResetObjective(int progress = 0)
	{
		CurrentObjectiveCount = progress;

		if (progress == 0 && Player.IsDefeated)
			HeadsUpDisplay.Instance.PlayObjectiveAnimation("bad");

		EmitSignal(SignalName.ObjectiveReset);
	}

	// Rings
	public int CurrentRingCount { get; private set; } // How many rings is the player currently holding?
	public int RingBonus { get; private set; }
	[Signal]
	public delegate void RingChangedEventHandler(int change); // Ring count has changed
	public void UpdateRingCount(int amount, MathModeEnum mode, bool disableAnimations = false)
	{
		int previousAmount = CurrentRingCount;
		CurrentRingCount = CalculateMath(CurrentRingCount, amount, mode);
		RingBonus = CurrentRingCount * 10;
		if (Data.MissionType == LevelDataResource.MissionTypeEnum.Ring &&
			CurrentRingCount >= Data.MissionObjectiveCount &&
			Data.MissionObjectiveCount != 0) // For ring based missions
		{
			CurrentRingCount = Data.MissionObjectiveCount; // Clamp
			FinishLevel(true);
		}

		// Soul barrier
		if (Player == null)
		{
			GD.PushError("PlayerController is missing!");
			if (mode == MathModeEnum.Subtract && SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.RingLossConvert))
				Player.Skills.ModifySoulGauge((previousAmount - CurrentRingCount) * 2);
		}

		if (DebugManager.Instance.InfiniteRings) // Infinite ring cheat
			CurrentRingCount = 999;

		EmitSignal(SignalName.RingChanged, CurrentRingCount - previousAmount, disableAnimations);
	}

	public int CurrentEXP { get; set; } // How much exp is the player earning from this stage?

	// Time
	[Signal]
	public delegate void TimeChangedEventHandler(); // Time has changed.

	public float CurrentTime { get; private set; } // How long has the player been on this level? (In Seconds)
	public string DisplayTime { get; private set; } // Current time formatted in mm:ss.ff
	private void UpdateTime(bool skipPhysicsTick = false)
	{
		if (!IsLevelIngame || !Interface.PauseMenu.AllowInputs) return;

		if (!skipPhysicsTick)
			CurrentTime += PhysicsManager.normalDelta; // Add current time
		DisplayTime = ExtensionMethods.FormatTime(CurrentTime);
		if (Data.MissionTimeLimit != 0 && CurrentTime >= Data.MissionTimeLimit) // Time's up!
			FinishLevel(false);

		EmitSignal(SignalName.TimeChanged);
	}

	/// <summary> Artifically add time. Used when skipping cutscenes. </summary>
	public void AddTime(float amount)
	{
		CurrentTime += amount;
		UpdateTime(true);
	}

	private bool[] fireSoulCheckpoints = new bool[3];
	public bool IsFireSoulCheckpointFlagSet(int index) => fireSoulCheckpoints[index];
	public bool SetFireSoulCheckpointFlag(int index, bool value) => fireSoulCheckpoints[index] = value;
	#endregion

	#region Path Settings
	[Export(PropertyHint.NodeType, "Node3D")]
	private Node3D pathParent;
	/// <summary> List of all level paths contained for this level. </summary>
	private readonly List<Path3D> pathList = [];

	/// <summary>
	/// Returns the path the player is currently the closest to.
	/// Allows placing the player anywhere in the editor without needing to manually assign paths.
	/// </summary>
	public Path3D CalculateStartingPath(Vector3 globalPosition)
	{
		int closestPathIndex = -1;
		float closestDistanceSquared = Mathf.Inf;

		for (int i = 0; i < pathList.Count; i++)
		{
			if (!pathList[i].Visible)
				continue;

			Vector3 closestPoint = pathList[i].Curve.GetClosestPoint(globalPosition - pathList[i].GlobalPosition);
			closestPoint += pathList[i].GlobalPosition;
			float dstSquared = globalPosition.DistanceSquaredTo(closestPoint);

			if (dstSquared < closestDistanceSquared)
			{
				closestPathIndex = i;
				closestDistanceSquared = dstSquared;
			}
		}

		if (closestPathIndex == -1)
			return null;

		return pathList[closestPathIndex];
	}
	#endregion

	#region Object Spawning
	// Checkpoint data
	[Signal]
	public delegate void TriggeredCheckpointEventHandler();
	public Triggers.CheckpointTrigger CurrentCheckpoint { get; private set; }
	private int CheckpointScore { get; set; }
	private int CheckpointObjectiveCount { get; set; }
	private float CheckpointEnvironmentFxFactor { get; set; }
	public void SetCheckpoint(Triggers.CheckpointTrigger checkpoint)
	{
		if (checkpoint == CurrentCheckpoint) return; // Already at this checkpoint

		CurrentCheckpoint = checkpoint;
		CheckpointScore = CurrentScore;
		CheckpointObjectiveCount = CurrentObjectiveCount;
		CheckpointEnvironmentFxFactor = targetEnvironmentFxFactor;
		EmitSignal(SignalName.TriggeredCheckpoint);
	}

	public void RevertToCheckpointData()
	{
		ResetObjective(CheckpointObjectiveCount);
		UpdateScore(CheckpointScore, MathModeEnum.Replace);
		SetEnvironmentFxFactor(CheckpointEnvironmentFxFactor, 0);
	}

	[Signal]
	public delegate void RespawnedEventHandler();
	[Signal]
	public delegate void RespawnedEnemiesEventHandler();
	[Signal]
	public delegate void UnloadedEventHandler();

	public void StartRespawn()
	{
		SoundManager.instance.CancelDialog(); // Cancel any active dialog
		GetTree().CreateTimer(PhysicsManager.physicsDelta, false, true).Timeout += RespawnEnemies;
	}

	private void RespawnEnemies()
	{
		EmitSignal(SignalName.Respawned);
		EmitSignal(SignalName.RespawnedEnemies);
	}
	#endregion

	#region Level Completion
	[Signal] public delegate void LevelCompletedEventHandler(); // Called when the level is completed
	[Signal] public delegate void LevelFailedEventHandler(); // Called when the level is failed
	[Signal] public delegate void LevelSuccessEventHandler(); // Called when the level is successfully finished
	[Signal] public delegate void LevelDemoStartedEventHandler(); // Called when the level demo starts

	public enum LevelStateEnum
	{
		Probes,
		Ingame,
		Failed,
		Success,
	}
	public LevelStateEnum LevelState { get; private set; }
	public bool IsLevelLoading => LevelState == LevelStateEnum.Probes;
	public bool IsLevelIngame => LevelState == LevelStateEnum.Ingame;
	/// <summary> Flag for keeping track of Uhu's race status. </summary>
	public bool IsRaceActive { get; set; }
	private const float FAIL_COMPLETION_DELAY = 1.5f; // Mission fails always have a delay of 1.5 seconds
	public void FinishLevel(bool wasSuccessful)
	{
		if (!IsLevelIngame)
			return;

		// Attempt to start the completion demo
		GetTree().CreateTimer(wasSuccessful ? Data.CompletionDelay : FAIL_COMPLETION_DELAY).Connect(SceneTreeTimer.SignalName.Timeout, new Callable(this, MethodName.StartCompletionDemo));

		SoundManager.instance.IsStageMusicPaused = true;
		SoundManager.instance.CancelDialog();
		PauseMenu.AllowInputs = false;
		LevelState = wasSuccessful ? LevelStateEnum.Success : LevelStateEnum.Failed;

		EmitSignal(SignalName.LevelCompleted);
		EmitSignal(wasSuccessful ? SignalName.LevelSuccess : SignalName.LevelFailed);

		// Process save data after emitting level completion
		CalculateTechnicalBonus(); // Recalculate technical bonus
		UpdateSaveData();
		ProcessAchievements(wasSuccessful);
	}

	private void ProcessAchievements(bool wasSuccessful)
	{
		if (Data.LevelID == ErazorLevelId)
		{
			AchievementManager.Instance.UnlockAchievement(HeroAchievementName);

			if (SaveManager.ActiveGameData.level <= RebellionAchievementRequirement)
				AchievementManager.Instance.UnlockAchievement(RebellionAchievementName);
		}
		else if (Data.LevelID == LastBossLevelId)
		{
			AchievementManager.Instance.UnlockAchievement(TrueHeroAchievementName);
		}

		if (wasSuccessful && SaveManager.ActiveSkillRing.TotalCost <= 100)
		{
			SaveManager.SharedData.MinimalSkillCount = (int)Mathf.MoveToward(SaveManager.SharedData.MinimalSkillCount, int.MaxValue, 1);

			if (SaveManager.SharedData.MinimalSkillCount >= SkillSaverAchievementRequirement)
				AchievementManager.Instance.UnlockAchievement(SkillSaverAchievementName);
		}

		if (SaveManager.ActiveSkillRing.AreSkillsSingleElement(SkillResource.SkillElement.Fire))
		{
			SaveManager.SharedData.FireOnlyCount = (int)Mathf.MoveToward(SaveManager.SharedData.FireOnlyCount, int.MaxValue, 1);

			if (SaveManager.SharedData.FireOnlyCount >= SkillMasterAchievementRequirement)
				AchievementManager.Instance.UnlockAchievement(FireMasterAchievementName);
		}
		else if (SaveManager.ActiveSkillRing.AreSkillsSingleElement(SkillResource.SkillElement.Wind))
		{
			SaveManager.SharedData.WindOnlyCount = (int)Mathf.MoveToward(SaveManager.SharedData.WindOnlyCount, int.MaxValue, 1);

			if (SaveManager.SharedData.WindOnlyCount >= SkillMasterAchievementRequirement)
				AchievementManager.Instance.UnlockAchievement(WindMasterAchievementName);
		}
		else if (SaveManager.ActiveSkillRing.AreSkillsSingleElement(SkillResource.SkillElement.Dark))
		{
			SaveManager.SharedData.DarkOnlyCount = (int)Mathf.MoveToward(SaveManager.SharedData.DarkOnlyCount, int.MaxValue, 1);

			if (SaveManager.SharedData.DarkOnlyCount >= SkillMasterAchievementRequirement)
				AchievementManager.Instance.UnlockAchievement(DarkMasterAchievementName);
		}

		if (SaveManager.SharedData.LevelData.GetSkillessGold(Data.LevelID))
			AchievementManager.Instance.UnlockAchievement(SkillQuintiAchievementName);
	}

	private void UpdateSaveData()
	{
		bool isStageCleared = LevelState == LevelStateEnum.Success;
		int rank = CalculateRank();

		// Write common data to save file
		SaveManager.ActiveGameData.LevelData.SetRank(Data.LevelID, rank);
		SaveManager.ActiveGameData.LevelData.SetClearStatus(Data.LevelID,
			isStageCleared ? SaveManager.LevelSaveData.LevelStatus.Cleared : SaveManager.LevelSaveData.LevelStatus.Attempted);

		if (rank == 3 && SaveManager.ActiveSkillRing.TotalCost == 0)
			SaveManager.ActiveGameData.LevelData.SetSkillessGold(Data.LevelID, true);

		if (!isStageCleared)
			return;

		UpdateUnlockNotifications();

		// Only write these when the stage is a success
		SaveManager.ActiveGameData.LevelData.SetHighScore(Data.LevelID, TotalScore);
		SaveManager.ActiveGameData.LevelData.SetBestTime(Data.LevelID, CurrentTime);

		SaveManager.SharedData.LevelData.SetHighScore(Data.LevelID, TotalScore);
		SaveManager.SharedData.LevelData.SetBestTime(Data.LevelID, CurrentTime);

		// Unlock World Rings, if necessary
		if (Data.WorldRing != SaveManager.WorldEnum.LostPrologue &&
			Data.WorldRing != SaveManager.WorldEnum.Mods &&
			!SaveManager.ActiveGameData.IsWorldRingObtained(Data.WorldRing))
		{
			SaveManager.ActiveGameData.UnlockWorldRing(Data.WorldRing);
			NotificationManager.Instance.AddNotification(NotificationManager.NotificationType.WorldRing, $"unlock_ring_{Data.WorldRing.ToString().ToSnakeCase()}");
		}

		if (Data.LevelID == "np_last" && !SaveManager.SharedData.IsTimeAttackUnlocked)
		{
			SaveManager.SharedData.IsTimeAttackUnlocked = true;
			NotificationManager.Instance.AddNotification(NotificationManager.NotificationType.TimeAttack, "unlock_time_attack");
		}
	}

	private void UpdateUnlockNotifications()
	{
		if (Data.UnlockWorld != SaveManager.WorldEnum.LostPrologue &&
			!SaveManager.ActiveGameData.IsWorldUnlocked(Data.UnlockWorld))
		{
			SaveManager.ActiveGameData.UnlockWorld(Data.UnlockWorld);
			StringName descriptionString = Tr($"unlock_world").Replace("[AREA]", Tr(Data.UnlockWorld.ToString().ToSnakeCase()));
			NotificationManager.Instance.AddNotification(NotificationManager.NotificationType.World, descriptionString);
		}

		int missionsUnlocked = NotificationManager.Instance.CalculateUnlockedSpecialLevelData();
		foreach (LevelDataResource stage in Data.UnlockStage)
		{
			if (SaveManager.ActiveGameData.IsStageUnlocked(stage.LevelID))
				continue;

			SaveManager.ActiveGameData.UnlockStage(stage.LevelID);

			if (!DebugManager.Instance.UseDemoSave) // Only add notification when not using the demo save
				missionsUnlocked++;
		}

		if (missionsUnlocked == 0)
			return;

		NotificationManager.Instance.AddNotification(NotificationManager.NotificationType.Mission,
			missionsUnlocked > 1 ? "unlock_mission_multiple" : "unlock_mission");
	}

	/// <summary> Camera demo that gets enabled after the level is cleared. </summary>
	[Export]
	private AnimationPlayer completionAnimator;
	private int completionAnimationIndex;
	private bool isCompletionDemoActive;
	public void StartCompletionDemo()
	{
		if (isCompletionDemoActive)
			return;

		isCompletionDemoActive = true;
		EmitSignal(SignalName.LevelDemoStarted);

		// Cull objects, if necessary
		if (!Data.DisableObjectCullOnCompletion)
		{
			Node3D objectParent = GetParent().GetChildOrNull<Node3D>(GetIndex() + 1);
			if (objectParent != null) // Object parent should always be the child after the static node
				objectParent.Visible = false;
		}

		if (completionAnimator == null) return;
		OnCameraDemoAdvance();
	}

	/// <summary> Completion demo advanced, play a crossfade. </summary>
	public void OnCameraDemoAdvance()
	{
		completionAnimationIndex++;
		if (completionAnimationIndex > 3)
			completionAnimationIndex = 1;
		completionAnimator.Play($"demo{completionAnimationIndex}");
		Player.Camera.StartCrossfade();
	}

	#endregion

	/// <summary> Reference to active area's WorldEnvironment node. </summary>
	[Export] public WorldEnvironment Environment { get; private set; }
	[Export(PropertyHint.Range, "0,1,.1")] private float environmentFxFactor;
	private float targetEnvironmentFxFactor;
	private float environmentFxVelocity;
	private float environmentFxSmoothing;
	private readonly string ShaderEnvironmentFXParameter = "environment_fx_intensity";
	public void SetEnvironmentFxFactor(float value, float smoothing)
	{
		targetEnvironmentFxFactor = Mathf.Clamp(value, 0f, 1f);
		environmentFxSmoothing = smoothing;
	}

	private void UpdateEnvironmentFXFactor()
	{
		if (Mathf.IsZeroApprox(environmentFxSmoothing))
		{
			environmentFxFactor = targetEnvironmentFxFactor;
			environmentFxVelocity = 0;
		}
		else
		{
			environmentFxFactor = ExtensionMethods.SmoothDamp(environmentFxFactor, targetEnvironmentFxFactor, ref environmentFxVelocity, environmentFxSmoothing);
		}

		RenderingServer.GlobalShaderParameterSet(ShaderEnvironmentFXParameter, environmentFxFactor);
	}
}

public struct SpawnData(Node parent, Transform3D transform)
{
	/// <summary> Original parent node. </summary>
	public Node parentNode = parent;
	/// <summary> Local transform to spawn with. </summary>
	public Transform3D spawnTransform = transform;

	public readonly void Respawn(Node3D n)
	{
		if (parentNode != null && n.GetParent() != parentNode)
		{
			if (n.IsInsideTree()) // Object needs to be reparented first.
				n.GetParent().RemoveChild(n);

			parentNode.AddChild(n);
		}

		n.Visible = true;
		n.ProcessMode = Node.ProcessModeEnum.Inherit;
		n.SetDeferred("transform", spawnTransform);
	}
}
