using Godot;
using Project.Core;
using Project.CustomNodes;
using Project.Gameplay.Objects;
using Project.Gameplay.Triggers;

namespace Project.Gameplay.Bosses;

public partial class AlfLayla : Node3D
{
	[Signal] public delegate void CutsceneFinishedEventHandler();

	[ExportGroup("Components")]
	[Export] private AnimationTree animationTree;
	[Export] private CameraTrigger cutsceneCamera;
	[Export] private LockoutTrigger autorunLockout;
	[Export] private LockoutTrigger stopLockout;
	[Export] private Node3D strikeParent;

	[Export] private SpiritBomb spiritBomb;

	[Export] private CameraTrigger defaultCameraTrigger;
	[Export] private CameraTrigger punchCameraTrigger;

	[Export] private AlfSlash[] slashControllers;
	[Export] private GravityOrb[] gravityOrbs;
	[Export] private PurpleOrb[] purpleOrbs;
	[Export] private BoneAttachment3D[] purpleOrbSpawnPoints;
	[Export] private GpuParticles3D[] smokeParticles;
	[Export] private GroupGpuParticles3D[] explosionParticles;
	[Export] private CameraTrigger[] explosionCameras;
	[Export] private Node3D itemParent;

	[Export] private DialogTrigger[] progressionDialogs;
	[Export] private DialogTrigger[] spiritBombPushDialogs;
	[Export] private DialogTrigger[] spiritBombDamageDialogs;
	private int currentProgressionDialogIndex;
	private int currentPushDialogIndex;
	private int currentDamageDialogIndex;

	private void PlayProgressionDialog()
	{
		if (currentProgressionDialogIndex >= progressionDialogs.Length)
			return;

		progressionDialogs[currentProgressionDialogIndex].Activate();
		currentProgressionDialogIndex++;
	}

	public void PlaySpiritBombPushDialog()
	{
		if (currentPushDialogIndex >= spiritBombPushDialogs.Length)
			return;

		spiritBombPushDialogs[currentPushDialogIndex].Activate();
		currentPushDialogIndex++;
	}

	public void PlaySpiritBombDamageDialog()
	{
		if (currentDamageDialogIndex >= spiritBombDamageDialogs.Length)
			return;

		spiritBombDamageDialogs[currentDamageDialogIndex].Activate();
		currentDamageDialogIndex++;
	}

	private PlayerController Player => StageSettings.Player;
	private PlayerPathController PlayerPathFollower => Player.PathFollower;

	[ExportGroup("Patterns")]
	[Export] private string[] attackPatterns;
	/// <summary> Tracks the index of the current phase. </summary>
	private int currentPatternIndex;
	/// <summary> Tracks the index character being processed in the current phase. </summary>
	private int currentActionIndex;
	/// <summary> Tracks the character associated with the action currently being processed. </summary>
	private char currentActionCharacter;
	private float actionTimer;

	/// <summary> Tracks Alf's current health. </summary>
	private int currentHealth;
	private int lastExplosionHealth;
	private readonly int MaxHealth = 25;
	public bool IsDefeated => currentHealth == 0;

	/// <summary> Tracks Alf's current action. </summary>
	private FightState CurrentFightState;
	private enum FightState
	{
		Introduction,
		Idle,
		Movement,
		AttackWindup,
		AttackStrike,
		Stunned,
		Exploding,
		Defeated,
	}

	[ExportGroup("Movement Settings")]
	/// <summary> Curve that determines how Alf advances. </summary>
	[Export] private Curve advanceMovementCurve;
	/// <summary> Curve that determines how Alf retreats. </summary>
	[Export] private Curve retreatMovementCurve;
	private Curve currentMovementCurve;
	/// <summary> Tracks Alf's current distance to the player. </summary>
	private float currentDistance;
	/// <summary> [0, CurveMaxDomain] Samples the curve. </summary>
	private float movementSample;
	/// <summary> The starting distance of a movement. </summary>
	private float initialDistance;
	/// <summary> The ending distance of a movement. </summary>
	private float targetDistance;
	private readonly float BombDistance = 80.0f;
	private readonly float FarDistance = 40.0f;
	private readonly float NormalDistance = 20.0f;
	private readonly float CloseDistance = 10.0f;

	////////////////////////////////
	///// ANIMATION PARAMETERS /////
	////////////////////////////////
	private readonly string IntroCutsceneID = "last_boss_intro";
	private readonly string DefeatCutsceneID = "last_boss_defeat";
	private readonly string MovePlayback = "parameters/move-state/playback";
	private readonly string SlashType = "parameters/slash-type-transition/transition_request";
	private readonly string SlashTrigger = "parameters/slash-trigger/request";
	private readonly string SlashSpeed = "parameters/slash-speed/scale";
	private readonly string SixOrbTrigger = "parameters/six-orb-trigger/request";
	private readonly string ThreeOrbTrigger = "parameters/three-orb-trigger/request";
	private readonly string SixOrbSpeed = "parameters/six-orb-speed/scale";
	private readonly string SpiritBombTrigger = "parameters/spirit-bomb-trigger/request";
	private AnimationNodeStateMachinePlayback MoveStatePlayback => animationTree.Get(MovePlayback).Obj as AnimationNodeStateMachinePlayback;

	/*
	///////////////////////////////
	///////// ACTION KEYS /////////
	///////////////////////////////
	
	F - Far Distance
	M - Medium Distance
	C - Close Distance

	6 - Six orb attack
	3 - Three orb attack, goes from left to right (L M R)

	\ - \\ Two slashes right
	/ - // Two slashes left
	> - \\\ Three slashes right
	< - /// Three slashes left
	X - cross slash
	_ - Horizontal slash from right to left (<--)
	# - Net slash, covers the whole screen
	| - Three slashes down

	B - Bomb (Move to Bomb distance before using it)
	*/

	public override void _Ready()
	{
		animationTree.Active = true; // Activate animation trees

		StageSettings.Instance.RespawnedEnemies += Respawn;
		StageSettings.Instance.LevelStarted += StartIntroduction;
		spiritBomb.Kicked += AttemptSpiritBombVoiceLine;
	}

	public override void _PhysicsProcess(double _delta)
	{
		ProcessAction();
		switch (CurrentFightState)
		{
			case FightState.Introduction:
				if ((Input.IsActionJustPressed("sys_pause") || Input.IsActionJustPressed("button_jump")) &&
					SaveManager.ActiveGameData.CanSkipCutscene(IntroCutsceneID))
				{
					FinishIntroduction();
				}
				return;
			case FightState.Defeated:
				if ((Input.IsActionJustPressed("sys_pause") || Input.IsActionJustPressed("button_jump")) &&
					SaveManager.ActiveGameData.CanSkipCutscene(DefeatCutsceneID))
				{
					FinishDefeat();
				}

				GlobalTransform = PlayerPathFollower.GlobalTransform;
				GlobalPosition += VisualOffset;
				return;
		}

		if (IsStunned || IsExploding)
			return;

		SnapPosition();
	}

	private void Respawn()
	{
		autorunLockout.Activate();
		stopLockout.Deactivate();
		Player.Animator.CancelOneshot();
		Player.Skills.ModifySoulGauge(-Player.Skills.MaxSoulPower); // Reset soul to 0

		CurrentFightState = FightState.Idle;

		currentHealth = MaxHealth;
		lastExplosionHealth = MaxHealth;
		currentPatternIndex = 0;
		currentActionIndex = 0;
		currentActionCharacter = '\0';
		currentGravityOrbIndex = 0;
		currentExplosionParticleIndex = 0;

		currentDistance = BombDistance;
		targetDistance = BombDistance;
		SnapPosition();
		itemParent.GlobalPosition = Vector3.Zero;
		ShowObjects();

		Player.Camera.LookaroundAmount = Vector2.Zero;

		Transform = Transform3D.Identity;
		ResetPhysicsInterpolation();

		foreach (AlfSlash slash in slashControllers)
			slash.Respawn();

		foreach (GravityOrb orb in gravityOrbs)
			orb.Respawn();

		foreach (PurpleOrb orb in purpleOrbs)
			orb.Respawn();

		foreach (GpuParticles3D particle in smokeParticles)
		{
			particle.Visible = true;
			particle.SetEmitting(true);
		}

		// Reset Animations
		animationTree.Set(SixOrbTrigger, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		animationTree.Set(ThreeOrbTrigger, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		animationTree.Set(SlashTrigger, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		animationTree.Set(SpiritBombTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Abort);
		animationTree.Set(StunDamageFinalTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Abort);
		animationTree.Set(StunDamageTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Abort);
		MoveStatePlayback.Start("idle");
	}

	private readonly string IntroductionTrigger = "parameters/intro-trigger/request";
	private void StartIntroduction()
	{
		StageSettings.Player.KnockbackFinished += OnPlayerKnockbackFinished; // Finish the spirit bomb attack after the player gets up
		spiritBomb.AlfExploded += StartStun;

		GlobalTransform = Player.GlobalTransform;
		GlobalPosition += Vector3.Down * 5f;
		ResetPhysicsInterpolation();

		HideObjects();

		animationTree.Set(IntroductionTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		cutsceneCamera.Activate();
		stopLockout.Activate();
		Interface.PauseMenu.AllowInputs = false;
		HeadsUpDisplay.Instance.SetVisibility(false);
		Player.Skills.DisableBreakSkills();
		Player.Animator.PlayOneshotAnimation(IntroCutsceneID);
	}

	private void FinishIntroduction()
	{
		if (TransitionManager.IsTransitionActive) return; // Player must have skipped the introduction animation

		TransitionManager.StartTransition(new()
		{
			inSpeed = 0f,
			outSpeed = .5f,
			color = Colors.Black
		});
		TransitionManager.Instance.Connect(TransitionManager.SignalName.TransitionProcess, new Callable(this, MethodName.StartBattle), (uint)ConnectFlags.OneShot);
		SaveManager.ActiveGameData.AllowSkippingCutscene(IntroCutsceneID);
		Player.Animator.CancelOneshot();
		EmitSignal(SignalName.CutsceneFinished);
	}

	private void StartBattle()
	{
		cutsceneCamera.Deactivate();
		animationTree.Set(IntroductionTrigger, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		GlobalPosition = Vector3.Down * -5f;

		Respawn();
		Player.Skills.EnableBreakSkills();
		TransitionManager.FinishTransition();
		Interface.PauseMenu.AllowInputs = true;
		HeadsUpDisplay.Instance.SetVisibility(true);
	}

	private readonly string DefeatTrigger = "parameters/defeat-trigger/request";
	private readonly string DefeatSeek = "parameters/defeat-seek/seek_request";
	private void DefeatBoss()
	{
		TransitionManager.StartTransition(new()
		{
			inSpeed = 0f,
			outSpeed = .5f,
			color = Colors.Black
		});
		TransitionManager.FinishTransition();

		Player.Skills.CancelBreakSkills();
		Player.Skills.DisableBreakSkills();
		Player.MoveSpeed = 0;
		Player.StrafeSpeed = 0;
		Player.SnapToGround();
		Player.Effect.CanelSpinFX();
		Player.Effect.StopTrailFX();
		Player.Animator.ResetState(0.0f);
		Player.Animator.PlayOneshotAnimation(DefeatCutsceneID);
		Player.AddLockoutData(Runtime.Instance.DefaultCompletionLockout);
		Player.GlobalRotation = Vector3.Zero;
		Interface.PauseMenu.AllowInputs = false;
		HeadsUpDisplay.Instance.SetVisibility(false);

		cutsceneCamera.Activate();
		animationTree.Set(DefeatTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);

		CurrentFightState = FightState.Defeated;

		// Award 1000 points for defeating the boss
		BonusManager.instance.QueueBonus(new(BonusType.Boss, 1000));
	}

	private void FinishDefeat()
	{
		cutsceneCamera.Deactivate();
		defaultCameraTrigger.Activate();

		animationTree.Set(DefeatSeek, 22f);
		animationTree.SetDeferred("active", false);

		Player.Animator.CancelOneshot();

		StageSettings.Instance.FinishLevel(true);
		SaveManager.ActiveGameData.AllowSkippingCutscene(DefeatCutsceneID);
		EmitSignal(SignalName.CutsceneFinished);
	}

	private readonly Vector3 VisualOffset = Vector3.Down * 5f;
	/// <summary> Snaps Alf's position to the correct position. </summary>
	private void SnapPosition()
	{
		GlobalPosition = PlayerPathFollower.GlobalPosition + PlayerPathFollower.Forward() * currentDistance + VisualOffset;
		GlobalRotation = Vector3.Zero;
		strikeParent.GlobalPosition = Vector3.Back * Player.PathFollower.GlobalPosition.Z;
	}

	private void ProcessAction()
	{
		if (CurrentFightState == FightState.Exploding)
			return;

		if (spiritBomb.IsTravelling || Player.IsSpiritBombActive) // Idle when spirit bomb is active
			return;

		if (CurrentFightState == FightState.Movement)
		{
			ProcessMovement();
			return;
		}

		if (CurrentFightState == FightState.Stunned)
		{
			ProcessStun();
			return;
		}

		if (CurrentFightState == FightState.AttackWindup)
		{
			ProcessAttackWindup();
			return;
		}

		if (CurrentFightState == FightState.AttackStrike)
			return;

		if (CurrentFightState != FightState.Idle || spiritBomb.IsTravelling)
			return;

		if (!ProcessActionTimer())
			return;

		StartNextAction();
	}

	private bool ProcessActionTimer()
	{
		actionTimer = Mathf.MoveToward(actionTimer, 0f, PhysicsManager.physicsDelta);
		return Mathf.IsZeroApprox(actionTimer);
	}

	private void GetNextAction()
	{
		currentActionCharacter = attackPatterns[currentPatternIndex][currentActionIndex];

		// Allow the player to interupt movement patterns with a bomb
		if (Player.Skills.IsSoulGaugeFilled &&
			(currentActionCharacter == 'F' || currentActionCharacter == 'C' || currentActionCharacter == 'M'))
		{
			currentActionCharacter = 'B';
		}
		else // Otherwise, increment the action index
		{
			currentActionIndex = (currentActionIndex + 1) % attackPatterns[currentPatternIndex].Length;
		}
	}

	/// <summary>
	/// Updates animations and sets them off.
	/// </summary>
	private void StartNextAction()
	{
		GetNextAction();

		if (StartMove()) // Started movement pattern
			return;

		StartAttackWindup();
	}

	private bool StartMove()
	{
		switch (currentActionCharacter)
		{
			case 'B':
				targetDistance = BombDistance;
				break;
			case 'F':
				targetDistance = FarDistance;
				break;
			case 'M':
				targetDistance = NormalDistance;
				break;
			case 'C':
				targetDistance = CloseDistance;
				break;
			default:
				return false;
		}

		initialDistance = currentDistance; // Store the current distance

		if (Mathf.IsEqualApprox(initialDistance, targetDistance)) // No movement needed-skip
			return false;

		// Start Animation
		bool isAdvancing = initialDistance > targetDistance;
		MoveStatePlayback.Travel(isAdvancing ? "advance-start" : "retreat-start");
		currentMovementCurve = isAdvancing ? advanceMovementCurve : retreatMovementCurve;
		CurrentFightState = FightState.Movement;
		movementSample = 0f;

		if (currentProgressionDialogIndex == 0)
			PlayProgressionDialog();

		return true;
	}

	private void ProcessMovement()
	{
		if (Mathf.IsEqualApprox(movementSample, currentMovementCurve.MaxDomain))
			return;

		movementSample = Mathf.MoveToward(movementSample, currentMovementCurve.MaxDomain, PhysicsManager.physicsDelta);
		float t = currentMovementCurve.Sample(movementSample);

		if (Mathf.IsEqualApprox(t, 1.0f))
			MoveStatePlayback.Travel("idle");

		currentDistance = Mathf.Lerp(initialDistance, targetDistance, t);
	}

	private void FinishMovement()
	{
		if (currentActionCharacter == 'B') // Start bomb attack
		{
			StartAttackWindup();
			actionTimer = 0.8f;
			if (currentProgressionDialogIndex == 2)
				PlayProgressionDialog();

			return;
		}

		actionTimer = 0.3f;
		CurrentFightState = FightState.Idle;
	}

	private void StartAttackWindup()
	{
		float slashSpeed = 1.2f;
		if (currentPatternIndex == 2)
			slashSpeed = 1.5f;

		// Update the delay for each attack as needed below
		switch (currentActionCharacter)
		{
			case '3':
				currentGravityOrbSide = -1; // Start on the left side
				if (currentProgressionDialogIndex == 6)
					PlayProgressionDialog();
				break;
			case '\\':
			case '>':
			case '|':
				actionTimer = 1f;
				animationTree.Set(SlashType, "right");
				animationTree.Set(SlashSpeed, slashSpeed);
				if (currentProgressionDialogIndex == 1)
					PlayProgressionDialog();
				break;
			case '/':
			case '<':
			case '_':
				actionTimer = 0.2f;
				animationTree.Set(SlashType, "left");
				animationTree.Set(SlashSpeed, slashSpeed);
				break;
			case 'X':
			case '#':
				actionTimer = 0.5f;
				animationTree.Set(SlashType, "middle");
				animationTree.Set(SlashSpeed, slashSpeed * 1.5f);
				break;
			default: // No windup
				actionTimer = 0f;
				break;
		}

		CurrentFightState = FightState.AttackWindup;
	}

	private void ProcessAttackWindup()
	{
		if (!ProcessActionTimer())
			return;

		switch (currentActionCharacter)
		{
			case '6':
				currentPurpleOrbIndex = 0;
				animationTree.Set(SixOrbTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);
				break;
			case '3':
				animationTree.Set(ThreeOrbTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);
				break;
			case '\\':
			case '/':
			case '>':
			case '<':
			case 'X':
			case '#':
			case '|':
			case '_':
				animationTree.Set(SlashTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);
				break;
			case 'B':
				spiritBomb.Respawn();
				animationTree.Set(SpiritBombTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Fire);
				break;
			default: // Unimplmented
				GD.Print($"Action {currentActionCharacter} is not implemented!");
				FinishAttack();
				return;
		}

		CurrentFightState = FightState.AttackStrike;
	}

	/// <summary> Activate the slashes. </summary>
	public void StartSlashAttack()
	{
		switch (currentActionCharacter)
		{
			case '\\':
				slashControllers[0].Activate();
				break;
			case '/':
				slashControllers[1].Activate();
				break;
			case '>':
				slashControllers[2].Activate();
				break;
			case '<':
				slashControllers[3].Activate();
				break;
			case 'X':
				slashControllers[4].Activate();
				break;
			case '#':
				slashControllers[5].Activate();
				break;
			case '|':
				slashControllers[6].Activate();
				break;
			case '_':
				slashControllers[7].Activate();
				break;
		}
	}

	private void OnPlayerKnockbackFinished()
	{
		if (currentActionCharacter != 'B' || CurrentFightState == FightState.Movement)
			return;

		FinishAttack();
		ResetPositions();
		currentDistance = CloseDistance;
		SnapPosition();
		ResetCamera();
		ShowObjects();
		PlaySpiritBombDamageDialog();
	}

	private void FinishAttack()
	{
		switch (currentActionCharacter)
		{
			case '6':
				actionTimer = 1f;
				break;
			case '3':
				actionTimer = 0.4f;
				break;
			case '<':
				actionTimer = 1f;
				break;
			default:
				actionTimer = 0.1f;
				break;
		}

		CurrentFightState = FightState.Idle;
	}

	/// <summary> Release the spirit bomb from Alf's hands and have it start flying. </summary>
	private void LaunchSpiritBomb() => spiritBomb.StartTravelling();

	private int currentGravityOrbIndex;
	private int currentGravityOrbSide;
	private readonly float GravityOrbSpacing = 1.5f;
	private readonly float GravitySpawnOffset = 8f;
	public void LaunchGravityOrb()
	{
		Vector3 orbPosition = new()
		{
			X = GravityOrbSpacing * -currentGravityOrbSide,
			Y = 0,
			Z = GlobalPosition.Z - GravitySpawnOffset
		};
		gravityOrbs[currentGravityOrbIndex].GlobalPosition = orbPosition;
		gravityOrbs[currentGravityOrbIndex].Activate();
		currentGravityOrbIndex = (currentGravityOrbIndex + 1) % gravityOrbs.Length;
		currentGravityOrbSide++;
	}

	public void AdvanceTripleOrb()
	{
		if (currentGravityOrbSide > 1) // Finished
		{
			currentGravityOrbSide = -1;
			FinishAttack();
			return;
		}

		// Give some space in between gravity orbs
		actionTimer = 0.2f;
		CurrentFightState = FightState.AttackWindup;
	}

	private int currentPurpleOrbIndex;
	public void AdvanceSixOrb()
	{
		purpleOrbs[currentPurpleOrbIndex].GlobalPosition = purpleOrbSpawnPoints[currentPurpleOrbIndex].GlobalPosition;
		purpleOrbs[currentPurpleOrbIndex].Activate();
		currentPurpleOrbIndex++;
	}

	private int currentExplosionParticleIndex;
	public void AdvanceExplosionParticle()
	{
		smokeParticles[currentExplosionParticleIndex].SetEmitting(false);
		smokeParticles[currentExplosionParticleIndex].Visible = true;
		explosionParticles[currentExplosionParticleIndex].RestartGroup();
		currentExplosionParticleIndex++;
		Player.Camera.StartCameraShake(new()
		{
			fadeIn = 0f,
			duration = 0.2f,
			fadeOut = 0.05f,
		});
	}

	public bool IsStunned => CurrentFightState == FightState.Stunned;
	private readonly string StunTransition = "parameters/stun-transition/transition_request";
	private readonly string StunPlaybackPath = "parameters/stun-state/playback";
	private readonly string StunDamageTrigger = "parameters/stun-damage-trigger/request";
	private readonly string StunDamageFinalTrigger = "parameters/stun-damage-final-trigger/request";
	private AnimationNodeStateMachinePlayback StunPlayback => (AnimationNodeStateMachinePlayback)animationTree.Get(StunPlaybackPath);
	/// <summary> Called when the spirit bomb explodes on Alf. </summary>
	private void StartStun()
	{
		CurrentFightState = FightState.Stunned;
		StunPlayback.Start("stun-start");
		animationTree.Set(StunTransition, "enabled");
		actionTimer = MaxStunLength;
		currentActionIndex = 0; // Reset to the start of the phase
	}

	private readonly float MaxStunLength = 10f;
	private void ProcessStun()
	{
		if (Player.IsMultiPunchActive) // Use timer in player state instead
		{
			if (currentProgressionDialogIndex == 4)
				PlayProgressionDialog();

			return;
		}

		actionTimer = Mathf.MoveToward(actionTimer, 0, PhysicsManager.physicsDelta);
		if (Mathf.IsZeroApprox(actionTimer))
			FinishStun(false);
	}

	public void FinishStun(bool isFromMultipunch)
	{
		if (isFromMultipunch)
		{
			stopLockout.Deactivate();
			ResetPositions();
			currentDistance = CloseDistance;
			SnapPosition();
			ResetCamera();
			ShowObjects();
		}
		else
		{
			currentDistance = Player.GlobalPosition.RemoveVertical().DistanceTo(GlobalPosition.RemoveVertical());
		}

		if (currentProgressionDialogIndex == 5 && currentHealth != MaxHealth)
			PlayProgressionDialog();

		actionTimer = 1f;
		StunPlayback.Travel("stun-stop");
		CurrentFightState = FightState.Idle;
		HeadsUpDisplay.Instance.SetVisibility(true);
	}

	public void StartStunCamera() => punchCameraTrigger.Activate();

	public void DefeatScreenShake()
	{
		Player.Camera.StartCameraShake(new()
		{
			fadeIn = 0f,
			duration = 0.3f,
			fadeOut = 0.05f,
			magnitude = Vector3.One
		});
	}

	public void StartDefeatScreenFlash()
	{
		TransitionManager.StartTransition(new TransitionData()
		{
			color = Colors.White,
			inSpeed = 0.1f,
			outSpeed = 0.5f,
		});
	}

	public void FinishDefeatScreenFlash() => TransitionManager.FinishTransition();

	public void MultiPunchScreenShake()
	{
		Player.Camera.StartCameraShake(new()
		{
			fadeIn = 0f,
			duration = 0.3f,
			fadeOut = 0.05f,
			magnitude = Vector3.One * 3f
		});
	}

	public void FinishMultiPunch()
	{
		// Always do the explosion loop
		Player.Visible = false;
		stopLockout.Activate();
		StunPlayback.Start("explosion");
		animationTree.Set(StunDamageFinalTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Abort);
		isRingExploding = false; // Reset flag
		CurrentFightState = FightState.Exploding;
	}

	public bool IsExploding => CurrentFightState == FightState.Exploding;

	public void CheckDefeat()
	{
		if (currentExplosionParticleIndex < explosionParticles.Length)
			return;

		Player.Visible = true;
		Player.Activate();
		DefeatBoss();
	}

	/// <summary> Tracks whether the rings are exploding or not. </summary>
	private bool isRingExploding;
	public void CheckRingExplosion(bool isFirstRing)
	{
		if (!isFirstRing && !isRingExploding)
			return;

		if (isFirstRing)
			isRingExploding = true;

		if (currentHealth > 0)
		{
			int countAmount = Mathf.FloorToInt(MaxHealth / (explosionParticles.Length - 1));
			if (lastExplosionHealth - currentHealth < countAmount) // No explosion
			{
				if (CurrentFightState == FightState.Exploding)
				{
					Player.Visible = true;
					Player.Activate();
					FinishStun(true);
				}

				return;
			}

			lastExplosionHealth -= countAmount;
		}

		// Start explosion
		if (currentExplosionParticleIndex < explosionCameras.Length)
			explosionCameras[currentExplosionParticleIndex].Activate();
		StunPlayback.Start("explosion-damage");
	}

	// Play a super cool animation
	public void StartFinalMultiPunch()
	{
		TransitionManager.StartTransition(new()
		{
			color = Colors.Black,
			inSpeed = 0f,
			outSpeed = 0.5f,
		});
		TransitionManager.FinishTransition();
		HeadsUpDisplay.Instance.SetVisibility(false);
		animationTree.Set(StunDamageFinalTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Fire);
		animationTree.Set(StunDamageTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Abort);
	}

	public void TakeDamage()
	{
		currentHealth--;
		if (currentHealth < 15 && currentPatternIndex == 0)
			currentPatternIndex = 1; // Phase 2
		else if (currentHealth < 5 && currentPatternIndex == 1)
			currentPatternIndex = 2; // Phase 3

		Player.Camera.StartCameraShake(new()
		{
			fadeIn = 0f,
			duration = 0.2f,
			fadeOut = 0.05f,
		});

		animationTree.Set(StunDamageTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Fire);
	}

	public void StartSpiritBombKick()
	{
		currentDistance = BombDistance;
		SnapPosition();
	}

	private void AttemptSpiritBombVoiceLine()
	{
		if (currentProgressionDialogIndex == 3)
			PlayProgressionDialog();
	}

	/// <summary> Reset the spirit bomb positions so we can see it hitting Alf. </summary>
	public void FinishSpiritBombKick()
	{
		currentDistance = BombDistance;
		SnapPosition();
		ResetCamera();
		spiritBomb.IsTargetingAlf = true;
		spiritBomb.GlobalPosition = Player.GlobalPosition.Lerp(GlobalPosition, 0.2f);
	}

	private void ResetPositions()
	{
		// Reset positions to prevent going out of bounds
		float offsetPosition = Player.GlobalPosition.Z;
		Player.GlobalPosition += Vector3.Forward * offsetPosition;
		itemParent.GlobalPosition += Vector3.Forward * offsetPosition;
	}

	/// <summary> Reset camera to the hallway camera. </summary>
	public void ResetCamera()
	{
		if (TransitionManager.IsTransitionActive)
			return;

		TransitionManager.StartTransition(new()
		{
			color = Colors.Black,
			inSpeed = 0f,
			outSpeed = 0.5f,
		});
		TransitionManager.FinishTransition();
		defaultCameraTrigger.Activate();
	}

	public void HideObjects() => itemParent.Visible = false;
	public void ShowObjects() => itemParent.Visible = true;
}
