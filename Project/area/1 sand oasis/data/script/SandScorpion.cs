using Godot;
using Godot.Collections;
using Project.Core;
using Project.CustomNodes;
using Project.Gameplay.Hazards;
using Project.Gameplay.Triggers;

namespace Project.Gameplay.Bosses;

/// <summary> Controls the first boss of the game, the Sand Scorpion. </summary>
/*
Behaviour:
Runs away from the player, unless the player is far away and backstepping, in which case Sand Scorpion will walk towards the player to maintain distance.
-Remains idle until the player moves-
When far away, shoot missiles. When really far, one of these missiles locks onto the player's current position, to force the player to keep moving.
When close, attack depending on which side the player is on. Attack Pattern: Two hit light attacks, heavy attack.
The player can skip the second tail attack by exiting close range then re-entering

ITEMS:
After hitting the trigger point (Excluding the first time), change the active itemset to the next lap. Lap 4 doesn't have any items.
*/
public partial class SandScorpion : Node3D
{
	/// <summary> Boss's path follower. </summary>
	[Export] private PathFollow3D bossPathFollower;
	[Export] private CameraTrigger cutsceneCamera;

	private enum AttackState
	{
		Inactive,
		Windup,
		Strike,
		Recovery,
	}
	/// <summary> Phase of the boss's current attack. </summary>
	[Export] private AttackState attackState;

	private enum FightState
	{
		Introduction,
		Waiting,
		Active,
		Defeated
	}
	/// <summary> Is the boss being processed? </summary>
	private FightState fightState;
	private readonly float StartingPosition = 60;
	private readonly int TraversalEyePearlAmount = 10;

	private PlayerController Player => StageSettings.Player;
	private PlayerPathController PathFollower => Player.PathFollower;

	[ExportGroup("Dialogs")]
	[Export] private Array<DialogTrigger> hitDialogs = [];
	[Export] private Array<DialogTrigger> hintDialogs = [];
	private int[] dialogFlags = [
		0, // Light attack hint flag
		0, // Heavy attack hint flags
		0 // Flying Eye flag
	];

	private bool PlayHint(int index)
	{
		if (!hintDialogs[index].Visible) return false; // Already played that hint

		hintDialogs[index].Activate();
		hintDialogs[index].Visible = false; // Use visibility as a flag to keep track of played hints
		return true;
	}

	[ExportGroup("Animation")]
	[Export] private AnimationTree rootAnimationTree;
	[Export] private AnimationTree lTailAnimationTree;
	[Export] private AnimationTree rTailAnimationTree;
	[Export] private AnimationTree flyingEyeAnimationTree;
	/// <summary> Extra animator that manages stuff like damage flashing, hitboxes, etc. </summary>
	[Export] private AnimationPlayer eventAnimator;

	private readonly string IntroCutsceneID = "so_boss_intro";
	private readonly string DefeatCutsceneID = "so_boss_defeat";

	private readonly string DisabledState = "disabled";
	private readonly string EnabledState = "enabled";
	private readonly string IntroParameter = "parameters/intro_trigger/request";
	private readonly string DamageParameter = "parameters/damage_trigger/request";
	private readonly string DefeatParameter = "parameters/defeat_trigger/request";
	private readonly string DefeatSeekParameter = "parameters/defeat_seek/seek_request";
	private readonly string PhaseTwoDamageParameter = "parameters/phase_two_damage_trigger/request";

	public override void _Ready()
	{
		rootAnimationTree.Active = lTailAnimationTree.Active = rTailAnimationTree.Active = flyingEyeAnimationTree.Active = true; // Activate animation trees

		SetUpMissiles();

		StageSettings.Instance.Unloaded += Unload;
		StageSettings.Instance.Respawned += Respawn;

		StageSettings.Instance.Connect(StageSettings.SignalName.LevelStarted, new(this, MethodName.StartIntroduction));
	}

	public void Respawn()
	{
		fightState = FightState.Waiting;
		currentHealth = MaxHealth;

		bossPathFollower.Progress = StartingPosition;
		GlobalPosition = Vector3.Forward * StartingPosition;
		GlobalRotation = Vector3.Up * Mathf.Pi;
		ResetPhysicsInterpolation();

		// Reset animations
		isPhaseTwoActive = false;
		lTailAnimationTree.Set(HeavyAttackParameter, DisabledState);
		rTailAnimationTree.Set(HeavyAttackParameter, DisabledState);
		rootAnimationTree.Set(EyeParameter, DisabledState);

		rootAnimationTree.Set(DamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		rootAnimationTree.Set(PhaseTwoDamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		lTailAnimationTree.Set(LightAttackParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		rTailAnimationTree.Set(LightAttackParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);

		flyingEyeAnimationTree.Set(EyeParameter, DisabledState);
		flyingEyeAnimationTree.Set(DamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);

		// Reset movement
		MoveSpeed = 0;
		moveSpeedVelocity = 0;
		movementBlend = 0;
		rootAnimationTree.Set(MovementBlendParameter, movementBlend);

		// Reset phase
		phaseTwoBlend = 0;
		phaseTwoBlendVelocity = 0;
		rootAnimationTree.Set(PhaseTwoParameter, phaseTwoBlend);
		lTailAnimationTree.Set(TailPhaseTwoTransitionParameter, DisabledState);
		rTailAnimationTree.Set(TailPhaseTwoTransitionParameter, DisabledState);
		rootAnimationTree.Set(HeavyAttackTriggerParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		impactEffect.Visible = false;
		flyingEyeHitbox.Monitorable = false;
		flyingEyeVFX.Emitting = false;

		eventAnimator.Play("RESET");

		// Reset attacks
		flyingEyeBlend = 0;
		attackTimer = 0;
		attackCounter = 0;
		attackState = AttackState.Inactive;

		// Reset flying eye
		flyingEyeRoot.Position = Vector3.Zero;
		flyingEyeRoot.Rotation = Vector3.Zero;

		RespawnMissiles();
	}

	public void Unload()
	{
		// Cleanup orphan nodes
		for (int i = 0; i < missilePool.Count; i++)
			missilePool[i].QueueFree();
	}

	private void StartIntroduction()
	{
		rootAnimationTree.Set(IntroParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		lTailAnimationTree.Set(IntroParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		rTailAnimationTree.Set(IntroParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		GlobalTransform = Transform3D.Identity;
		ResetPhysicsInterpolation();

		cutsceneCamera.Activate();
		Player.Deactivate();
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
	}

	private void StartBattle()
	{
		cutsceneCamera.Deactivate();
		rootAnimationTree.Set(IntroParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		lTailAnimationTree.Set(IntroParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		rTailAnimationTree.Set(IntroParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);

		Respawn();
		eventAnimator.Play("finish-intro");
		TransitionManager.FinishTransition();
		Player.Activate();
	}

	private void StartFinalBlow()
	{
		if (attackState == AttackState.Recovery)
			DefeatBoss();

		TransitionManager.StartTransition(new()
		{
			inSpeed = 0f,
			outSpeed = .5f,
			color = Colors.Black
		});
		TransitionManager.FinishTransition();

		eventAnimator.Play("defeat");
		eventAnimator.Advance(0.0);

		Player.Skills.DisableBreakSkills();
		Player.MoveSpeed = 0;
		Player.StrafeSpeed = 0;

		Player.Visible = false;
		Player.AddLockoutData(Runtime.Instance.DefaultCompletionLockout);
		Interface.PauseMenu.AllowInputs = false;

		// Award 1000 points for defeating the boss
		BonusManager.instance.QueueBonus(new(BonusType.Boss, 1000));
	}

	private void DefeatBoss()
	{
		cutsceneCamera.Activate();
		rootAnimationTree.Set(DefeatParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		lTailAnimationTree.Set(DefeatParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		rTailAnimationTree.Set(DefeatParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		flyingEyeAnimationTree.Set(DefeatParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);

		fightState = FightState.Defeated;
		Player.Skills.CancelBreakSkills();
		Player.Deactivate();
	}

	private void FinishDefeat()
	{
		cutsceneCamera.Deactivate();
		rootAnimationTree.Active = rTailAnimationTree.Active = lTailAnimationTree.Active = flyingEyeAnimationTree.Active = false;
		eventAnimator.Play("finish-defeat");

		Player.Activate();
		StageSettings.Instance.FinishLevel(true);
		SaveManager.ActiveGameData.AllowSkippingCutscene(DefeatCutsceneID);
	}

	public override void _PhysicsProcess(double _)
	{
		switch (fightState)
		{
			case FightState.Introduction:
				if ((Input.IsActionJustPressed("sys_pause") || Input.IsActionJustPressed("button_jump")) &&
					SaveManager.ActiveGameData.CanSkipCutscene(IntroCutsceneID))
				{
					FinishIntroduction();
				}
				break;
			case FightState.Waiting:
				UpdateEyes();
				// Wait for the player to do something
				if (!Mathf.IsZeroApprox(Player.MoveSpeed))
					fightState = FightState.Active;
				break;
			case FightState.Active:
				// Update Boss
				UpdateEyes();
				UpdatePhase();
				UpdatePosition();
				UpdateMissiles();
				UpdateAttacks();
				UpdateHitboxes();
				break;
			case FightState.Defeated:
				if ((Input.IsActionJustPressed("sys_pause") || Input.IsActionJustPressed("button_jump")) &&
					SaveManager.ActiveGameData.CanSkipCutscene(DefeatCutsceneID))
				{
					eventAnimator.Play("finish-defeat");
					rootAnimationTree.Set(DefeatSeekParameter, 10);
					rTailAnimationTree.Set(DefeatSeekParameter, 10);
					lTailAnimationTree.Set(DefeatSeekParameter, 10);
				}
				break;
		}
	}

	/// <summary> Fastest possible movement speed, based on the player's top speed. </summary>
	private float CharacterTopSpeed => Player.Stats.GroundSettings.Speed;

	private float MoveSpeed { get; set; }
	private float moveSpeedVelocity;

	private float movementBlend;
	private readonly string MoveSpeedParameter = "parameters/movespeed/scale";
	private readonly string MovementBlendParameter = "parameters/movement_blend/blend_amount";

	/// <summary> Current distance to the player. </summary>
	private float currentDistance;
	/// <summary> Ideal distance when attacking. </summary>
	private const float StrikeDistance = 8.0f;
	/// <summary> Always try to keep at least this much distance between the player. (Unless boss is attacking) </summary>
	private const float ChaseDistance = 25.0f;
	/// <summary> Distance to start using close range attacks. </summary>
	private const float AttackDistance = 30.0f;
	/// <summary> Distance to start running away from the player. </summary>
	private const float RetreatDistance = 55.0f;
	/// <summary> Distance to start advancing towards the player. </summary>
	private const float AdvanceDistance = 65.0f;
	/// <summary> Distance to teleport. </summary>
	private const float TeleportDistance = 300.0f;

	/// <summary> Traction amount to use when attacking. </summary>
	private const float StrikeTraction = 20.0f;
	private const float Traction = .2f;
	private const float Friction = .8f;
	private const float HitstunFriction = .4f;

	private float CalculateDistance() // Calculate the distance between the player and the boss based on their respective pathfollowers.
	{
		float bossProgress = bossPathFollower.Progress + (MoveSpeed * PhysicsManager.physicsDelta);
		float playerProgress = PathFollower.Progress + (Player.MoveSpeed * PhysicsManager.physicsDelta);
		if (bossProgress < playerProgress)
			bossProgress += PathFollower.ActivePath.Curve.GetBakedLength();

		return bossProgress - playerProgress;
	}

	private void UpdatePosition()
	{
		if (currentHealth == 0)
		{
			MoveSpeed = ExtensionMethods.SmoothDamp(MoveSpeed, 0, ref moveSpeedVelocity, HitstunFriction);
		}
		else if (damageState != DamageState.None && !isPhaseTwoActive) // Knockback/hitstun
		{
			MoveSpeed = ExtensionMethods.SmoothDamp(MoveSpeed, 0, ref moveSpeedVelocity, HitstunFriction); // Slow down

			if (damageState == DamageState.Knockback)
			{
				if (MoveSpeed < 5.0f) // Because transitioning from speed 0 feels laggy
				{
					if (currentHealth <= 3) // Check for second phase
					{
						isPhaseTwoActive = true;
						hitDialogs[1].Activate();
					}

					damageState = DamageState.None;
				}
			}
			else if (Player.IsOnGround) // Player canceled their assault, resume movement
			{
				FinishHeavyAttack(true);
				damageState = DamageState.None;
			}
		}
		else
		{
			currentDistance = CalculateDistance();

			if (currentDistance >= RetreatDistance && currentDistance <= AdvanceDistance) // Waiting for the player
			{
				MoveSpeed = ExtensionMethods.SmoothDamp(MoveSpeed, 0, ref moveSpeedVelocity, Friction);
			}
			else if (attackState == AttackState.Strike && currentDistance < AttackDistance && !isPhaseTwoActive) // Attempt to match distance for more consistant attacks
			{
				float delta = currentDistance - StrikeDistance;
				MoveSpeed = ExtensionMethods.SmoothDamp(MoveSpeed, Mathf.Clamp(MoveSpeed - delta, 0f, CharacterTopSpeed), ref moveSpeedVelocity, StrikeTraction * PhysicsManager.physicsDelta);
			}
			else
			{
				float speedFactor;
				if (currentDistance < RetreatDistance)
				{
					speedFactor = 1f - ((currentDistance - ChaseDistance) / (RetreatDistance - ChaseDistance));
				}
				else // Move towards player
				{
					speedFactor = -Mathf.Clamp((currentDistance - AdvanceDistance) * .1f, 0f, 1f);

					if (currentDistance > TeleportDistance
						&& PathFollower.ActivePath.Curve.GetBakedLength() - currentDistance > 10) // Teleport when really far away
					{
						bossPathFollower.Progress -= (currentDistance - TeleportDistance) * PhysicsManager.physicsDelta;
					}
				}

				MoveSpeed = ExtensionMethods.SmoothDamp(MoveSpeed, CharacterTopSpeed * speedFactor, ref moveSpeedVelocity, Traction);
			}
		}

		bossPathFollower.Progress += MoveSpeed * PhysicsManager.physicsDelta;

		float speedRatio = .5f + (Mathf.Abs(MoveSpeed / CharacterTopSpeed) * 1.2f);
		if (damageState == DamageState.Knockback)
			speedRatio = 0f;

		rootAnimationTree.Set(MoveSpeedParameter, speedRatio);

		float direction = Mathf.Sign(MoveSpeed);
		if (Mathf.Abs(MoveSpeed) <= 2f)
			direction = 0;
		else if (isPhaseTwoActive)
			direction *= -1;

		movementBlend = Mathf.MoveToward(movementBlend, direction, 4f * PhysicsManager.physicsDelta);
		rootAnimationTree.Set(MovementBlendParameter, movementBlend);

		GlobalPosition = bossPathFollower.GlobalPosition;
	}

	/// <summary> Math.PI during the first phase, 0 during the phase two. </summary>
	private float phaseRotation;
	/// <summary> Used to smoothdamp phaseRotation. </summary>
	private float phaseRotationVelocity;
	/// <summary> How much smoothing to apply when the phase changes. </summary>
	private readonly float PhaseRotationSmoothing = 30.0f;

	/// <summary> Has phase two started? </summary>
	private bool isPhaseTwoActive;
	private float phaseTwoBlend;
	private float phaseTwoBlendVelocity;
	private readonly float PhaseTwoBlendSmoothing = 30.0f;
	private readonly string PhaseTwoParameter = "parameters/phase_two_add/add_amount";
	private readonly string TailPhaseTwoTransitionParameter = "parameters/phase_two/transition_request";

	private void UpdatePhase()
	{
		if (damageState == DamageState.None)
		{
			if (isPhaseTwoActive)
				phaseRotation = ExtensionMethods.SmoothDampAngle(phaseRotation, 0, ref phaseRotationVelocity, PhaseRotationSmoothing * PhysicsManager.physicsDelta);
			else
				phaseRotation = Mathf.Pi;
		}

		float facingAngle = bossPathFollower.Back().Flatten().AngleTo(Vector2.Down) - phaseRotation;
		GlobalRotation = Vector3.Up * facingAngle;

		if (isPhaseTwoActive)
		{
			phaseTwoBlend = ExtensionMethods.SmoothDamp(phaseTwoBlend, 1.0f, ref phaseTwoBlendVelocity, PhaseTwoBlendSmoothing * PhysicsManager.physicsDelta);
			rootAnimationTree.Set(PhaseTwoParameter, phaseTwoBlend);
			lTailAnimationTree.Set(TailPhaseTwoTransitionParameter, EnabledState);
			rTailAnimationTree.Set(TailPhaseTwoTransitionParameter, EnabledState);
		}
	}

	#region Attacks
	[ExportGroup("Missiles")]
	[Export]
	private Array<NodePath> missilePositionPaths;
	private Node3D[] missilePositions; // Where to fire missiles from
	[Export] private PackedScene missileScene;
	private readonly Array<BombMissile> missilePool = []; // Pool of missiles
	private readonly int MaxMissleCount = 5; // Same as the original game, only 5 missiles can be fired at a time

	private void SetUpMissiles()
	{
		missilePositions = new Node3D[missilePositionPaths.Count];
		for (int i = 0; i < missilePositionPaths.Count; i++)
			missilePositions[i] = GetNode<Node3D>(missilePositionPaths[i]);

		for (int i = 0; i < MaxMissleCount; i++) // Pool missiles
			missilePool.Add(missileScene.Instantiate<BombMissile>());
	}

	private void RespawnMissiles()
	{
		for (int i = 0; i < MaxMissleCount; i++)
		{
			if (missilePool[missileIndex].IsInsideTree()) // Remove all missiles from the scene tree
				missilePool[missileIndex].GetParent().RemoveChild(missilePool[missileIndex]);
		}

		missileTimer = MissleDelay;
	}

	private bool missileGroupReset = true;
	private int missileIndex;
	private float missileTimer;
	/// <summary> How long between each individual missile shots. </summary>
	private const float MissleInterval = .1f;
	/// <summary> Interval length between missile groups. </summary>
	private const float MissleGroupInterval = 2.5f;
	/// <summary> Starting delay so missiles don't fire immediately. </summary>
	private const float MissleDelay = 1.5f;

	private void UpdateMissiles()
	{
		if (missileGroupReset && currentDistance < AttackDistance) // Too close for missiles
			return;

		if (currentDistance > TeleportDistance) // Too far for missiles
			return;

		missileTimer = Mathf.MoveToward(missileTimer, 0, PhysicsManager.physicsDelta);

		// Spawn a Missile
		if (missileTimer <= 0)
		{
			SpawnMissile();

			// Wait for the next missile group?
			missileGroupReset = missileIndex >= MaxMissleCount;
			missileTimer = missileGroupReset ? MissleGroupInterval : MissleInterval;
			if (missileGroupReset) // Loop missile index
				missileIndex = 0;
		}
	}

	/// <summary>
	/// Spawns a missle.
	/// </summary>
	private void SpawnMissile()
	{
		if (!missilePool[missileIndex].IsInsideTree()) // Add missile to the tree if it isn't already added
			GetTree().Root.AddChild(missilePool[missileIndex]);

		int spawnFrom = Runtime.randomNumberGenerator.RandiRange(0, 2); // Figure out which position to spawn from
		bool allowSpread = missileIndex != 0 && missileIndex < MaxMissleCount - 1;
		float tracking = Runtime.randomNumberGenerator.RandfRange(1f, 2f);

		Vector3 targetPosition = missilePool[missileIndex].GetTargetPosition(allowSpread, tracking);
		targetPosition.Y = 0; // Make sure missiles end up on the floor

		Vector3 spawnPosition = missilePositions[spawnFrom].GlobalPosition; // Move missile to the spawn position
		missilePool[missileIndex].Launch(LaunchSettings.Create(spawnPosition, targetPosition, 5)); // Recalculate trajectory

		missileIndex++;
	}

	/// <summary> Which side is the boss attacking? -1 for left, 1 for right. </summary>
	private int attackSide;
	/// <summary> How many attacks has the boss done since giving an opening? </summary>
	private int attackCounter;
	/// <summary> Timer for attacks. </summary>
	private float attackTimer;
	/// <summary> How long to wait between phase one attacks. </summary>
	private readonly float PhaseOneAttackInterval = .8f;
	/// <summary> How long to wait between phase two attacks </summary>
	private readonly float PhaseTwoAttackInterval = 1.4f;

	/// <summary> Target position of the flying eye when sent out. </summary>
	private Vector3 flyingEyeTarget;
	/// <summary> 0 - 1 value blend value. </summary>
	private float flyingEyeBlend;
	private readonly float FlyingEyeNormalSpeed = 0.6f; // How fast does the eye typically move?
	private readonly float FlyingEyeLockonSpeed = 0.2f; // How fast does the eye move when targeted?
	private readonly float FlyingEyeKnockback = 1.2f; // How quickly to knock the eye back on the final hit

	private void UpdateAttacks()
	{
		if (damageState != DamageState.None) return; // Boss is too busy getting owned

		if (attackState != AttackState.Inactive) // Process the current attack
		{
			if (isPhaseTwoActive) // Eye attack
				UpdateEyeAttack();
			else if (!IsHeavyAttackActive) // Light Attack
				UpdateLightAttack();

			return;
		}

		if (currentDistance > AttackDistance || missileIndex != 0) return; // Out of range, or shooting missiles

		attackTimer -= PhysicsManager.physicsDelta;
		if (attackTimer < 0)
		{
			attackTimer = PhaseOneAttackInterval;
			if (isPhaseTwoActive) // Send eye out
			{
				attackTimer = PhaseTwoAttackInterval;
				StartEyeAttack();
			}
			else if (attackCounter <= 1)
			{
				StartLightAttack();
			}
			else
			{
				StartHeavyAttack();
			}
		}
	}

	private float lightAttackTrackingVelocity;
	private readonly string LightAttackParameter = "parameters/light_attack_trigger/request";
	private readonly string LightAttackPositionParameter = "parameters/light_attack_blend/blend_amount";
	private void StartLightAttack()
	{
		// Play hint
		if (dialogFlags[0] == 0)
		{
			dialogFlags[0]++;
		}
		else if (dialogFlags[0] == 1)
		{
			PlayHint(0);
			dialogFlags[0] = 1;
		}

		lightAttackTrackingVelocity = 0;
		attackCounter++;
		if (PathFollower.LocalPlayerPositionDelta.X < 0) // Left Attack
		{
			attackSide = -1;
			eventAnimator.Play("l-light-attack");
			lTailAnimationTree.Set(LightAttackParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		}
		else
		{
			attackSide = 1;
			eventAnimator.Play("r-light-attack");
			rTailAnimationTree.Set(LightAttackParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		}
	}

	private void UpdateLightAttack()
	{
		if (attackState != AttackState.Strike || attackSide == 0) return;

		// Track the player's position
		float current = (float)lTailAnimationTree.Get(LightAttackPositionParameter);
		float pos = PathFollower.LocalPlayerPositionDelta.X;
		if ((attackSide == -1 && pos > 0) || (attackSide == 1 && pos < 0))
			pos = 0;

		pos = (2 * -Mathf.Abs(pos / 4)) + 1;
		current = ExtensionMethods.SmoothDamp(current, pos, ref lightAttackTrackingVelocity, .2f);

		lTailAnimationTree.Set(LightAttackPositionParameter, current);
		rTailAnimationTree.Set(LightAttackPositionParameter, current);
	}

	private bool IsHeavyAttackActive => attackState != AttackState.Inactive && attackCounter == 0;

	private readonly string HeavyStrikeState = "strike";
	private readonly string HeavyLoopState = "loop";
	private readonly string HeavyRecoveryState = "recovery";
	private readonly string HeavyAttackParameter = "parameters/heavy_attack_transition/transition_request";
	private readonly string HeavyAttackTriggerParameter = "parameters/heavy_attack_trigger/request";

	private void StartHeavyAttack()
	{
		attackCounter = 0;
		if (PathFollower.LocalPlayerPositionDelta.X < 0) // Left Attack
		{
			attackSide = -1;
			eventAnimator.Play("l-heavy-attack");
			lTailAnimationTree.Set(HeavyAttackParameter, HeavyStrikeState);
		}
		else
		{
			attackSide = 1;
			eventAnimator.Play("r-heavy-attack");
			rTailAnimationTree.Set(HeavyAttackParameter, HeavyStrikeState);
		}

		rootAnimationTree.Set(HeavyAttackTriggerParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
	}

	public void FinishHeavyAttack(bool forced = default)
	{
		if (!forced && (damageState == DamageState.Hitstun || Player.IsHomingAttacking)) return;

		// Player missed their chance, update hints.
		if (dialogFlags[1] < 2)
		{
			if (dialogFlags[1] == 0)
				PlayHint(1);
			else if (dialogFlags[1] == 1)
				PlayHint(2);

			dialogFlags[1]++;
		}

		attackState = AttackState.Inactive;

		// Disables all hurtboxes
		eventAnimator.Play("disable-hurtbox-03");

		if (attackSide == 1)
			rTailAnimationTree.Set(HeavyAttackParameter, HeavyRecoveryState);
		else if (attackSide == -1)
			lTailAnimationTree.Set(HeavyAttackParameter, HeavyRecoveryState);
	}

	private void PlayScreenShake(float magnitude)
	{
		StageSettings.Player.Camera.StartCameraShake(new()
		{
			magnitude = Vector3.One.RemoveDepth() * magnitude,
			intensity = Vector3.One * 50.0f,
		});
	}

	[ExportGroup("Effects")]
	[Export]
	private GroupGpuParticles3D impactEffect;
	private void StartImpactFX(NodePath n)
	{
		PlayScreenShake(1f);
		impactEffect.Visible = true;
		Vector3 p = GetNode<Node3D>(n).GlobalPosition;
		p.Y = 0; // Snap to the floor
		impactEffect.GlobalPosition = p;
		impactEffect.ResetPhysicsInterpolation();
		impactEffect.RestartGroup();
	}

	[Export]
	private GroupGpuParticles3D hitEffect;
	private void StartHitFX()
	{
		hitEffect.Visible = true;
		hitEffect.GlobalPosition = Player.CenterPosition;
		hitEffect.ResetPhysicsInterpolation();
		hitEffect.RestartGroup();
	}

	[Export]
	private GpuParticles3D flyingEyeVFX;
	#endregion

	#region Eyes
	[ExportGroup("Eyes")]
	[Export]
	private Array<Node3D> mainEyes;
	[Export]
	private Array<Node3D> tailEyes;
	/// <summary> Eyes that track the player. </summary>
	private float eyeTrackingFactor;
	private float eyeTrackingVelocity;
	private const float EyeTrackingSmoothing = 30.0f;

	/// <summary>
	/// Updates the eyes to look at the player's position.
	/// </summary>
	private void UpdateEyes()
	{
		// Main eyes always look at the player
		for (int i = 0; i < mainEyes.Count; i++)
		{
			if (currentHealth == 0)
			{
				mainEyes[i].Rotation = Vector3.Zero;
				continue;
			}

			if ((mainEyes[i].GlobalPosition - Player.GlobalPosition).LengthSquared() < 1f) // Failsafe
				continue;

			mainEyes[i].LookAt(Player.GlobalPosition, Vector3.Up);
		}

		float targetTracking;
		if (isPhaseTwoActive)
			targetTracking = phaseTwoBlend;
		else
			targetTracking = attackState == AttackState.Inactive ? 1f : 0f;
		eyeTrackingFactor = ExtensionMethods.SmoothDamp(eyeTrackingFactor, targetTracking, ref eyeTrackingVelocity, EyeTrackingSmoothing * PhysicsManager.physicsDelta);

		// Update tail eyes
		for (int i = 0; i < tailEyes.Count; i++)
		{
			if (currentHealth == 0)
			{
				tailEyes[i].Rotation = Vector3.Zero;
				continue;
			}

			if ((tailEyes[i].GlobalPosition - Player.GlobalPosition).LengthSquared() < 1f) // Failsafe
				continue;

			tailEyes[i].LookAt(Player.GlobalPosition, Vector3.Up);
			tailEyes[i].Basis = tailEyes[i].Basis.Slerp(Basis.Identity, 1f - eyeTrackingFactor).Orthonormalized();
		}
	}

	[Export]
	/// <summary> Hurtbox of the flying eye. </summary>
	private Area3D flyingEyeHitbox;
	[Export]
	/// <summary> Flying eye, only accessible during phase two. </summary>
	private Node3D flyingEyeRoot;
	[Export]
	/// <summary> Position in the body. </summary>
	private Node3D flyingEyeBone;
	/// <summary> Target position of the flying eye attack, based on the pathfollower. </summary>
	private Vector2 flyingEyeAttackPosition;
	/// <summary> Maximum amount the flying eye can track the player. Lower values make the attack easier to avoid. </summary>
	private const float FlyingEyeMaxTracking = 2.5f;
	/// <summary> Used to prevent the flying eye clipping into the ground. </summary>
	private const float FlyingEyeRadius = 2f;
	/// <summary> The eye will start retreating if when the player is moving backwards. </summary>
	private const float FlyingEyeRetreatDistanceSquared = 12f;
	private void UpdateFlyingEyeTarget()
	{
		// Calculate
		float horizontalTracking = flyingEyeAttackPosition.X - PathFollower.LocalPlayerPositionDelta.X;
		horizontalTracking = Mathf.Clamp(horizontalTracking, -FlyingEyeMaxTracking, FlyingEyeMaxTracking);
		flyingEyeTarget = Player.PathFollower.GlobalPosition + (Vector3.Up * flyingEyeAttackPosition.Y);
		flyingEyeTarget += Player.PathFollower.Right() * horizontalTracking;
		flyingEyeTarget += Player.PathFollower.ForwardAxis * FlyingEyeRadius;
	}

	private readonly string EyeBiteState = "bite";
	private readonly string EyeRetreatState = "retreat";
	private readonly string EyeParameter = "parameters/eye_transition/transition_request";
	private void StartEyeAttack()
	{
		attackState = AttackState.Strike;

		// Cache current player position delta
		flyingEyeAttackPosition = new Vector2(PathFollower.LocalPlayerPositionDelta.X, FlyingEyeRadius);
		eventAnimator.Play("cage");
		flyingEyeVFX.Emitting = true;
		rootAnimationTree.Set(EyeParameter, EnabledState); // Open eye cage
		flyingEyeAnimationTree.Set(EyeParameter, EyeBiteState); // Start biting
	}

	private void UpdateEyeAttack()
	{
		if (attackState == AttackState.Strike)
		{
			UpdateFlyingEyeTarget();

			flyingEyeBlend = Mathf.MoveToward(flyingEyeBlend, 1f, FlyingEyeNormalSpeed * PhysicsManager.physicsDelta);
			if (Mathf.IsEqualApprox(flyingEyeBlend, 1f))
				RetreatEyeAttack();

			if (Player.IsBackflipping &&
				flyingEyeRoot.GlobalPosition.DistanceSquaredTo(Player.CenterPosition) < FlyingEyeRetreatDistanceSquared)
			{
				RetreatEyeAttack();
			}
		}
		else
		{
			if (currentHealth == 0)
				flyingEyeBlend = Mathf.MoveToward(flyingEyeBlend, 0f, FlyingEyeKnockback * PhysicsManager.physicsDelta);
			else if (Player.IsHomingAttacking || Player.Skills.IsSpeedBreakActive)
				flyingEyeBlend = Mathf.MoveToward(flyingEyeBlend, 0f, FlyingEyeLockonSpeed * PhysicsManager.physicsDelta);
			else
				flyingEyeBlend = Mathf.MoveToward(flyingEyeBlend, 0f, FlyingEyeNormalSpeed * PhysicsManager.physicsDelta);

			if (Mathf.IsZeroApprox(flyingEyeBlend))
				FinishEyeAttack();
		}

		float t = Mathf.SmoothStep(0, 1, flyingEyeBlend);
		flyingEyeRoot.GlobalPosition = flyingEyeBone.GlobalPosition.Lerp(flyingEyeTarget, t);

		// Update rotation
		Vector2 delta = (flyingEyeTarget - flyingEyeBone.GlobalPosition).Flatten();
		flyingEyeRoot.GlobalRotation = Vector3.Up * Mathf.LerpAngle(flyingEyeBone.GlobalRotation.Y, delta.AngleTo(Vector2.Down), t);
	}

	private void RetreatEyeAttack()
	{
		attackState = AttackState.Recovery;
		flyingEyeAnimationTree.Set(EyeParameter, EyeRetreatState);
		flyingEyeHitbox.Monitorable = true;
	}

	private void FinishEyeAttack()
	{
		if (currentHealth <= 0)
		{
			DefeatBoss();
		}
		else if (dialogFlags[2] == 0 && currentHealth == 1)
		{
			hitDialogs[3].Activate();
			dialogFlags[2] = 1;
		}

		eventAnimator.Play("cage");
		flyingEyeVFX.Emitting = false;
		flyingEyeAnimationTree.Set(EyeParameter, DisabledState); // Reset
		rootAnimationTree.Set(EyeParameter, DisabledState); // Close eye cage

		flyingEyeHitbox.Monitorable = false;
		attackState = AttackState.Inactive;
	}
	#endregion

	#region Hitboxes
	private int currentHealth;
	private readonly int MaxHealth = 5;

	private enum DamageState
	{
		None, // Not taking any damage
		Hitstun, // Player is bouncing on the tail
		Knockback // Sliding backwards
	}
	private DamageState damageState;
	/// <summary> How much force to knockback the boss back with when taking damage. </summary>
	private readonly float KnockbackStrength = 80.0f;

	/// <summary>
	/// Deals damage to the boss. Returns True if the boss is defeated.
	/// </summary>
	private void TakeDamage(int amount = 1)
	{
		currentHealth = (int)Mathf.MoveToward(currentHealth, 0, amount);
		eventAnimator.Play("damage");
		eventAnimator.Advance(0.0);

		if (currentHealth == 4)
			hitDialogs[0].Activate();
		else if (isPhaseTwoActive && currentHealth == 2)
			hitDialogs[2].Activate();
		else if (currentHealth == 0)
			StartFinalBlow();
	}

	private void UpdateHitboxes()
	{
		if (isCollidingWithBackEye)
		{
			ProcessBackEyeCollision();
			return;
		}

		if (isCollidingWithFlyingEye)
		{
			ProcessFlyingEyeCollision();
			return;
		}

		if (isInteractingWithTraversalHitbox)
		{
			ProcessTraversalCollision();
			return;
		}

		if (IsCollidingWithBoss)
			ProcessHitboxCollision();
	}

	/// <summary> Keeps track of how many of the boss's hitboxes the player is colliding with. </summary>
	private int bossHitboxCounter;
	private bool IsCollidingWithBoss => bossHitboxCounter != 0;
	/// <summary>
	/// Called when the player enters one of the boss's hitboxes.
	/// </summary>
	public void OnHitboxEntered(Area3D a)
	{
		if (!a.IsInGroup("player detection")) return;
		bossHitboxCounter++;
	}

	public void OnHitboxExited(Area3D a)
	{
		if (!a.IsInGroup("player detection")) return;
		bossHitboxCounter--;
	}

	public void ProcessHitboxCollision()
	{
		if (currentHealth <= 0) return;
		if (Player.IsHomingAttacking || Player.IsBouncing || Player.IsKnockback) return; // Player's homing attack always takes priority
		if (damageState == DamageState.Knockback) return; // Boss is in knockback and can't damage the player

		if (!Player.Skills.IsSpeedBreakActive)
		{
			Player.StartKnockback();
			return;
		}

		StartClash();
		StartKnockback();
	}


	private void StartClash()
	{
		Player.Skills.ToggleSpeedBreak();
		Player.StartKnockback(new()
		{
			disableDamage = true,
			stayOnGround = true,
			ignoreInvincibility = true,
			disableInvincibility = true,
			knockbackType = KnockbackSettings.KnockbackAnimation.Block,
		});

		PlayScreenShake(1f);
	}

	/// <summary> Is the player currently colliding with the flying eye? </summary>
	private bool isCollidingWithFlyingEye;
	/// <summary>
	/// Called when the player enters the flying eye.
	/// </summary>
	public void OnFlyingEyeEntered(Area3D a)
	{
		if (!a.IsInGroup("player")) return;
		isCollidingWithFlyingEye = true;
	}

	/// <summary>
	/// Called when the player exits the flying eye.
	/// </summary>
	public void OnFlyingEyeExited(Area3D a)
	{
		if (!a.IsInGroup("player")) return;
		isCollidingWithFlyingEye = false;
	}

	private void ProcessFlyingEyeCollision()
	{
		if (Player.IsBouncing) return; // Player just finished a homing attack

		if (Player.Skills.IsSpeedBreakActive) // Special attack
		{
			if (attackState == AttackState.Strike)
			{
				flyingEyeAnimationTree.Set(DamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
				rootAnimationTree.Set(PhaseTwoDamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
				StartHitFX();
				RetreatEyeAttack();
				TakeDamage(2);
			}
			else
			{
				StartClash();
			}

			return;
		}

		if (Player.AttackState != PlayerController.AttackStates.None)
		{
			flyingEyeAnimationTree.Set(DamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			rootAnimationTree.Set(PhaseTwoDamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
			StartHitFX();
			if (Player.AttackState == PlayerController.AttackStates.Weak)
				TakeDamage(1);
			else
				TakeDamage(2);

			Player.StartBounce(false);
			return;
		}

		if (Player.IsJumpDashOrHomingAttack ||
			Player.IsAccelerationJumping)
		{
			// Player countered the attack
			RetreatEyeAttack();
			Player.StartBounce(false);
			return;
		}

		Player.StartKnockback();
	}

	/// <summary> Is the player currently colliding with the eye on the boss's back? </summary>
	private bool isCollidingWithBackEye;
	/// <summary>
	/// Called when the player enters with the eye on the boss's back.
	/// </summary>
	public void OnBackEyeEntered(Area3D area)
	{
		if (!area.IsInGroup("player")) return;
		isCollidingWithBackEye = true;
	}

	/// <summary>
	/// Called when the player leavesthe eye on the boss's back.
	/// </summary>
	public void OnBackEyeExited(Area3D area)
	{
		if (!area.IsInGroup("player")) return;
		isCollidingWithBackEye = false;
	}

	public void ProcessBackEyeCollision()
	{
		if (Player.AttackState == PlayerController.AttackStates.None) return; // Player isn't attacking

		if (IsHeavyAttackActive) // End active heavy attack
		{
			// Player can see what's happening; skip obvious hint
			if (dialogFlags[1] < 2)
				dialogFlags[1] = 1;

			FinishHeavyAttack(true);
		}

		StartHitFX();
		TakeDamage();

		if (Player.IsHomingAttacking)
			Player.StartBounce(); // Bounce the player

		StartKnockback();
	}

	private void StartKnockback()
	{
		MoveSpeed = KnockbackStrength; // Start knockback
		damageState = DamageState.Knockback;

		lTailAnimationTree.Set(LightAttackParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
		rTailAnimationTree.Set(LightAttackParameter, (int)AnimationNodeOneShot.OneShotRequest.Abort);
	}

	private bool isHittingFarEye;
	private bool isInteractingWithTraversalHitbox;

	/// <summary>
	/// Called when the player hits one of the eyes on the tail. No damage is actually dealt.
	/// </summary>
	public void OnTraversalHurtboxEntered(Area3D a, bool hitFarEye)
	{
		if (!a.IsInGroup("player")) return;

		isHittingFarEye = hitFarEye;
		isInteractingWithTraversalHitbox = true;
		ProcessTraversalCollision();
	}

	public void OnTraversalHurtboxExited(Area3D a)
	{
		if (!a.IsInGroup("player")) return;
		isInteractingWithTraversalHitbox = false;
	}

	private void ProcessTraversalCollision()
	{
		if (!Player.IsHomingAttacking) return; // Player isn't attacking

		StartHitFX();
		rootAnimationTree.Set(DamageParameter, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		Player.StartBounce();
		damageState = DamageState.Hitstun;
		Runtime.Instance.SpawnPearls(TraversalEyePearlAmount, Player.GlobalPosition, new Vector2(2, 1.5f));
		attackCounter = 0; // Reset attack counter whenever a tail eye is hit

		if (attackSide == 1)
			rTailAnimationTree.Set(HeavyAttackParameter, HeavyLoopState);
		else if (attackSide == -1)
			lTailAnimationTree.Set(HeavyAttackParameter, HeavyLoopState);

		// Disable hurtboxes so the player can't just bounce on the same eye infinitely
		string hitboxAnimation = isHittingFarEye ? "disable-hurtbox-01" : "disable-hurtbox-02";
		hitboxAnimation = (attackSide == 1 ? "r-" : "l-") + hitboxAnimation;
		eventAnimator.CallDeferred("play", hitboxAnimation, -1, -1, false);
	}
	#endregion
}
