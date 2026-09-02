using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class LandState : PlayerState
{
	[Export] private PlayerState runState;
	[Export] private PlayerState idleState;
	[Export] private PlayerState fallState;
	[Export] private PlayerState jumpState;
	[Export] private PlayerState crouchState;
	[Export] private PlayerState slideState;
	[Export] private PlayerState backstepState;

	private float knockbackTimer;
	private readonly float KnockbackLandingLength = .3f;

	public override void EnterState()
	{
		if (Player.IsLockoutActive &&
			Player.ActiveLockoutData.resetFlags.HasFlag(LockoutResource.ResetFlags.OnLand))
		{
			Player.RemoveLockoutData(Player.ActiveLockoutData);
		}

		if (Player.MoveSpeed < 0) // Fix negative movespeeds
		{
			Player.MoveSpeed *= -1;
			Player.IsMovingBackward = true;
			Player.MovementAngle += Mathf.Pi;
		}

		Player.ResetFallTimer();
		Player.VerticalSpeed = 0;
		Player.UpdateOrientation();
		Player.SnapToGround();
		Player.DisableAccelerationJump = false;
		Player.CanJumpDash = false;
		Player.CanDoubleJump = true;
		Player.CanAirBoost = true;
		Player.Lockon.IsMonitoring = true;

		if (Player.IsKnockback)
		{
			knockbackTimer = KnockbackLandingLength;
			Player.MoveSpeed = 0;
			Player.StrafeSpeed = 0;
			Player.IsKnockback = false;
			Player.AllowLandingSkills = false;
			Player.Animator.ResetState(0);
		}
		else
		{
			knockbackTimer = 0;

			if (!Player.IsTeleporting)
				Player.Animator.LandingAnimation();
		}
	}

	public override void ExitState()
	{
		// Snap to ground
		if (Player.IsGrindRailActive)
			return;

		// IsStomping is set false here so LandingSkills can check against it
		Player.IsStomping = false;
		Player.IsGrindstepping = false;
		Player.AllowLandingGrind = false;
		Player.Effect.PlayLandingFX();
	}

	public override PlayerState ProcessPhysics()
	{
		Player.CheckGround();
		Player.ApplyMovement();

		if (!Mathf.IsZeroApprox(knockbackTimer))
		{
			knockbackTimer = Mathf.MoveToward(knockbackTimer, 0, PhysicsManager.physicsDelta);
			return null;
		}

		if (!Player.IsOnGround)
			return fallState;

		if (Player.IsBounceJumping) // Bounce Attack
		{
			Player.DisableAccelerationJump = true;
			return jumpState;
		}

		if (Player.AllowLandingSkills)
		{
			// Apply landing skills
			CheckLandingBoost();
			CheckLandingSoul();

			Player.AllowLandingSkills = false;
		}

		// Allow buffering jump charge transition for responsiveness
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump) && Input.IsActionPressed("button_jump"))
			return Mathf.IsZeroApprox(Player.MoveSpeed) ? crouchState : slideState;

		if (Mathf.IsZeroApprox(Player.MoveSpeed))
			return idleState;

		if (Player.IsMovingBackward && !SaveManager.ActiveSkillRing.IsFreeRoamActive)
			return backstepState;

		return runState;
	}

	private void CheckLandingBoost()
	{
		bool applyLandingBoost = SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.LandDash) && !Player.IsStomping;
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompDash) && Player.IsStomping &&
			SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump) && !Input.IsActionPressed("button_jump"))
		{
			applyLandingBoost = true;
		}

		if (Player.Controller.IsBrakeHeld())
			applyLandingBoost = false;

		if (!applyLandingBoost)
			return;

		// Only apply landing boost when holding forward to avoid accidents (See Sonic and the Black Knight)
		float inputStrength = Player.Controller.GetInputStrength();
		if (!SaveManager.ActiveSkillRing.IsAutorunActive && Mathf.IsZeroApprox(inputStrength))
			return;

		float inputAngle = Player.Controller.GetTargetInputAngle();
		float targetForwardAngle = Player.PathFollower.ForwardAngle;
		if (SaveManager.ActiveSkillRing.IsFreeRoamActive)
			targetForwardAngle = Player.MovementAngle;

		if (!SaveManager.ActiveSkillRing.IsAutorunActive)
		{
			if (!Mathf.IsZeroApprox(inputStrength) && !Player.Controller.IsHoldingDirection(inputAngle, targetForwardAngle))
				return;
		}

		Player.Effect.PlayWindFX();
		Player.MovementAngle = targetForwardAngle;
		Player.MoveSpeed = Mathf.Max(Player.MoveSpeed, Player.Skills.landingDashSpeed);
	}

	private void CheckLandingSoul()
	{
		// Bonus EXP
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompExp) && Player.IsStomping)
		{
			Player.Effect.PlayDarkSpiralFX();
			StageSettings.Instance.CurrentEXP += 20;
		}

		// Increase soul gauge
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.LandSoul) && !Player.IsStomping)
		{
			Player.Effect.PlayDarkSpiralFX();

			switch (SaveManager.ActiveSkillRing.GetAugmentIndex(SkillKey.LandSoul))
			{
				case 0:
					Player.Skills.ModifySoulGauge(1);
					break;
				case 1:
					Player.Skills.ModifySoulGauge(2);
					break;
				case 2:
					Player.Skills.ModifySoulGauge(4);
					break;
				case 3:
					Player.Skills.ModifySoulGauge(4 + (Mathf.Min(StageSettings.Instance.CurrentRingCount, 5) * 2));
					StageSettings.Instance.UpdateRingCount(5, StageSettings.MathModeEnum.Subtract, true);
					break;
			}
		}
	}
}
