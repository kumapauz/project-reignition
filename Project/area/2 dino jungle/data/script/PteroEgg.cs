using Godot;
using Project.Core;

namespace Project.Gameplay.Objects;

/// <summary> For that one act in Dinosaur Jungle. Follows the player until damage is taken. </summary>
public partial class PteroEgg : Area3D
{
	/// <summary> Emitted when the egg is back in the nest. </summary>
	[Signal]
	public delegate void ReturnedEventHandler();

	[Export(PropertyHint.NodePathValidTypes, "Node3D")]
	private NodePath root;
	private Node3D Root { get; set; }
	[Export(PropertyHint.NodePathValidTypes, "AnimationPlayer")]
	private NodePath animator;
	private AnimationPlayer Animator { get; set; }

	public bool IsHeld => PteroEggManager.heldEggs.Contains(this);
	/// <summary> Is this egg on its way back to its nest? </summary>
	public bool IsReturningToNest { get; private set; }
	/// <summary> Has the egg been returned to the nest successfully? </summary>
	public bool IsReturnedToNest { get; private set; }
	/// <summary> Has this egg been saved at a checkpoint? </summary>
	public bool IgnoreRespawn { get; set; }

	/// <summary> The index of the egg's pattern, starting from 1. </summary>
	private int eggPattern;
	/// <summary> Which objective icon the egg is responsible for. </summary>
	private int eggIndex;

	private Vector3 followVelocity;
	private readonly float FollowDistance = 2f;
	private readonly float FollowSmoothing = 5.0f;
	private readonly float BackflipFollowSmoothing = 2.0f;
	private readonly float FollowRotationAmount = Mathf.DegToRad(60f);

	private float returnTravelRatio;
	private SpawnData spawnData;
	private LaunchSettings returnArc; // The path to follow when returning to the nest
	private PlayerController Player => StageSettings.Player;

	public override void _Ready()
	{
		Root = GetNodeOrNull<Node3D>(root);
		Animator = GetNodeOrNull<AnimationPlayer>(animator);
		spawnData = new(GetParent(), Transform);
		StageSettings.Instance.Respawned += Respawn;
		StageSettings.Instance.LevelStarted += InitializeHud;
	}

	public override void _PhysicsProcess(double _)
	{
		if (IsReturnedToNest) return;

		if (IsHeld)
			UpdateHeldPosition();
		else if (IsReturningToNest)
			ReturnToNest();
	}

	private void InitializeHud()
		=> HeadsUpDisplay.Instance.PlayObjectiveAnimation("dino-egg", eggIndex);

	/// <summary> Moves the eggs to circle around the back half of the player. </summary>
	private void UpdateHeldPosition()
	{
		int totalEggCount = PteroEggManager.heldEggs.Count;

		float smoothing = Player.IsBackflipping ? BackflipFollowSmoothing : FollowSmoothing; // Extra snappy when moving backwards
		Vector3 followDirection = Player.PathFollower.Back();
		if (totalEggCount > 1)
		{
			float rotationFactor = PteroEggManager.heldEggs.IndexOf(this) / (totalEggCount - 1f);
			rotationFactor = Mathf.Lerp(-1f, 1f, rotationFactor);
			followDirection = followDirection.Rotated(Vector3.Up, FollowRotationAmount * rotationFactor);
		}

		Vector3 targetPosition = Player.GlobalPosition + followDirection * FollowDistance;

		// Update position to trail player
		GlobalPosition = GlobalPosition.SmoothDamp(targetPosition, ref followVelocity, smoothing * PhysicsManager.physicsDelta);
	}

	private void ReturnToNest()
	{
		if (Mathf.IsZeroApprox(returnTravelRatio))
			Animator.Play("returned", .2f);

		returnTravelRatio = Mathf.MoveToward(returnTravelRatio, 1f, PhysicsManager.physicsDelta);
		GlobalPosition = returnArc.InterpolatePositionRatio(returnTravelRatio);

		if (Mathf.IsEqualApprox(returnTravelRatio, 1))
		{
			Animator.Play("sleep", .1f);
			IsReturnedToNest = true;
			IsReturningToNest = false;
			EmitSignal(SignalName.Returned);
		}
	}

	private void SaveNestStatus() => IgnoreRespawn = IsReturnedToNest;

	// Called when the player takes damage or respawns
	public void Frighten()
	{
		Animator.Play("frighten");
		HeadsUpDisplay.Instance.PlayObjectiveAnimation("dino-egg-loss", eggIndex);
	}

	private void Respawn()
	{
		if (IgnoreRespawn) return; // Don't respawn if we're already at the nest. Don't force the player to redo stuff they already did.

		if (IsHeld)
			PteroEggManager.heldEggs.Remove(this);

		IsReturnedToNest = false;
		followVelocity = Vector3.Zero;
		spawnData.Respawn(this);
		Animator.Play("idle");
		HeadsUpDisplay.Instance.PlayObjectiveAnimation("dino-egg", eggIndex);
	}

	public void SetType(int pattern, int index, Node3D model) // Adds the egg model as a child
	{
		eggPattern = pattern;
		eggIndex = index;
		Root.AddChild(model);
		model.GlobalTransform = GlobalTransform;
		model.Position += Vector3.Up * .5f;
		model.ResetPhysicsInterpolation();
	}

	public void OnEntered(Area3D a)
	{
		if (!a.IsInGroup("player detection")) return;
		if (IsReturningToNest || IsReturnedToNest) return;
		if (IsHeld) return;

		PteroEggManager.heldEggs.Add(this);
		HeadsUpDisplay.Instance.PlayObjectiveAnimation("dino-egg" + eggPattern, eggIndex);
		Animator.Play("pick-up", .1f);
	}

	public void ReturnToNest(PteroNest nest)
	{
		if (IsHeld)
		{
			PteroEggManager.heldEggs.Remove(this);
			HeadsUpDisplay.Instance.PlayObjectiveAnimation("dino-egg-return", eggIndex);
		}

		IsReturningToNest = true;

		returnTravelRatio = 0f;
		returnArc = LaunchSettings.Create(GlobalPosition, nest.GlobalPosition, 5f, true);
	}
}