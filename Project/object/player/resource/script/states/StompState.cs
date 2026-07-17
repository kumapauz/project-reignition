using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class StompState : PlayerState
{
	[Export]
	private PlayerState landState;

	/// <summary> How fast to fall when stomping. </summary>
	private readonly float StompSpeed = -32;
	/// <summary> How much gravity to add each frame. </summary>
	private readonly float JumpCancelGravity = 180;
	/// <summary> How much gravity to add each frame. </summary>
	private readonly float StompGravity = 540;

	public override void EnterState()
	{
		if (!SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompBounce) ||
			Player.Controller.IsHoldingDirection(Player.Controller.GetTargetInputAngle(), Player.PathFollower.BackAngle))
		{
			Player.MoveSpeed = 0; // Go STRAIGHT down
			Player.StrafeSpeed = 0;
		}
		Player.IsStomping = true;
		Player.Lockon.IsMonitoring = false;

		Player.AllowLandingGrind = true;
		if (Player.IsGrindstepping)
			Player.Animator.ResetState(.1f);

		Player.AllowLandingSkills = true;

		bool isAttackStomp = SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompAttack);

		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompBounce))
		{
			Player.Effect.StartSpinFX();
			Player.Animator.StartSpin(4f);
			Player.AttackState = PlayerController.AttackStates.Weak;
		}
		else
		{
			Player.Animator.StompAnimation(isAttackStomp);
		}

		if (isAttackStomp)
		{
			Player.AttackState = PlayerController.AttackStates.Weak;
			Player.ChangeHitbox("stomp");
			Player.Effect.StartStompFX();
		}
	}

	public override void ExitState()
	{
		if (!Player.IsOnGround && !Player.IsGrindRailActive)
			Player.IsStomping = false;

		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompBounce))
		{
			Player.Effect.StopSpinFX();
			Player.Animator.ResetState(0f);
		}

		Player.Effect.StopStompFX();
		Player.ChangeHitbox("RESET");
		Player.AttackState = PlayerController.AttackStates.None;
	}

	public override PlayerState ProcessPhysics()
	{
		UpdateVerticalSpeed();
		Player.ApplyMovement();
		Player.CheckGround();
		Player.UpdateUpDirection(true);

		if (Player.IsOnGround)
		{
			if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompBounce) &&
				(Input.IsActionPressed("button_action") ||
				(Input.IsActionPressed("button_jump") && SaveManager.Config.jumpButtonMode != SaveManager.JumpButtonModeEnum.Attack)))
			{
				Player.IsBounceJumping = true;

				// Bound Jump inherits cached speed
				Player.MoveSpeed = Player.LastActionMoveSpeed;
			}

			return landState;
		}

		Player.AttemptFallIntoTheVoid();
		return null;
	}

	private void UpdateVerticalSpeed()
	{
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompAttack) || SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.StompBounce))
			Player.VerticalSpeed = Mathf.MoveToward(Player.VerticalSpeed, StompSpeed, StompGravity * PhysicsManager.physicsDelta);
		else
			Player.VerticalSpeed = Mathf.MoveToward(Player.VerticalSpeed, StompSpeed, JumpCancelGravity * PhysicsManager.physicsDelta);
	}
}
