using Godot;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface;

public partial class Countdown : Control
{
	public static bool IsCountdownActive { get; private set; }
	public static Countdown Instance { get; private set; }

	[Signal] public delegate void CountdownStartedEventHandler();
	[Signal] public delegate void CountdownFinishedEventHandler();

	[Export] private Node2D tickParent;
	[Export] private Control[] tickPositions;
	[Export] private AnimationPlayer animator;

	public float CountdownSkipAmount { get; private set; }

	private readonly float DefaultCountdownLength = 4.0f;
	private readonly float ShortCountdownLength = 40.0f / 30.0f;

	public override void _EnterTree() => Instance = this;
	public override void _Ready() => StageSettings.Instance.LevelStarted += StartCountdown;

	public void StartCountdown()
	{
		SoundManager.instance.StartBgm(false); // Start BGM

		if (DebugManager.Instance.SkipCountdown || StageSettings.Instance.Data.DisableCountdown)
		{
			CountdownSkipAmount = DefaultCountdownLength;
			FinishCountdown();
			return;
		}

		if (DebugManager.Instance.HideCountdown)
		{
			animator.Play("mute");
			animator.Advance(0.0);
			Visible = false;
		}

		if (SaveManager.ActiveSkillRing.IsSkillEquipped(SkillKey.QuickStart))
		{
			animator.Play("countdown-short");
			CountdownSkipAmount = DefaultCountdownLength - ShortCountdownLength;
		}
		else
		{
			animator.Play("countdown");
			TweenCountdownTicks();
		}

		IsCountdownActive = true;
		PauseMenu.AllowInputs = false;
		StageSettings.Player.StartCountdown();
		StageSettings.Instance.LevelStarted -= StartCountdown;
		EmitSignal(SignalName.CountdownStarted);
	}

	public void FinishCountdown()
	{
		IsCountdownActive = false;
		PauseMenu.AllowInputs = true;
		SoundManager.instance.AdvanceDialogQueue();
		EmitSignal(SignalName.CountdownFinished);
	}

	// The ring animation is too tedious to animate by hand, so I'm using a tween instead.
	private void TweenCountdownTicks()
	{
		// Tween seems to create a temporary "memory leak" but the garbage collector cleans it up later
		Tween countdownTweener = CreateTween().SetTrans(Tween.TransitionType.Sine).SetParallel(true);

		for (int i = 0; i < tickParent.GetChildCount(); i++)
		{
			Node2D tick = tickParent.GetChild<Node2D>(i);

			float delay = i * (1f / tickParent.GetChildCount()) + .65f;
			Vector2 targetPosition = tick.Position + (tick.Position.Normalized() * 25f);
			countdownTweener.TweenProperty(tick, "position", targetPosition, .1f).SetDelay(delay);
			countdownTweener.TweenProperty(tick, "position", tick.Position, .2f).From(targetPosition).SetDelay(delay + .1f);
			countdownTweener.TweenProperty(tick, "modulate", Colors.Transparent, .25f).SetDelay(delay);

			delay += 1;
			countdownTweener.TweenProperty(tick, "position", targetPosition, .1f).SetDelay(delay);
			countdownTweener.TweenProperty(tick, "position", tick.Position, .2f).From(targetPosition).SetDelay(delay + .1f);
			countdownTweener.TweenProperty(tick, "modulate", Colors.White, .25f).From(Colors.Transparent).SetDelay(delay);

			delay += 1;
			countdownTweener.TweenProperty(tick, "position", targetPosition, .1f).SetDelay(delay);
			countdownTweener.TweenProperty(tick, "position", tick.Position, .2f).From(targetPosition).SetDelay(delay + .1f);
			countdownTweener.TweenProperty(tick, "modulate", Colors.Transparent, .25f).SetDelay(delay);
		}

		tickParent.GlobalPosition = tickPositions[0].GlobalPosition;
		countdownTweener.TweenProperty(tickParent, "global_position", tickPositions[1].GlobalPosition, 0.3f).SetDelay(1.67f);
		countdownTweener.TweenProperty(tickParent, "global_position", tickPositions[2].GlobalPosition, 0.3f).SetDelay(2.67f);
	}
}