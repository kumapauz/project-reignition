using Godot;
using Project.Core;
using Project.Gameplay.Triggers;

namespace Project.Gameplay.Objects;

/// <summary> Mash the action button for maximum speed. </summary>
[Tool]
public partial class FlyingPot : Node3D
{
	[Signal] public delegate void PotEnteredEventHandler();
	[Signal] public delegate void PotExitedEventHandler();

	[Export] public Vector2 travelBounds;
	[Export] public float boundOffset;
	[Export] private CameraSettingsResource customCameraSettings;
	[Export] private Vector2 initialLocalPosition;

	[ExportGroup("Components")]
	[Export] private Node3D root;
	public Node3D Root => root;
	[Export] private Area3D lockonArea;
	[Export] private CollisionShape3D environmentCollider;
	[Export] private AnimationTree animationTree;
	[Export] private AnimationPlayer interactionAnimator;
	[Export] private AudioStreamPlayer enterSFX;
	[Export] private AudioStreamPlayer exitSFX;
	private CameraTrigger cameraTrigger;

	private readonly string EnterTrigger = "parameters/enter_trigger/request";
	private readonly string FallTransition = "parameters/fall_transition/transition_request";
	private readonly string FlapTrigger = "parameters/flap_trigger/request";
	private readonly string FlapSpeed = "parameters/flap_speed/scale";
	private readonly string EnabledState = "enabled";
	private readonly string DisabledState = "disabled";

	private bool isSleeping;
	private bool isOnFloor;
	private bool interactingWithPlayer;
	private bool isPlayerJumpingIntoPot;
	private float angle;
	public float Angle => angle;
	public float Velocity { get; set; }
	private Vector2 localPosition;

	private const float RotationSpeed = .1f;
	private const float MaxGravity = -10.0f;

	public PlayerController Player => StageSettings.Player;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		animationTree.Active = true;
		StageSettings.Instance.Respawned += Respawn;
		Respawn();

		if (customCameraSettings == null)
			return;

		// Create the camera trigger
		cameraTrigger = new CameraTrigger()
		{
			transitionTime = .2f, // Default to .2 seconds for transitions
			settings = customCameraSettings
		};
		AddChild(cameraTrigger);
	}

	private void Respawn()
	{
		isSleeping = true;
		CallDeferred(MethodName.ResetPosition);

		interactionAnimator.Play("RESET");
		lockonArea.SetDeferred("monitorable", true);
		animationTree.Set(FallTransition, DisabledState);
	}

	public override void _PhysicsProcess(double _)
	{
		if (Engine.IsEditorHint())
		{
			initialLocalPosition = new Vector2(Mathf.Clamp(initialLocalPosition.X, -travelBounds.X + boundOffset, travelBounds.X + boundOffset),
				Mathf.Clamp(initialLocalPosition.Y, -travelBounds.Y, travelBounds.Y));
			root.Position = new Vector3(initialLocalPosition.X, initialLocalPosition.Y, 0);
			return;
		}

		if (Player.IsFlyingPotActive)
			return;

		if (!lockonArea.Monitorable && Player.VerticalSpeed < 0f) // Re-enable lockon
		{
			GD.Print("Lockon enabled;");
			lockonArea.SetDeferred("monitorable", true);
		}

		if (interactingWithPlayer && !Player.IsOnGround && Player.VerticalSpeed <= 0f)
			StartJump();

		if (isSleeping) return;

		UpdateAngle(0);
		ApplyMovement();
	}

	private void ResetPosition()
	{
		angle = 0f;
		Velocity = 0f;
		localPosition = initialLocalPosition;
		ApplyMovement();
	}

	public void Flap() => animationTree.Set(FlapTrigger, (uint)AnimationNodeOneShot.OneShotRequest.Fire);
	public void UpdateFlap(float speed) => animationTree.Set(FlapSpeed, speed);
	public void UpdateAngle(float targetAngle) => angle = Mathf.Lerp(angle, targetAngle, RotationSpeed);

	private void StartJump()
	{
		if (isPlayerJumpingIntoPot)
			return;

		isPlayerJumpingIntoPot = true;
		environmentCollider.Disabled = true;

		float jumpHeight = GlobalPosition.Y + 1 - Player.GlobalPosition.Y;
		jumpHeight = Mathf.Clamp(jumpHeight * 2f, 0.5f, 2f);
		LaunchSettings settings = LaunchSettings.Create(Player.GlobalPosition, root.GlobalPosition, jumpHeight, false);
		settings.AllowJumpDash = false;
		Player.StartLauncher(settings);
		Player.Animator.AutoJumpAnimation();
		Player.Effect.PlayActionSFX(Player.Effect.JumpSfx);

		lockonArea.SetDeferred("monitorable", false);

		Player.Skills.IsSpeedBreakEnabled = false;
		Player.LaunchFinished += OnEnteredPot;

		if (cameraTrigger == null)
			return;

		// Sync viewAngle to current flying pot's rotation
		cameraTrigger.settings.yawAngle = ExtensionMethods.ModAngle(GlobalRotation.Y + Mathf.Pi);
		cameraTrigger.Activate();
	}

	private void OnEnteredPot()
	{
		isPlayerJumpingIntoPot = false;
		enterSFX.Play();
		animationTree.Set(EnterTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		Player.StartFlyingPot(this);
		Player.LaunchFinished -= OnEnteredPot;
		EmitSignal(SignalName.PotEntered);
	}

	public void PlayExitFX()
	{
		exitSFX.Play();
		animationTree.Set(EnterTrigger, (int)AnimationNodeOneShot.OneShotRequest.Fire);
		cameraTrigger?.Deactivate();
		EmitSignal(SignalName.PotExited);
	}

	public void Shatter()
	{
		interactionAnimator.Play("shatter");
		cameraTrigger?.Deactivate();
	}

	public void ApplyMovement()
	{
		if (isPlayerJumpingIntoPot) // Don't move when player is jumping into the pot
			return;

		if (Velocity > 0)
			localPosition += Vector2.Down.Rotated(-Angle) * Velocity * PhysicsManager.physicsDelta;
		else
			localPosition += Vector2.Down * Velocity * PhysicsManager.physicsDelta;

		localPosition.X = Mathf.Clamp(localPosition.X, -travelBounds.X + boundOffset, travelBounds.X + boundOffset);
		localPosition.Y = Mathf.Clamp(localPosition.Y, 0f, travelBounds.Y);
		CheckFloor();

		if (!Mathf.IsZeroApprox(localPosition.Y) && !isOnFloor && !isPlayerJumpingIntoPot) // Fall
		{
			// Update velocity
			Velocity -= Runtime.Gravity * PhysicsManager.physicsDelta; // Floaty fall
			if (Velocity < MaxGravity)
				Velocity = MaxGravity;

			if (Velocity < 0)
				animationTree.Set(FallTransition, EnabledState);
		}
		else if (Velocity != 0) // Return to idle flapping
		{
			Velocity = 0;
			animationTree.Set(FallTransition, DisabledState);
		}

		root.Position = new Vector3(localPosition.X, localPosition.Y, 0);
		root.Rotation = Vector3.Forward * Angle;

		if (lockonArea.Monitorable && !interactingWithPlayer) // Update sleeping status
		{
			if ((Mathf.IsZeroApprox(localPosition.Y) || isOnFloor) && Mathf.IsZeroApprox(Angle))
				isSleeping = true;
		}
	}

	private void CheckFloor()
	{
		if (Velocity > 0)
		{
			// Moving up
			isOnFloor = false;
			return;
		}

		float checkLength = 1f + Velocity * PhysicsManager.physicsDelta;
		DebugManager.DrawRay(Root.GlobalPosition, Vector3.Down * checkLength, Colors.White);
		isOnFloor = this.CastRay(Root.GlobalPosition, Vector3.Down * checkLength, Runtime.Instance.environmentMask);
	}

	private void Activate()
	{
		GD.Print("Attempting Activation.");
		isSleeping = false;

		if (isPlayerJumpingIntoPot)
			return;

		if (!Player.IsFlyingPotActive && !Player.IsOnGround)
			StartJump();
	}

	public void OnEntered(Area3D a)
	{
		if (!a.IsInGroup("player detection"))
			return;

		interactingWithPlayer = true;
		Activate();
	}

	public void OnExited(Area3D a)
	{
		if (!a.IsInGroup("player detection"))
			return;

		if (isPlayerJumpingIntoPot)
			return;

		interactingWithPlayer = false;
		environmentCollider.SetDeferred("disabled", false);
	}
}