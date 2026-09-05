using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class PlayerInputController : Node
{
	private PlayerController Player { get; set; }
	public void Initialize(PlayerController player) => Player = player;

	public override void _Ready() => Runtime.Instance.ControllerChanged += dir => OnControllerChanged();

	private void OnControllerChanged()
	{
		if (Runtime.Instance.IsUsingController)
			Input.SetJoyMotionSensorsEnabled(Runtime.Instance.ActiveController, true);
		else
			Input.SetJoyMotionSensorsEnabled(0, true);
	}

	public override void _EnterTree()
	{
		if (OS.IsDebugBuild()) // For booting into a level
			Input.SetJoyMotionSensorsEnabled(0, true);
	}

	public override void _ExitTree()
	{
		if (Runtime.Instance.IsUsingController)
			Input.SetJoyMotionSensorsEnabled(Runtime.Instance.ActiveController, false);
	}

	private Vector2 mouseInput;

	[Export]
	private Curve InputCurve { get; set; }
	public float GetInputStrength()
	{
		float inputLength = InputAxis.Length();
		if (IsGyroEnabled)
			inputLength = new Vector2(InputHorizontal, InputVertical).Length();

		if (Player.IsLockoutActive && Player.ActiveLockoutData.movementMode == LockoutResource.MovementModes.Replace)
		{
			float inputDot = Mathf.Abs(ExtensionMethods.DotAngle(GetTargetInputAngle(), GetTargetMovementAngle()));
			if (!Mathf.IsZeroApprox(inputLength) && inputDot < .2f) // Fixes player holding perpendicular to target direction
				return 0;
		}

		return InputCurve.Sample(inputLength);
	}

	public float DeadZone => SaveManager.Config.deadZone;

	private float jumpBuffer;
	public bool IsJumpBufferActive => !Mathf.IsZeroApprox(jumpBuffer);
	private readonly float InputBufferLength = .2f;
	public void ResetJumpBuffer() => jumpBuffer = 0;

	private float actionBuffer;
	public bool IsActionBufferActive => !Mathf.IsZeroApprox(actionBuffer);
	public void ResetActionBuffer() => actionBuffer = 0;

	public bool IsGimmickBufferActive => IsActionBufferActive || IsAttackBufferActive;
	public void ResetGimmickBuffer()
	{
		ResetActionBuffer();
		ResetAttackBuffer();
	}

	private float attackBuffer;
	public bool IsAttackBufferActive => !Mathf.IsZeroApprox(attackBuffer);
	public void ResetAttackBuffer() => attackBuffer = 0;

	private float stepBuffer;
	private int stepDirection;
	public int StepDirection => stepDirection * (Player.Camera.ActiveSettings.controlMode == CameraSettingsResource.ControlModeEnum.Reverse ? -1 : 1);
	public bool IsStepBufferActive => !Mathf.IsZeroApprox(stepBuffer);
	public void ResetStepBuffer()
	{
		stepDirection = 0;
		stepBuffer = 0;
	}

	private float lightDashBuffer;
	public bool IsLightDashBufferActive => !Mathf.IsZeroApprox(lightDashBuffer);
	public void ResetLightDashBuffer() => lightDashBuffer = 0;

	/// <summary> Angle to use when transforming from world space to camera space. </summary>
	public float XformAngle { get; set; }
	public Vector2 InputAxis { get; private set; }
	public Vector2 NonZeroInputAxis { get; private set; }
	public float InputHorizontal { get; private set; }
	public float InputVertical { get; private set; }
	public Vector2 CameraAxis { get; private set; }

	public float StepAxis { get; private set; }

	/// <summary> Minimum angle from PathFollower.ForwardAngle that counts as backstepping/moving backwards. </summary>
	private readonly float MinBackStepAngle = Mathf.Pi * .6f;
	/// <summary> Maximum angle that counts as holding a direction. </summary>
	private const float MaximumHoldDelta = Mathf.Pi * .35f;

	/// <summary> Maximum amount the player can turn when running at full speed. </summary>
	public readonly float TurningDampingRange = Mathf.Pi * .35f;
	/// <summary> Maximum amount the player can turn when in an autorun lockout at minimum speed. </summary>
	public readonly float AutorunLockoutTurningDampingRange = Mathf.Pi * 0.45f;
	/// <summary> Rotation amount to just flat-out ignore player input. </summary>
	public readonly float TurningDeadzone = Mathf.Pi * .08f;

	public void ProcessInputs()
	{
		ProcessMouseMovement();
		ProcessGyroMovement();
		InputAxis = Input.GetVector("move_left", "move_right", "move_up", "move_down", DeadZone);
		InputAxis = (InputAxis + mouseInput).LimitLength(1f);
		InputHorizontal = Input.GetAxis("move_left", "move_right");
		InputHorizontal = Mathf.Clamp(InputHorizontal + mouseInput.X + GyroInput.X, -1f, 1f);
		InputVertical = Input.GetAxis("move_up", "move_down");
		InputVertical = Mathf.Clamp(InputVertical + mouseInput.Y + GyroInput.Y, -1f, 1f);
		if (!InputAxis.IsZeroApprox())
			NonZeroInputAxis = InputAxis;

		CameraAxis = Input.GetVector("camera_left", "camera_right", "camera_up", "camera_down", DeadZone);

		UpdateJumpBuffer();
		UpdateActionBuffer();
		UpdateAttackBuffer();
		UpdateStepBuffer();
		UpdateLightDashBuffer();
	}

	/// <summary> Constant to convert floats ratios to int percentages.  </summary>
	private readonly float MouseConversionFactor = 100f;
	private readonly float MouseMotionDenominator = 40f;
	private readonly float MouseMotionDeadzone = 5f;
	private void ProcessMouseMovement()
	{
		if (SaveManager.Config.mouseControlMode == SaveManager.MouseControlModeEnum.Disabled || Runtime.Instance.IsUsingController)
		{
			// Disable mouse inputs
			mouseInput = Vector2.Zero;
			return;
		}

		if (SaveManager.Config.mouseControlMode == SaveManager.MouseControlModeEnum.Absolute)
			ProcessPositionalMouseInputs();

		if (SaveManager.Config.mouseControlMode == SaveManager.MouseControlModeEnum.Relative)
			ProcessRelativeMouseInputs();
	}

	private void ProcessPositionalMouseInputs()
	{
		// Convert input ranges to [-100f, 100f]
		Vector2 inputRatio = (Runtime.Instance.MousePositionRatio - Vector2.One * 0.5f) * 2f * MouseConversionFactor;
		inputRatio.Y += SaveManager.Config.mouseVerticalOffset;
		if (Mathf.Abs(inputRatio.X) < SaveManager.Config.mouseDeadzone)
		{
			mouseInput.X = 0f;
		}
		else
		{
			mouseInput.X = (inputRatio.X - SaveManager.Config.mouseDeadzone) / (SaveManager.Config.mouseHorizontalRange - SaveManager.Config.mouseDeadzone);
			mouseInput.X = Mathf.Clamp(mouseInput.X, -1f, 1f);
		}

		if (Mathf.Abs(inputRatio.Y) < SaveManager.Config.mouseDeadzone || !SaveManager.Config.isMouseVerticalEnabled)
		{
			mouseInput.Y = 0f;
		}
		else
		{
			mouseInput.Y = (inputRatio.Y - SaveManager.Config.mouseDeadzone) / (SaveManager.Config.mouseVerticalRange - SaveManager.Config.mouseDeadzone);
			mouseInput.Y = Mathf.Clamp(mouseInput.Y, -1f, 1f);
		}
	}

	private void ProcessRelativeMouseInputs()
	{
		Vector2 inputRatio = Runtime.Instance.MouseMotionAmount;
		float deadzone = MouseMotionDeadzone * SaveManager.Config.mouseSensitivity / MouseConversionFactor;
		if (Mathf.Abs(inputRatio.X) < deadzone)
		{
			mouseInput.X = 0;
		}
		else
		{
			mouseInput.X = inputRatio.X / MouseMotionDenominator;
			mouseInput.X = Mathf.Clamp(mouseInput.X, -1f, 1f);
		}

		if (Mathf.Abs(inputRatio.Y) < deadzone || !SaveManager.Config.isMouseVerticalEnabled)
		{
			mouseInput.Y = 0f;
			return;
		}

		mouseInput.Y = inputRatio.Y / MouseMotionDenominator;
		mouseInput.Y = Mathf.Clamp(mouseInput.Y, -1f, 1f);
	}

	/// <summary> Determines whether to invert gyro inputs for certain stage objects. </summary>
	public bool GyroInvertHorizontal { get; set; }
	/// <summary> Determines whether to invert vertical gyro inputs. </summary>
	public bool GyroInvertVertical { get; set; }
	/// <summary> Determines whether to use the full vertical axis for vertical gyro controls. </summary>
	public bool GyroUseFullVertical { get; set; }
	/// <summary> Offsets the gyro calibration. </summary>
	public Vector3 GyroCalibrationOffset { get; set; }
	public bool IsGyroEnabled => IsStrafeModeActive && SaveManager.Config.isGyroEnabled && Input.IsJoyMotionSensorsEnabled(Runtime.Instance.ActiveController);

	public Vector2 GyroInput { get; private set; }
	private Vector2 gyroInputVelocity;
	private readonly float GyroSmoothing = 5.0f;
	private readonly float TurnSensitivity = 0.2f;
	private readonly float TurnDeadzone = 0.5f;
	private readonly float PitchDeadzone = 0.5f;
	private readonly float PitchSensitivity = 0.2f;
	private readonly float ReverseDeadzone = -4f;
	public void ProcessGyroMovement(bool disableSmoothing = false)
	{
		if (!IsGyroEnabled)
		{
			GyroInput = Vector2.Zero;
			return;
		}

		Vector3 rawGyroInput = Input.GetJoyAccelerometer(Runtime.Instance.ActiveController);
		rawGyroInput += GyroCalibrationOffset;
		Vector2 targetGyroInput = Vector2.Zero;
		if (Mathf.Abs(rawGyroInput.X) >= TurnDeadzone)
		{
			targetGyroInput.X = rawGyroInput.X - Mathf.Sign(rawGyroInput.X) * TurnDeadzone;
			targetGyroInput.X *= TurnSensitivity * SaveManager.Config.gyroSensitivity * 0.01f;
		}

		if (!GyroUseFullVertical)
		{
			if (rawGyroInput.Y > ReverseDeadzone)
				targetGyroInput.Y = -1f;
		}
		else if (Mathf.Abs(rawGyroInput.Y) >= PitchDeadzone)
		{
			targetGyroInput.Y = rawGyroInput.Y - Mathf.Sign(rawGyroInput.Y) * PitchDeadzone;
			targetGyroInput.Y *= PitchSensitivity * SaveManager.Config.gyroSensitivity * 0.01f;
		}

		if (GyroInvertHorizontal)
			targetGyroInput.X *= -1;

		if (GyroInvertVertical)
			targetGyroInput.Y *= -1;

		targetGyroInput.X = Mathf.Clamp(targetGyroInput.X, -1f, 1f);
		targetGyroInput.Y = Mathf.Clamp(targetGyroInput.Y, -1f, 1f);

		if (disableSmoothing)
		{
			GyroInput = targetGyroInput;
			InputHorizontal = GyroInput.X;
		}
		else
		{
			GyroInput = ExtensionMethods.SmoothDamp(GyroInput, targetGyroInput, ref gyroInputVelocity, GyroSmoothing * PhysicsManager.physicsDelta);
		}
	}

	private readonly float DownShakeSensitivity = 4f;
	private readonly float DownShakeDeadzone = -8f;
	/// <summary> A basic shake downward. </summary>
	public bool IsDownShakeRegistered(float multiplier = 1f)
	{
		if (!IsGyroEnabled)
			return false;

		if (Input.GetJoyAccelerometer(Runtime.Instance.ActiveController).Y > DownShakeDeadzone)
			return false;

		return -Input.GetJoyGyroscope(Runtime.Instance.ActiveController).X > DownShakeSensitivity * multiplier;
	}

	/// <summary> A basic shake in any direction. </summary>
	public bool IsShakeRegistered(float multiplier = 1f)
	{
		if (!IsGyroEnabled)
			return false;

		return Input.GetJoyAccelerometer(Runtime.Instance.ActiveController).Length() > DownShakeSensitivity * multiplier;
	}

	private readonly float SideShakeSensitivity = 2f;
	private readonly float SideShakeLimit = 6f;
	/// <summary> A basic flick sideways. </summary>
	public bool IsSideFlickRegistered()
	{
		if (!IsGyroEnabled)
			return false;

		float accel = Input.GetJoyAccelerometer(Runtime.Instance.ActiveController).X;
		float gyro = Input.GetJoyGyroscope(Runtime.Instance.ActiveController).Z;
		if (Mathf.Abs(accel) > SideShakeLimit)
			return false;

		if (Mathf.Sign(gyro) == Mathf.Sign(accel)) // Recentering
			return false;

		return Mathf.Abs(gyro) > SideShakeSensitivity;
	}

	public bool IsBackTiltActive()
	{
		if (!IsGyroEnabled)
			return false;

		return GyroInput.Y < -DeadZone;
	}

	private void UpdateJumpBuffer()
	{
		if (Player.IsLockoutDisablingAction(LockoutResource.ActionFlags.JumpButton))
		{
			// Allow player to jump out of certain lockouts (i.e. DriftLockout)
			if (Player.ActiveLockoutData.resetFlags.HasFlag(LockoutResource.ResetFlags.OnJump))
				UpdateJumpBuffer();
			else
				ResetJumpBuffer();

			return;
		}

		if (Input.IsActionJustPressed("button_jump"))
		{
			jumpBuffer = InputBufferLength;
			return;
		}

		jumpBuffer = Mathf.MoveToward(jumpBuffer, 0, PhysicsManager.physicsDelta);
	}

	private void UpdateActionBuffer()
	{
		if (Player.IsLockoutDisablingAction(LockoutResource.ActionFlags.ActionButton))
		{
			if (Player.ActiveLockoutData.resetFlags.HasFlag(LockoutResource.ResetFlags.OnAction))
				UpdateActionBuffer();
			else
				ResetActionBuffer();

			return;
		}

		if (Input.IsActionJustPressed("button_action"))
		{
			actionBuffer = InputBufferLength;
			return;
		}

		actionBuffer = Mathf.MoveToward(actionBuffer, 0, PhysicsManager.physicsDelta);
	}

	private void UpdateAttackBuffer()
	{
		if (Player.IsLockoutDisablingAction(LockoutResource.ActionFlags.Attacks))
		{
			if (Player.ActiveLockoutData.resetFlags.HasFlag(LockoutResource.ResetFlags.OnAttack))
				UpdateAttackBuffer();
			else
				ResetAttackBuffer();

			return;
		}

		if (Input.IsActionJustPressed("button_attack"))
		{
			attackBuffer = InputBufferLength;
			return;
		}

		if (IsDownShakeRegistered())
		{
			attackBuffer = InputBufferLength;
			return;
		}

		attackBuffer = Mathf.MoveToward(attackBuffer, 0, PhysicsManager.physicsDelta);
	}

	private void UpdateStepBuffer()
	{
		if (StepAxis == 0)
		{
			StepAxis = Input.GetAxis("button_step_right", "button_step_left");
		}
		else
		{
			if (Input.IsActionJustPressed("button_step_right"))
				StepAxis = 1;
			else if (Input.IsActionJustPressed("button_step_left"))
				StepAxis = -1;
			else if (!Input.IsActionPressed("button_step_right") && !Input.IsActionPressed("button_step_left"))
				StepAxis = 0;
		}

		if (Player.IsLockoutDisablingAction(LockoutResource.ActionFlags.Sidestep))
		{
			ResetStepBuffer();
			return;
		}

		if (StepAxis != 0)
		{
			if (Input.IsActionJustPressed("button_step_right"))
			{
				stepBuffer = InputBufferLength;
				stepDirection = -1;
				return;
			}

			if (Input.IsActionJustPressed("button_step_left"))
			{
				stepBuffer = InputBufferLength;
				stepDirection = 1;
				return;
			}
		}

		stepBuffer = Mathf.MoveToward(stepBuffer, 0, PhysicsManager.physicsDelta);
	}

	private void UpdateLightDashBuffer()
	{
		if (Player.IsLockoutDisablingAction(LockoutResource.ActionFlags.Lightdash))
		{
			ResetLightDashBuffer();
			return;
		}

		if (Input.IsActionJustPressed("button_light_dash"))
		{
			lightDashBuffer = InputBufferLength;
			return;
		}

		lightDashBuffer = Mathf.MoveToward(lightDashBuffer, 0, PhysicsManager.physicsDelta);
	}

	public bool IsBrakeHeld()
	{
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump))
			return Input.IsActionPressed("button_action");

		return Input.IsActionPressed("button_brake");
	}

	public bool IsBrakePressed()
	{
		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump))
			return Input.IsActionJustPressed("button_action");

		return Input.IsActionJustPressed("button_brake");
	}

	/// <summary> Returns the angle between the player's input angle and movementAngle. </summary>
	public float GetTargetMovementAngle() => CalculateLockoutForwardAngle(GetTargetInputAngle());

	public float CalculatePathControlAmount()
	{
		if (SaveManager.ActiveSkillRing.IsAutorunActive)
			return 0; // Don't use path influence during autorun

		return Player.PathTurnInfluence;
	}

	/// <summary> Returns whether the player is currently in strafing mode. </summary>
	public bool IsStrafeModeActive => Player.Skills.IsSpeedBreakActive ||
			SaveManager.ActiveSkillRing.IsAutorunActive ||
			(Player.IsLockoutActive && Player.ActiveLockoutData.movementMode == LockoutResource.MovementModes.Strafe);

	/// <summary> Returns the automaticly calculated input angle based on the game's settings and skills. </summary>
	public float GetTargetInputAngle()
	{
		if (SaveManager.ActiveSkillRing.IsAutorunActive && InputAxis.IsZeroApprox())
			return Player.PathFollower.ForwardAngle;

		float nonZeroInput = NonZeroInputAxis.Rotated(-XformAngle).AngleTo(Vector2.Up);

		if (Player.IsLockoutActive && Player.ActiveLockoutData.allowGlobalForward)
		{
			bool isHoldingBackwards = IsHoldingDirection(nonZeroInput, Player.ActiveLockoutData.movementAngle + Player.PathFollower.BackAngle, Mathf.Pi * 0.2f);
			Vector2 referenceInput = isHoldingBackwards ? Vector2.Down : Vector2.Up;

			if (!InputAxis.IsZeroApprox() && NonZeroInputAxis.AngleTo(referenceInput) < Mathf.Pi * 0.2f &&
			Player.Stats.GroundSettings.GetSpeedRatioClamped(Player.MoveSpeed) > 0.2f)
			{
				// Allow moving forward by just holding up when moving quickly along certain lockouts
				return isHoldingBackwards ? Player.PathFollower.BackAngle : Player.PathFollower.ForwardAngle;
			}
		}

		return nonZeroInput;
	}

	private float CalculateLockoutForwardAngle(float inputAngle)
	{
		LockoutResource resource = Player.ActiveLockoutData;
		if (Player.Skills.IsSpeedBreakCharging)
		{
			if (Player.IsMovingBackwardFreeRoam)
				return Player.PathFollower.ForwardAngle + Mathf.Pi;

			return Player.PathFollower.ForwardAngle;
		}

		if (Player.IsLockoutOverridingMovementAngle)
		{
			if (Player.ActiveLockoutData.movementMode == LockoutResource.MovementModes.Strafe)
				return GetStrafeAngle();

			float forwardAngle = Player.ActiveLockoutData.movementAngle;
			switch (resource.spaceMode)
			{
				case LockoutResource.SpaceModes.Local:
					forwardAngle += Player.MovementAngle;
					break;
				case LockoutResource.SpaceModes.Camera:
					forwardAngle += XformAngle;
					break;
				case LockoutResource.SpaceModes.PathFollower:
					forwardAngle += Player.PathFollower.ForwardAngle;
					break;
			}

			if (resource.allowReversing)
			{
				float backwardsAngle = forwardAngle + Mathf.Pi;
				if (IsMovingBackwardsInLockout(inputAngle, backwardsAngle))
					return backwardsAngle;
			}

			return forwardAngle;
		}

		if (Player.Skills.IsSpeedBreakActive)
			return GetStrafeAngle();

		if (SaveManager.ActiveSkillRing.IsAutorunActive)
			return GetStrafeAngle();

		if (Mathf.IsZeroApprox(GetInputStrength()))
			return Player.MovementAngle;

		return inputAngle;
	}

	private bool IsMovingBackwardsInLockout(float inputAngle, float backwardsAngle)
	{
		if (Mathf.IsZeroApprox(Player.MoveSpeed) && IsHoldingDirection(inputAngle, backwardsAngle))
			return true;

		if (!Mathf.IsZeroApprox(Player.MoveSpeed))
		{
			if (Player.IsMovingBackward)
				return true;

			if (Player.IsMovingBackwardFreeRoam)
				return true;
		}

		return false;
	}

	private float GetStrafeAngle()
	{
		CameraSettingsResource.ControlModeEnum controlMode = Player.Camera.ActiveSettings.controlMode;
		Vector2 inputs = InputAxis;
		float baseAngle = Player.PathFollower.ForwardAngle;

		if (controlMode == CameraSettingsResource.ControlModeEnum.Sidescrolling)
		{
			int rotationDirection = Mathf.Sign(ExtensionMethods.SignedDeltaAngleRad(baseAngle, XformAngle));
			inputs = inputs.Rotated(rotationDirection * Mathf.Pi * .5f);
		}
		else if (controlMode == CameraSettingsResource.ControlModeEnum.Reverse)
		{
			// Transform inputs based on the control mode
			inputs *= -1;
		}
		else if (controlMode == CameraSettingsResource.ControlModeEnum.Auto)
		{
			// Transform inputs based on camera angle
			int sign = Mathf.Sign(ExtensionMethods.DotAngle(baseAngle, XformAngle));
			inputs *= sign >= 0 ? 1 : -1;
		}

		float strafeAngle = TurningDampingRange;
		if (Player.IsLockoutActive && Player.ActiveLockoutData.overrideSpeed)
		{
			float t = Player.Stats.GroundSettings.GetSpeedRatioClamped(Player.MoveSpeed);
			strafeAngle = Mathf.Lerp(AutorunLockoutTurningDampingRange, TurningDampingRange, t);
		}

		strafeAngle *= inputs.X;

		if (Player.IsMovingBackwardFreeRoam || Player.IsMovingBackward)
		{
			strafeAngle *= -1;
			baseAngle = Player.PathFollower.BackAngle;
		}

		if (!SaveManager.ActiveSkillRing.IsAutorunActive || Player.IsBackflipping)
			baseAngle -= strafeAngle;

		return baseAngle;
	}

	/// <summary> Checks whether the player is holding a particular direction. </summary>
	public bool IsHoldingDirection(float inputAngle, float referenceAngle, float maximumDelta = MaximumHoldDelta)
	{
		float deltaAngle = ExtensionMethods.DeltaAngleRad(inputAngle, referenceAngle);
		return deltaAngle <= maximumDelta;
	}

	/// <summary> Returns how far the player's input is from the reference angle, normalized to MinBackStepAngle. </summary>
	public float GetHoldingDistance(float inputAngle, float referenceAngle)
	{
		float deltaAngle = ExtensionMethods.DeltaAngleRad(referenceAngle, inputAngle);
		return deltaAngle / MinBackStepAngle;
	}

	/// <summary>
	/// Remaps controller inputs when holding forward to provide more analog detail.
	/// </summary>
	public float ImproveAnalogPrecision(float inputAngle, float referenceAngle)
	{
		if (!Runtime.Instance.IsUsingController)
			return inputAngle;

		float deltaAngle = ExtensionMethods.SignedDeltaAngleRad(inputAngle, referenceAngle);
		if (Mathf.Abs(deltaAngle) < TurningDeadzone)
			inputAngle = referenceAngle;
		else if (Mathf.Abs(deltaAngle) < TurningDampingRange)
			inputAngle -= deltaAngle * .5f;

		return inputAngle;
	}

	/// <summary>
	/// Returns true if the player is trying to recenter themselves.
	/// </summary>
	public bool IsRecentering(float movementDeltaAngle, float inputDeltaAngle)
	{
		return Mathf.Sign(movementDeltaAngle) != Mathf.Sign(inputDeltaAngle) ||
			Mathf.Abs(movementDeltaAngle) > Mathf.Abs(inputDeltaAngle);
	}
}
