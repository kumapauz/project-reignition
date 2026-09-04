using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class BackflipState : PlayerState
{
	[Export] private PlayerState landState;
	[Export] private PlayerState jumpDashState;
	[Export] private PlayerState homingAttackState;
	[Export] private PlayerState stompState;
	[Export] private float backflipHeight;

	private float referenceAngle;
	/// <summary> How much can the player adjust their angle while backflipping? </summary>
	private readonly float MaxBackflipAdjustment = Mathf.Pi * .25f;

	public override void EnterState()
	{
		if (Player.Skills.IsSpeedBreakActive)
			Player.Skills.ToggleSpeedBreak();

		turningVelocity = 0;
		referenceAngle = Player.PathFollower.BackAngle;
		if (SaveManager.ActiveSkillRing.IsFreeRoamActive)
			referenceAngle = Player.MovementAngle + Mathf.Pi;

		Player.IsOnGround = false;
		Player.IsMovingBackward = true;
		Player.IsBackflipping = true;
		Player.MovementAngle = referenceAngle;
		Player.MoveSpeed = Player.Stats.BackflipSettings.Speed;
		Player.VerticalSpeed = Runtime.CalculateJumpPower(backflipHeight);

		Player.Lockon.IsMonitoring = true;
		Player.Animator.BackflipAnimation();
		Player.Effect.PlayActionSFX(Player.Effect.JumpSfx);

		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.BackstepAttack))
		{
			Player.Effect.PlayFireFX();
			Player.AttackState = PlayerController.AttackStates.Weak;
		}
	}

	public override void ExitState()
	{
		Player.IsBackflipping = false;
		Player.AttackState = PlayerController.AttackStates.None;
	}

	public override PlayerState ProcessPhysics()
	{
		ProcessMoveSpeed();
		ProcessTurning();
		ProcessGravity();
		Player.ApplyMovement();
		Player.CheckGround();
		Player.CheckWall(Vector3.Zero, false);
		if (Player.CheckCeiling())
			return null;
		Player.UpdateUpDirection(true, Player.PathFollower.HeightAxis);

		if (Player.IsOnGround)
			return landState;

		if (Player.Controller.IsJumpBufferActive)
		{
			Player.Controller.ResetJumpBuffer();

			if (Player.CanDoubleJump && SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.DoubleJump)) // Start a double jump
			{
				Player.StartDoubleJump();
				return null;
			}

			if (SaveManager.Config.jumpButtonMode == SaveManager.JumpButtonModeEnum.Disabled)
				return null;

			if (SaveManager.Config.jumpButtonMode == SaveManager.JumpButtonModeEnum.Stomp)
				return stompState;

			if (Player.Lockon.IsTargetAttackable)
				return homingAttackState;

			if (SaveManager.ActiveSkillRing.IsFreeRoamActive)
				Player.MovementAngle += Mathf.Pi;

			return jumpDashState;
		}

		if (Player.Controller.IsAttackBufferActive)
		{
			Player.Controller.ResetAttackBuffer();
			if (Player.Lockon.IsTargetAttackable)
				return homingAttackState;

			if (SaveManager.ActiveSkillRing.IsFreeRoamActive)
				Player.MovementAngle += Mathf.Pi;

			return jumpDashState;
		}

		if (Player.Controller.IsActionBufferActive)
		{
			Player.Controller.ResetActionBuffer();
			return stompState;
		}

		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.LightSpeedDash) &&
			Player.Controller.IsLightDashBufferActive)
		{
			Player.StartLightSpeedDash();
		}

		Player.AttemptFallIntoTheVoid();
		return null;
	}

	protected override void ProcessMoveSpeed()
	{
		ProcessAutorunStrafeSpeed();
		if (Player.Controller.IsBackTiltActive())
		{
			Player.MoveSpeed = Player.Stats.BackflipSettings.UpdateInterpolate(Player.MoveSpeed, 1f);
			return;
		}

		float inputAngle = Player.Controller.GetTargetInputAngle();
		float inputStrength = Player.Controller.GetInputStrength();
		if (Player.Controller.IsHoldingDirection(inputAngle, referenceAngle + Mathf.Pi) ||
			Player.Controller.IsBrakeHeld())
		{
			Player.MoveSpeed = Player.Stats.BackflipSettings.UpdateInterpolate(Player.MoveSpeed, -1);
			return;
		}

		if (Player.Controller.IsHoldingDirection(inputAngle, referenceAngle))
			Player.MoveSpeed = Player.Stats.BackflipSettings.UpdateInterpolate(Player.MoveSpeed, inputStrength);
		else if (Mathf.IsZeroApprox(inputStrength))
			Player.MoveSpeed = Player.Stats.BackflipSettings.UpdateInterpolate(Player.MoveSpeed, 0);
	}

	protected override void ProcessTurning()
	{
		float pathControlAmount = Player.Controller.CalculatePathControlAmount();
		float targetMovementAngle = Player.Controller.GetTargetMovementAngle() + pathControlAmount;
		if (DisableTurning(targetMovementAngle))
			return;

		// Use GroundSettings so backstep turning feels consistent with the run state
		float speedRatio = Player.Stats.GroundSettings.GetSpeedRatioClamped(Player.MoveSpeed);
		float turnSmoothing = Mathf.Lerp(Player.Stats.MinTurnAmount, Player.Stats.MaxTurnAmount, speedRatio);
		Player.MovementAngle = ExtensionMethods.SmoothDampAngle(Player.MovementAngle + Player.PathTurnInfluence, targetMovementAngle, ref turningVelocity, turnSmoothing);
	}

	protected override bool DisableTurning(float targetMovementAngle)
	{
		if (Player.IsLockoutActive &&
			Player.ActiveLockoutData.movementMode == LockoutResource.MovementModes.Replace) // Direction is being overridden
		{
			Player.MovementAngle = targetMovementAngle;
			return true;
		}

		if (Player.Controller.IsHoldingDirection(targetMovementAngle, Player.MovementAngle + Mathf.Pi, Mathf.Pi * .2f) &&
			!Player.Controller.IsStrafeModeActive)
		{
			return true;
		}

		return false;
	}
}
