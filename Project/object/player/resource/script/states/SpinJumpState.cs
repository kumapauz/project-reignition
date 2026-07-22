using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class SpinJumpState : PlayerState
{
	[Export] private PlayerState stompState;
	[Export] private PlayerState landState;
	[Export] private PlayerState jumpDashState;
	[Export] private PlayerState homingAttackState;
	[Export] private PlayerState darkspineSpinState;

	public bool IsShortenedJump { get; set; }
	private readonly float JumpCurve = .95f;

	public override void EnterState()
	{
		Player.IsSpinJump = true;
		Player.AllowLandingGrind = true;
		Player.AttackState = PlayerController.AttackStates.Weak;
		Player.Animator.StartSpin(5f);
		Player.Effect.StartSpinFX();
		Player.Effect.StartSpinSquashFX();
	}

	public override void ExitState()
	{
		Player.IsSpinJump = false;
		Player.AttackState = PlayerController.AttackStates.None;
		Player.Animator.ResetState(0);
		Player.Effect.StopSpinFX();
	}

	public override PlayerState ProcessPhysics()
	{
		if (!SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump) && !Input.IsActionPressed("button_jump") && Player.IsVariableJumpHeightEnabled)
			IsShortenedJump = true;

		ProcessMoveSpeed();
		ProcessTurning();
		ProcessGravity();
		Player.ApplyMovement();
		Player.IsMovingBackward = Player.Controller.IsHoldingDirection(Player.MovementAngle, Player.PathFollower.BackAngle);
		Player.CheckGround();
		Player.CheckWall(Vector3.Zero);
		if (Player.CheckCeiling())
			return null;
		Player.UpdateUpDirection();

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

			if (SaveManager.Config.jumpButtonMode == SaveManager.JumpButtonModeEnum.Stomp)
				return stompState;

			return Player.Lockon.IsTargetAttackable ? homingAttackState : jumpDashState;
		}

		if (Player.Controller.IsAttackBufferActive)
		{
			Player.Controller.ResetAttackBuffer();

			if (Player.Lockon.IsTargetAttackable)
				return homingAttackState;

			if (Player.IsDarkspineSonic)
			{
				if (Player.Controller.IsBrakeHeld() || !Player.Controller.IsGyroEnabled && (Player.Controller.InputAxis.IsZeroApprox() ||
					!Player.Controller.IsHoldingDirection(Player.Controller.GetTargetInputAngle(), Player.MovementAngle)))
				{
					return darkspineSpinState;
				}
			}

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

		return null;
	}

	protected override void ProcessGravity()
	{
		if (IsShortenedJump && Player.VerticalSpeed > 0)
			Player.VerticalSpeed *= JumpCurve; // Kill jump height

		base.ProcessGravity();
	}
}
