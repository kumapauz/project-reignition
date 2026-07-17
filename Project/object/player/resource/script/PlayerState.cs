using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class PlayerState : Node
{
	protected PlayerController Player { get; private set; }

	public void Initialize(PlayerController player) => Player = player;

	/// <summary> Called when this state is entered. </summary>
	public virtual void EnterState() { }

	/// <summary> Caches last ground/aerial speed to PlayerController. Writes speed used for Bound Jump.</summary>
	public void CacheMomentumOnExit() => Player.LastActionMoveSpeed = Player.MoveSpeed;

	/// <summary> Called when this state is exited. </summary>
	public virtual void ExitState() { }

	/// <summary> Called on each frame update. </summary>
	public virtual PlayerState ProcessFrame() => null;

	/// <summary> Called on each physics update. </summary>
	public virtual PlayerState ProcessPhysics() => null;

	protected bool turnInstantly;
	protected MovementSetting ActiveMovementSettings => Player.IsOnGround ? Player.Stats.GroundSettings : Player.Stats.AirSettings;
	protected virtual void ProcessMoveSpeed()
	{
		ProcessAutorunStrafeSpeed();
		turnInstantly = Mathf.IsZeroApprox(Player.MoveSpeed) && !Player.Skills.IsSpeedBreakActive; // Store this for turning function

		if (Player.Skills.IsSpeedBreakActive)
		{
			// Override to speedbreak speed
			if (Player.Skills.IsSpeedBreakOverrideActive)
				Player.MoveSpeed = ActiveMovementSettings.UpdateInterpolate(Player.Skills.speedBreakSpeed, 1.0f);
			return;
		}

		float inputStrength = Player.Controller.GetInputStrength();
		if (Player.IsLockoutActive)
		{
			// Process Lockouts
			if (Player.ActiveLockoutData.overrideSpeed)
			{
				AccelerateLockout();
				return;
			}

			if (Player.ActiveLockoutData.movementMode == LockoutResource.MovementModes.Strafe)
			{
				Accelerate(inputStrength);
				return;
			}
		}

		if ((!SaveManager.ActiveSkillRing.IsAutorunActive || Player.IsMovingBackward) &&
			Mathf.IsZeroApprox(inputStrength)) // Basic slow down
		{
			Deccelerate();
			return;
		}

		float pathControlAmount = Player.Controller.CalculatePathControlAmount();
		float targetInputAngle = Player.Controller.GetTargetInputAngle() + pathControlAmount;
		if (IsBraking(targetInputAngle)) // Turning around
		{
			Brake();
			return;
		}

		// Always move at full power when autorun is enabled
		if (SaveManager.ActiveSkillRing.IsAutorunActive && !Player.IsMovingBackward)
		{
			Accelerate(1f);
			return;
		}

		if (Player.IsLockoutActive && Player.ActiveLockoutData.spaceMode == LockoutResource.SpaceModes.PathFollower) // Zipper exception
		{
			// Arbitrary math to make it easier to maintain speed
			float inputDot = Mathf.Abs(ExtensionMethods.DotAngle(Player.MovementAngle, targetInputAngle));
			inputStrength *= Mathf.Clamp(inputDot + .5f, 0, 1f);
		}

		if (Mathf.IsZeroApprox(inputStrength))
			return;

		Accelerate(inputStrength);
	}

	protected virtual void ProcessAutorunStrafeSpeed()
	{
		if (!SaveManager.ActiveSkillRing.IsAutorunActive)
		{
			Player.StrafeSpeed = 0;
			return;
		}

		if (Player.Controller.IsBrakeHeld() ||
			Player.IsLockoutOverridingMovementAngle && (Player.ActiveLockoutData.movementMode != LockoutResource.MovementModes.Strafe || Player.ActiveLockoutData.recenterPlayer))
		{
			Player.StrafeSpeed = Player.Stats.StrafeSettings.UpdateInterpolate(Player.StrafeSpeed, -1.0f); // Reset to 0 quickly
			return;
		}

		float input = Player.Controller.InputAxis.X;
		if (Player.Camera.ActiveSettings.controlMode == CameraSettingsResource.ControlModeEnum.Sidescrolling)
			input = Player.Controller.InputAxis.Y;
		if (Player.Controller.IsGyroEnabled)
			input = Mathf.Clamp(input + Player.Controller.GyroInput.X, -1, 1);

		if (Player.PathFollower.IsReversingPath)
			input *= -1;

		float dot = ExtensionMethods.DotAngle(Player.PathFollower.ForwardAngle, Player.Controller.XformAngle);
		if (Mathf.Abs(dot) > 0.5f)
			input *= Mathf.Sign(dot) >= 0 ? 1 : -1; // Take camera direction into account
		Player.StrafeSpeed = Player.Stats.StrafeSettings.UpdateInterpolateSigned(Player.StrafeSpeed, input);
	}

	private bool IsBraking(float inputAngle)
	{
		if (Player.Controller.IsBrakeHeld())
			return true;

		if (Mathf.IsZeroApprox(Player.MoveSpeed))
			return false;

		if (Player.Camera.IsCrossfading)
			return false;

		if (Player.Controller.IsBackTiltActive())
			return !Player.IsMovingBackward;

		return Player.Controller.IsHoldingDirection(inputAngle, Player.MovementAngle + Mathf.Pi) && Player.Controller.GetInputStrength() > 0.5f;
	}

	protected virtual void Deccelerate()
	{
		Player.MoveSpeed = ActiveMovementSettings.UpdateInterpolate(Player.MoveSpeed, 0);
	}

	protected virtual void AccelerateLockout()
	{
		if (Player.IsMovingBackward)
		{
			Deccelerate();
			return;
		}

		Player.MoveSpeed = Player.ActiveLockoutData.ApplySpeed(Player.MoveSpeed, ActiveMovementSettings);
	}

	protected virtual void Accelerate(float inputStrength)
	{
		if (Player.MoveSpeed < Player.Stats.BackstepSettings.Speed) // Accelerate faster when at low speeds
			Player.MoveSpeed = Mathf.Lerp(Player.MoveSpeed, ActiveMovementSettings.Speed * ActiveMovementSettings.GetSpeedRatio(Player.Stats.BackstepSettings.Speed), .05f * inputStrength);

		Player.MoveSpeed = ActiveMovementSettings.UpdateInterpolate(Player.MoveSpeed, inputStrength); // Accelerate based on input strength/input direction
	}

	protected virtual void Brake() => Player.MoveSpeed = ActiveMovementSettings.UpdateInterpolate(Player.MoveSpeed, -1);

	protected float strafeBlend;
	protected float turningVelocity;
	protected virtual void ProcessTurning()
	{
		if (SaveManager.ActiveSkillRing.IsAutorunActive)
		{
			if (Mathf.IsZeroApprox(Player.MoveSpeed) && !Player.Controller.IsBrakeHeld())
				Player.IsMovingBackward = Player.Controller.IsHoldingDirection(Player.Controller.GetTargetInputAngle(), Player.PathFollower.BackAngle);
		}

		if (Player.Controller.IsBackTiltActive())
			return;

		float pathControlAmount = Player.Controller.CalculatePathControlAmount();
		float targetMovementAngle = Player.Controller.GetTargetMovementAngle() + pathControlAmount;
		if (DisableTurning(targetMovementAngle))
			return;

		float speedRatio = Player.Stats.GroundSettings.GetSpeedRatioClamped(Player.MoveSpeed);
		targetMovementAngle = ProcessTargetMovementAngle(targetMovementAngle);

		// Normal turning
		float inputDeltaAngle = ExtensionMethods.SignedDeltaAngleRad(targetMovementAngle, Player.PathFollower.ForwardAngle);
		float movementDeltaAngle = ExtensionMethods.SignedDeltaAngleRad(Player.MovementAngle, Player.PathFollower.ForwardAngle);
		bool isRecentering = Player.Controller.IsRecentering(movementDeltaAngle, inputDeltaAngle);
		float maxTurnAmount = isRecentering ? Player.Stats.RecenterTurnAmount : Player.Stats.MaxTurnAmount;

		float turnSmoothing = Mathf.Lerp(Player.Stats.MinTurnAmount, maxTurnAmount, speedRatio);
		if (Player.Skills.IsTimeBreakActive)
		{
			// Increase turning responsiveness when time break is active
			turnSmoothing *= 0.5f;
		}

		Player.MovementAngle += pathControlAmount;
		Turn(targetMovementAngle, turnSmoothing);

		// Strafe implementation
		ProcessAutorunStrafe(targetMovementAngle);
	}

	protected virtual bool DisableTurning(float targetMovementAngle)
	{
		if (Mathf.IsZeroApprox(Player.MoveSpeed) && Player.Controller.IsBrakeHeld())
			return true;

		if (Player.IsLockoutActive &&
			Player.ActiveLockoutData.movementMode == LockoutResource.MovementModes.Replace) // Direction is being overridden
		{
			Player.MovementAngle = targetMovementAngle;
			return true;
		}

		if (turnInstantly) // Instantly set movement angle to target movement angle
		{
			SnapRotation(targetMovementAngle);
			return true;
		}

		if (Player.Controller.IsHoldingDirection(targetMovementAngle, Player.MovementAngle + Mathf.Pi) &&
			!Player.Controller.IsStrafeModeActive)
		{
			return true;
		}

		return false;
	}

	protected virtual void ProcessAutorunStrafe(float targetMovementAngle)
	{
		if (!SaveManager.ActiveSkillRing.IsAutorunActive)
			return;

		if (Mathf.IsZeroApprox(Player.Controller.GetInputStrength()))
			strafeBlend = Mathf.MoveToward(strafeBlend, 1.0f, PhysicsManager.physicsDelta);
		else
			strafeBlend = 0;

		Player.MovementAngle += Player.PathTurnInfluence;
		Player.MovementAngle = Mathf.LerpAngle(Player.MovementAngle, targetMovementAngle, strafeBlend);
	}

	protected virtual void Turn(float targetMovementAngle, float smoothing) => Player.MovementAngle = ExtensionMethods.SmoothDampAngle(Player.MovementAngle, targetMovementAngle, ref turningVelocity, smoothing);

	protected virtual float ProcessTargetMovementAngle(float targetMovementAngle)
	{
		if (SaveManager.ActiveSkillRing.IsFreeRoamActive)
		{
			float dot = ExtensionMethods.DotAngle(Player.MovementAngle, Player.PathFollower.ForwardAngle);

			if (Mathf.Abs(dot) <= .5f)
				return targetMovementAngle;

			if (dot < 0f)
				return Player.Controller.ImproveAnalogPrecision(targetMovementAngle, Player.PathFollower.BackAngle);
		}


		return Player.Controller.ImproveAnalogPrecision(targetMovementAngle, Player.PathFollower.ForwardAngle);
	}

	protected virtual void SnapRotation(float targetMovementAngle)
	{
		turningVelocity = 0;
		Player.MovementAngle = targetMovementAngle;
	}

	protected virtual void ProcessGravity() => Player.VerticalSpeed = Mathf.MoveToward(Player.VerticalSpeed, Runtime.MaxGravity, Runtime.Gravity * PhysicsManager.physicsDelta);
}
