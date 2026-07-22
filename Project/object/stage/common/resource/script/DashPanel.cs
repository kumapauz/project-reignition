using Godot;

namespace Project.Gameplay.Objects;

public partial class DashPanel : Area3D
{
	[Signal]
	public delegate void ActivatedEventHandler();

	[Export(PropertyHint.Range, "0, 2")]
	private float speedRatio;
	[Export]
	private float length; // How long for the boost pad to last
	private bool isQueued; // For when the player collides with the dash panel from the air
	[Export]
	private bool alignToPath; // Forces the player to stay aligned to the path. Useful when a dash panel is right before a corner.

	[Export(PropertyHint.NodePathValidTypes, "AudioStreamPlayer3D")]
	private NodePath sfxPlayer;
	private AudioStreamPlayer3D SfxPlayer { get; set; }
	private PlayerController Player => StageSettings.Player;

	public override void _Ready() => SfxPlayer = GetNodeOrNull<AudioStreamPlayer3D>(sfxPlayer);

	public override void _PhysicsProcess(double _)
	{
		if (!isQueued) return;

		Activate();
	}

	public void OnEntered(Area3D a)
	{
		if (!a.IsInGroup("player detection"))
			return;

		isQueued = true;
		Activate();
	}

	public void OnExited(Area3D a)
	{
		if (!a.IsInGroup("player detection"))
			return;

		isQueued = false;
	}

	private void Activate()
	{
		if (!Player.IsOnGround) return; // Can't activate when player is in the air

		SfxPlayer.Play();
		isQueued = false;

		// Only apply speed boost when player is moving slow. Don't slow them down!
		if (Player.Stats.GroundSettings.GetSpeedRatio(Player.MoveSpeed) < speedRatio)
		{
			Player.MoveSpeed = Player.Stats.GroundSettings.Speed * speedRatio;
			Player.Effect.PlayVoice("dash panel");
		}

		// Recalculate lockout length to be consistent across different speeds
		float normalizedSpeedRatio = Player.Stats.GroundSettings.GetSpeedRatio(Player.MoveSpeed);
		float normalizedLockoutLength = length * speedRatio / normalizedSpeedRatio;

		LockoutResource lockout = new()
		{
			movementMode = LockoutResource.MovementModes.Replace,
			spaceMode = LockoutResource.SpaceModes.Local,
			movementAngle = 0,
			speedRatio = normalizedSpeedRatio,
			disableActionFlags = LockoutResource.ActionFlags.ActionButton + (int)LockoutResource.ActionFlags.Backflip,
			resetFlags = LockoutResource.ResetFlags.OnJump + (int)LockoutResource.ResetFlags.OnKnockback,
			overrideSpeed = true,
			tractionMultiplier = -1,
			frictionMultiplier = 0,
			length = normalizedLockoutLength,
			priority = -1, // Not using priority
		};

		if (alignToPath)
		{
			lockout.movementAngle = 0f;
			lockout.spaceMode = LockoutResource.SpaceModes.PathFollower;
			Player.MovementAngle = Player.PathFollower.ForwardAngle;
			lockout.recenterPlayer = Player.IsLockoutActive && Player.ActiveLockoutData.recenterPlayer;
		}
		else
		{
			Player.MovementAngle = ExtensionMethods.CalculateForwardAngle(this.Forward(), Player.PathFollower.Up());
		}

		Player.Animator.StopBrake();
		Player.AddLockoutData(lockout);
		EmitSignal(SignalName.Activated);
	}
}