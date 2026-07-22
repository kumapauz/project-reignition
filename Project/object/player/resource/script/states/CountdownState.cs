using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class CountdownState : PlayerState
{
	[Export]
	private PlayerState idleState;
	[Export]
	private PlayerState runState;
	private float countdownBoostTimer;
	private readonly float BoostWindow = .5f;

	public override void EnterState()
	{
		Player.IsCountdown = true;

		Player.Transform = Transform3D.Identity;
		Player.PathFollower.Resync();
		Player.MovementAngle = Player.PathFollower.ForwardAngle;

		Player.Animator.SnapRotation(Player.PathFollower.ForwardAngle);
		Player.Animator.PlayCountdown();
	}

	public override void ExitState()
	{
		Player.SnapToGround();
		Player.IsCountdown = false;
		Player.Animator.CancelOneshot();

		// Snap camera to gameplay
		Player.Camera.SnapXform();
		Player.Camera.SnapFlag = true;
	}

	public override PlayerState ProcessPhysics()
	{
		if (Player.Controller.IsGimmickBufferActive || Player.Controller.IsDownShakeRegistered())
		{
			Player.Controller.ResetGimmickBuffer();
			countdownBoostTimer = (countdownBoostTimer > 0) ? -BoostWindow : BoostWindow;
		}

		if (!Interface.Countdown.IsCountdownActive)
			return ProcessLevelStart();

		countdownBoostTimer = Mathf.MoveToward(countdownBoostTimer, 0, PhysicsManager.physicsDelta);
		return null;
	}

	private PlayerState ProcessLevelStart()
	{
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.RocketStart) &&
			countdownBoostTimer > 0) // Successful starting boost
		{
			Player.MoveSpeed = Player.Skills.countdownBoostSpeed;
			Player.Effect.PlayWindFX();
			Player.AddLockoutData(new()
			{
				length = .5f,
				overrideSpeed = true,
				speedRatio = Player.Skills.countdownBoostSpeed,
				resetFlags = LockoutResource.ResetFlags.OnJump,
			});

			if (SaveManager.ActiveSkillRing.GetAugmentIndex(SkillKey.RocketStart) == 1) // Fill the soul gauge
				Player.Skills.ModifySoulGauge(Player.Skills.MaxSoulPower);

			if (StageSettings.Instance.Data.RequiredSkill?.Key == SkillKey.RocketStart)
				StageSettings.Instance.IncrementObjective();

			return runState;
		}

		return idleState;
	}
}
