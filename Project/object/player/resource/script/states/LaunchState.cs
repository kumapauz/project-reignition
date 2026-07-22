using Godot;
using Project.Core;
using Project.Gameplay.Objects;

namespace Project.Gameplay;

public partial class LaunchState : PlayerState
{
	[Export] private PlayerState landState;
	[Export] private PlayerState jumpDashState;
	[Export] private PlayerState homingAttackState;
	[Export] private PlayerState stompState;
	[Export] private PlayerState fallState;

	private bool isDoublingJumping;
	public LaunchSettings Settings { get; private set; }
	public Launcher ActiveLauncher => Settings.Launcher;
	private float launcherTime;
	private RaycastHit wallHit;
	private readonly float LaunchLength = 0.2f;
	public bool UpdateSettings(LaunchSettings settings)
	{
		if (settings.startPosition.IsEqualApprox(settings.endPosition) &&
			Mathf.IsZeroApprox(settings.middleHeight)) // Launcher initialization error
		{
			return false;
		}

		if (Player.IsLaunching &&
			Settings.Launcher != null &&
			Settings.Launcher == settings.Launcher) // Already launching
		{
			return false;
		}

		Settings = settings;
		return true;
	}

	public override void EnterState()
	{
		launcherTime = 0;
		wallHit = new();
		isDoublingJumping = false;

		Player.IsOnGround = false;
		Player.IsLaunching = true;
		Player.IsBounceJumping = false;
		Player.IsMovingBackward = false;
		Player.AllowLandingGrind = true;
		Player.AllowLandingSkills = false;
		Player.CanDoubleJump = true;
		Player.CanAirBoost = true;
		Player.MoveSpeed = Settings.HorizontalVelocity;
		Player.VerticalSpeed = Settings.InitialVerticalVelocity;

		Player.Lockon.IsMonitoring = Settings.AllowInterruption; // Disable lockon monitoring while launch is active

		if (ActiveLauncher == null || Settings.OneshotEnemies)
			Player.AttackState = PlayerController.AttackStates.OneShot; // Launchers typically oneshot enemies for fairness
		else
			Player.AttackState = PlayerController.AttackStates.None;

		Player.UpDirection = Vector3.Up;
		Player.Rotation = Vector3.Zero; // Reset rotation

		if (Settings.UseAutoAlign)
		{
			Player.MovementAngle = GetLaunchFacingAngle();
			Player.Animator.SnapRotation(Player.MovementAngle);
		}

		if (Settings.IsJump) // Play jump effects
		{
			if (Settings.InitialVerticalVelocity > 0)
				Player.Animator.AutoJumpAnimation();
			else
				Player.Animator.IsFallTransitionEnabled = true;

			Player.Effect.PlayActionSFX(Player.Effect.JumpSfx);
		}

		if (Settings.IgnoreCollisions)
			Player.ChangeHitbox("disable-environment");
	}

	public override void ExitState()
	{
		Player.IsLaunching = false;
		if (!wallHit && !isDoublingJumping)
		{
			Player.MoveSpeed = Settings.HorizontalVelocity * .5f; // Prevent too much movement
			Player.VerticalSpeed = Player.IsOnGround ? 0 : Settings.FinalVerticalVelocity;
		}

		Player.AttackState = PlayerController.AttackStates.None;

		Player.CanJumpDash = Settings.AllowJumpDash;
		if (!Player.Lockon.IsMonitoring)
			Player.Lockon.IsMonitoring = !Player.IsOnGround && Settings.AllowJumpDash;

		Player.Effect.StopSpinFX();
		Player.Effect.StopTrailFX();
		Player.Animator.ResetState();

		Player.ChangeHitbox("RESET");
		Player.EmitSignal(PlayerController.SignalName.LaunchFinished);
		Settings.Launcher?.Deactivate();
	}

	public override PlayerState ProcessPhysics()
	{
		if (IsRecenteringPlayer())
			return null;

		launcherTime = Mathf.Min(launcherTime + PhysicsManager.physicsDelta, Settings.TotalTravelTime);
		Vector3 targetPosition = Settings.InterpolatePositionTime(launcherTime);
		float heightDelta = Mathf.IsZeroApprox(launcherTime) ? 0 : targetPosition.Y - Player.GlobalPosition.Y;

		if (!Settings.IgnoreCollisions)
		{
			UpdateWallHit(targetPosition);
			if (wallHit)
				return fallState;
		}

		Player.GlobalPosition = targetPosition;
		Player.VerticalSpeed = heightDelta / PhysicsManager.physicsDelta;
		Player.PathFollower.Resync();

		if (heightDelta < 0 && Player.CheckGround()) // Only check ground when falling
		{
			Player.MoveSpeed = 0;
			Player.StrafeSpeed = 0;
			return landState;
		}

		if (Settings.AllowInterruption && launcherTime >= LaunchLength)
		{
			if (Player.Controller.IsJumpBufferActive)
			{
				Player.Controller.ResetJumpBuffer();
				if (Player.CanDoubleJump && SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.DoubleJump)) // Start a double jump
				{
					isDoublingJumping = true;
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
				return Player.Lockon.IsTargetAttackable ? homingAttackState : jumpDashState;
			}

			if (Player.Controller.IsActionBufferActive)
			{
				Player.Controller.ResetActionBuffer();
				return stompState;
			}
		}

		if (Settings.IsLauncherFinished(launcherTime)) // Revert to normal state
			return fallState;

		return null;
	}

	private float GetLaunchFacingAngle()
	{
		Vector3 launchDirection;
		if (Settings.Launcher == null)
			launchDirection = Settings.InitialVelocity.RemoveVertical();
		else
			launchDirection = Settings.Launcher.GetLaunchDirection().RemoveVertical();

		if (launchDirection.IsEqualApprox(Vector3.Zero)) // Fallback to PathFollower's forward angle
			return Player.PathFollower.ForwardAngle;

		return ExtensionMethods.CalculateForwardAngle(launchDirection);
	}

	private bool IsRecenteringPlayer()
	{
		if (Settings.Launcher?.IsPlayerCentered != false)
			return false;

		Player.GlobalPosition = Settings.Launcher.RecenterPlayer();
		return true;
	}

	private void UpdateWallHit(Vector3 targetPosition)
	{
		wallHit = new();
		Vector3 direction = targetPosition - Player.GlobalPosition;
		RaycastHit hit = Player.CastRay(Player.GlobalPosition, direction, Runtime.Instance.environmentMask);
		DebugManager.DrawRay(Player.GlobalPosition, direction, hit ? Colors.Red : Colors.White);

		if (!hit || !hit.collidedObject.IsInGroup("wall"))
			return;

		wallHit = hit;
		Player.MoveSpeed = 0;
		Player.StrafeSpeed = 0;
		Player.GlobalPosition = wallHit.point - (wallHit.direction * Player.CollisionSize.X);
	}
}