using Godot;
using Project.Core;

namespace Project.Gameplay;

public partial class QuickStepState : PlayerState
{
	[Export] private PlayerState idleState;
	[Export] private PlayerState runState;
	[Export] private PlayerState fallState;
	[Export] private PlayerState backflipState;
	[Export] private PlayerState slideState;
	[Export] private PlayerState jumpState;

	[Export] private Curve movementCurve;

	public bool IsSteppingRight { get; set; }

	private bool isQuickSlide;
	private bool isQuickSlideActive;
	private float currentStepLength;
	private readonly float MaxQuickSlideSamplePosition = 0.5f;
	private readonly float StepLength = 0.3f;
	private readonly float InterruptLength = 0.1f;
	private readonly float FallPreventionLength = 0.1f;
	private int stepDirection;

	public override void EnterState()
	{
		isQuickSlide = SaveManager.ActiveSkillRing.GetAugmentIndex(SkillKey.QuickStep) == 1;
		isQuickSlideActive = isQuickSlide && Player.Controller.StepAxis != 0;
		currentStepLength = 0.0f;

		stepDirection = 1;
		if (SaveManager.ActiveSkillRing.IsFreeRoamActive &&
			(!Player.IsLockoutActive || !Player.IsLockoutOverridingMovementAngle))
		{
			stepDirection = Mathf.Sign(ExtensionMethods.DotAngle(Player.MovementAngle, Player.Controller.XformAngle));
			if (stepDirection == 0)
				stepDirection = 1;
			else if (stepDirection == -1)
				IsSteppingRight = !IsSteppingRight;
		}

		if (isQuickSlide)
			Player.Animator.StartQuickSlide(IsSteppingRight);
		else
			Player.Animator.StartQuickStep(IsSteppingRight);

		Player.Effect.PlayQuickStepFX(IsSteppingRight);
		Player.Effect.StartDust();
	}

	public override void ExitState()
	{
		if (isQuickSlide)
			Player.Animator.StopQuickSlide(IsSteppingRight);

		Player.Effect.StopDust();
	}

	public override PlayerState ProcessPhysics()
	{
		if (!Player.IsQuickStepValid) // Exit quick step state
			return runState;

		float currentSpeed = CalculateSpeed();
		if (!IsSteppingRight)
			currentSpeed *= -1;

		Player.Velocity = Player.PathFollower.Right() * stepDirection * currentSpeed;
		Player.MoveAndSlide();

		ProcessMoveSpeed();
		ProcessTurning();
		Player.AddSlopeSpeed();
		Player.ApplyMovement();
		Player.CheckGround();
		Player.CheckWall();
		if (Player.CheckCeiling())
			return null;

		if (!Player.IsOnGround)
		{
			Player.Velocity = Player.PathFollower.Right() * currentSpeed;
			Player.MoveAndSlide(); // Force player off the ledge
			return fallState;
		}

		if (!Player.Skills.IsSpeedBreakActive && Mathf.IsZeroApprox(Player.MoveSpeed))
			return idleState;

		if (currentStepLength <= FallPreventionLength)
			return null;

		// Prevent player from flying off the ground if they're "close enough" to the grind step ending
		Vector3 groundCheckPosition = Player.CenterPosition;
		groundCheckPosition += Player.PathFollower.Right() * (currentSpeed * PhysicsManager.physicsDelta + Mathf.Sign(currentSpeed) * Player.CollisionSize.X);
		RaycastHit groundCheck = Player.CastRay(groundCheckPosition, -Player.UpDirection * Player.CollisionSize.Y * 2f, Runtime.Instance.environmentMask);
		DebugManager.DrawRay(groundCheckPosition, -Player.UpDirection * Player.CollisionSize.Y * 2f, groundCheck ? Colors.Red : Colors.Pink);
		if (!groundCheck || !groundCheck.collidedObject.IsInGroup("floor"))
			currentStepLength = StepLength;

		if (currentStepLength >= InterruptLength)
		{
			if (Player.Controller.IsStepBufferActive)
			{
				Player.StartQuickStep(Player.Controller.StepDirection < 0);
				Player.Controller.ResetStepBuffer();
				EnterState(); // Restart quick steps
				return null;
			}
			else if (Player.Controller.IsJumpBufferActive)
			{
				Player.Controller.ResetJumpBuffer();

				if (Player.IsBackflipInputValid())
					return backflipState;

				if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump) &&
					!Player.IsLockoutDisablingAction(LockoutResource.ActionFlags.FullJump))
				{
					return slideState;
				}

				return jumpState;
			}

			if (!SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.ChargeJump) &&
				Player.Controller.IsActionBufferActive)
			{
				Player.Controller.ResetActionBuffer();
				return slideState;
			}
		}

		if (currentStepLength >= StepLength && !isQuickSlideActive)
			return runState;

		return null;
	}

	protected override void ProcessMoveSpeed()
	{
		if (!isQuickSlideActive)
			return;

		ProcessAutorunStrafeSpeed();
		Player.Stats.UpdateSlideSpeed(Player.SlopeRatio);

		// Influence speed based on input strength
		float inputAmount = -.5f; // Default to halfway
		float inputStrength = Player.Controller.GetInputStrength();
		float inputAngle = Player.Controller.GetTargetMovementAngle();
		if (Player.Controller.IsHoldingDirection(inputAngle, Player.MovementAngle + Mathf.Pi))
			inputAmount = -(1 + inputStrength) * .5f; // -0.5 to -1
		else if (SaveManager.ActiveSkillRing.IsAutorunActive)
			inputAmount = 0;
		else if (Player.Controller.IsHoldingDirection(inputAngle, Player.MovementAngle))
			inputAmount = -(1 - inputStrength) * .5f; // 0 to -0.5
		Player.MoveSpeed = Player.Stats.SlideSettings.UpdateSlide(Player.MoveSpeed, inputAmount);
	}

	private float CalculateSpeed()
	{
		currentStepLength += PhysicsManager.physicsDelta;

		if (isQuickSlideActive)
		{
			int axisInput = -Mathf.Sign(Player.Controller.StepAxis) * stepDirection;
			isQuickSlideActive = (axisInput > 0 && IsSteppingRight) || (axisInput < 0 && !IsSteppingRight);
			currentStepLength = Mathf.Min(currentStepLength, StepLength * MaxQuickSlideSamplePosition);
		}

		return -movementCurve.Sample(Mathf.Clamp(currentStepLength / StepLength, 0f, 1f));
	}

	protected override void ProcessTurning() => Player.MovementAngle = stepDirection == 1 ? Player.PathFollower.ForwardAngle : Player.PathFollower.BackAngle;
}
