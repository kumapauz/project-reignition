using Godot;

namespace Project.Core;

/// <summary>
/// Handles transitions and scene changes.
/// The transition will play halfway, at which point a signal will be emitted, allowing for loading.
/// Call <see cref="FinishTransition"/> to complete the transition.
/// </summary>
public partial class TransitionManager : Node
{
	public static TransitionManager Instance;
	/// <summary> Path to the main menu scene. </summary>
	public const string MenuScenePath = "res://interface/menu/Menu.tscn";
	/// <summary> Path to story events. </summary>
	public const string EventScenePath = "res://video/event/scene/";
	public const string OptionsScenePath = "res://interface/menu/options/Options.tscn";
	public const string PartyScenePath = "res://party/scene/party menu/PartyMenu.tscn";
	public const string SpecialBookScenePath = "res://interface/menu/special book/SpecialBook.tscn";
	public const string TimeAttackScenePath = "res://interface/menu/time attack/TimeAttack.tscn";
	public const string TimeAttackResultsPath = "res://interface/menu/time attack/TimeAttackResults.tscn";

	public bool IsReloadingScene { get; private set; }

	[Export] private Label loadLabel;
	[Export] private ColorRect fade;
	[Export] private AnimationPlayer animator;
	[Export] private AnimationPlayer loadingAnimator;
	[Export] private Control missionDescriptionRoot;
	[Export] private Label missionDescriptionLabel;

	public override void _EnterTree() => Instance = this;

	#region Transition Types
	// Simple cut transition. During loading, everything will freeze temporarily.
	private void StartCut() => EmitSignal(SignalName.TransitionProcess);
	private void StartFade()
	{
		if (IsTransitionActive)
		{
			GD.PushWarning("Transition is already active!");
			return;
		}

		if (CurrentTransitionData.loadAsynchronously)
			loadingAnimator.Play("show");

		IsTransitionActive = true;
		fade.Color = CurrentTransitionData.color;
		animator.Play("fade");

		if (CurrentTransitionData.inSpeed == 0)
		{
			animator.Seek(animator.CurrentAnimationLength, true);
			CallDeferred(MethodName.EmitSignal, SignalName.TransitionProcess);
		}
		else
		{
			animator.SpeedScale = 1.0f / CurrentTransitionData.inSpeed;
			animator.Connect(AnimationPlayer.SignalName.AnimationFinished, new(Instance, MethodName.TransitionLoading), (uint)ConnectFlags.OneShot);
		}

		EmitSignal(SignalName.TransitionStarted);
	}

	private void FinishFade()
	{
		if (CurrentTransitionData.loadAsynchronously)
			loadingAnimator.Play("hide");

		animator.PlayBackwards("fade");
		if (Mathf.IsZeroApprox(CurrentTransitionData.outSpeed)) // Cut
			animator.Seek(animator.CurrentAnimationLength, true);
		else
			animator.SpeedScale = 1.0f / CurrentTransitionData.outSpeed;

		if (!animator.IsConnected(AnimationPlayer.SignalName.AnimationFinished, new(Instance, MethodName.TransitionFinished)))
			animator.Connect(AnimationPlayer.SignalName.AnimationFinished, new(Instance, MethodName.TransitionFinished), (uint)ConnectFlags.OneShot);
	}
	#endregion

	private TransitionData CurrentTransitionData { get; set; }
	public static bool IsTransitionActive { get; set; }
	/// <summary> Called when the scene changes. </summary>
	[Signal] public delegate void SceneChangedEventHandler();
	/// <summary> Called whenever a transition is started. </summary>
	[Signal] public delegate void TransitionStartedEventHandler();
	/// <summary> Called in the middle of the transition (when the screen is completely black). </summary>
	[Signal] public delegate void TransitionProcessEventHandler();
	/// <summary> Called when the transition is finished. </summary>
	[Signal] public delegate void TransitionFinishEventHandler();
	private void TransitionLoading(string _) => EmitSignal(SignalName.TransitionProcess);
	private void TransitionFinished(string _)
	{
		IsTransitionActive = false;
		EmitSignal(SignalName.TransitionFinish);
	}

	public static void StartTransition(TransitionData data)
	{
		Instance.animator.Play("RESET"); // Reset animator, just in case
		Instance.animator.Advance(0);
		Instance.UpdateLoadingText(null);

		Instance.CurrentTransitionData = data;
		Instance.missionDescriptionRoot.Visible = data.showMissionDescription;

		if (data.loadAsynchronously) // Start loading immediately
		{
			GD.Print("Async loading started.");
			ResourceLoader.LoadThreadedRequest(Instance.QueuedScene, string.Empty, SaveManager.Config.useQuickLoad);
		}

		if (data.inSpeed == 0 && data.outSpeed == 0)
		{
			Instance.StartCut(); // Cut transition
			return;
		}

		Instance.StartFade();
	}

	public static void FinishTransition()
	{
		Instance.UpdateLoadingText(null);
		Instance.FinishFade();
	}

	/// <summary> The scene to load. Note that the scene only gets applied if queued using QueueSceneChange(). </summary>
	public string QueuedScene { get; set; }
	/// <summary> Queues a scene to load and connects the TransitionProcess signal. Be sure to call StartTransition to actually transition to the scene. </summary>
	public static void QueueSceneChange(string scene)
	{
		Instance.QueuedScene = scene;

		var call = new Callable(Instance, MethodName.ApplySceneChange);
		if (!Instance.IsConnected(SignalName.TransitionProcess, call))
			Instance.Connect(SignalName.TransitionProcess, call, (uint)ConnectFlags.OneShot);
	}

	private float loadTime;
	/// <summary> After this amount of time, the game will attempt to restart loading. </summary>
	private readonly float LoadTimeoutLength = 3f;

	private async void ApplySceneChange()
	{
		SoundManager.instance.CancelDialog(); // Cancel any active dialog
		IsReloadingScene = string.IsNullOrEmpty(QueuedScene);
		if (IsReloadingScene) // Reload the current scene
		{
			GetTree().ReloadCurrentScene();
		}
		else
		{
			if (CurrentTransitionData.loadAsynchronously)
			{
				loadTime = 0f;
				ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(QueuedScene);
				while (status != ResourceLoader.ThreadLoadStatus.Loaded)
				{
					await ToSignal(GetTree().CreateTimer(.1f), SceneTreeTimer.SignalName.Timeout); // Still loading; wait a bit
					status = ResourceLoader.LoadThreadedGetStatus(QueuedScene);
					loadTime += .1f;
					if (loadTime > LoadTimeoutLength && status != ResourceLoader.ThreadLoadStatus.Loaded)
					{
						// Forget async loading
						GD.Print("Infinite Loading Detected. Force loading.");
						GetTree().ChangeSceneToFile(QueuedScene);
						FinishSceneChange();
						return;
					}
				}

				PackedScene scene = ResourceLoader.LoadThreadedGet(QueuedScene) as PackedScene;
				GetTree().ChangeSceneToPacked(scene);
				GD.Print($"Scene loaded in {loadTime} milliseconds.");
				FinishSceneChange();
				return;
			}

			GetTree().ChangeSceneToFile(QueuedScene);
		}

		FinishSceneChange();
	}

	private void FinishSceneChange()
	{
		// Reset time scale and unpause whenever we change scenes
		Engine.TimeScale = 1f;
		GetTree().Paused = false;

		QueuedScene = string.Empty; // Clear queue
		EmitSignal(SignalName.SceneChanged);

		if (!CurrentTransitionData.disableAutoTransition)
			FinishFade();
	}

	private readonly string commonResourcesScenePath = "res://object/CommonResources.tscn";
	private bool isLoadingCommonResources;
	/// <summary>
	/// Attempts to reduce load times by loading common objects before-hand. Called on boot.
	/// </summary>
	public async void LoadCommonResources()
	{
		Error err = ResourceLoader.LoadThreadedRequest(commonResourcesScenePath);
		if (err != Error.Ok)
		{
			GD.PrintErr("Couldn't load common resources!");
			return;
		}

		GD.Print("Loading Common Resources.");
		isLoadingCommonResources = true;

		while (ResourceLoader.LoadThreadedGetStatus(commonResourcesScenePath) == ResourceLoader.ThreadLoadStatus.InProgress) // Still loading
			await ToSignal(GetTree().CreateTimer(.1f), SceneTreeTimer.SignalName.Timeout); // Wait a bit

		GD.Print("Common Resources Loaded.");
		isLoadingCommonResources = false;
	}

	public void UpdateLoadingText(StringName localizationKey, int currentProgress = 0, int maxProgress = 0)
	{
		if (localizationKey == null)
		{
			loadLabel.Text = string.Empty;
			return;
		}

		loadLabel.Text = Tr(localizationKey);
		if (maxProgress != 0)
			loadLabel.Text += $" {currentProgress}/{maxProgress}";
	}

	public void SetMissionDescriptionText(StringName typeKey, StringName descriptionKey)
	{
		string missionText = Tr(descriptionKey);
		if (SaveManager.Config.textLocale.LocaleId != "ja")
			missionText = missionText.Replace('\n', ' ');
		else
			missionText = missionText.Replace("\n", "");

		missionDescriptionLabel.Text = $"{Tr(typeKey)}: {missionText}";
	}
}

public struct TransitionData
{
	// Keep both speeds at 0 to perform simple cut transitions
	public float inSpeed;
	public float outSpeed;
	public Color color;
	public bool loadAsynchronously;
	public bool disableAutoTransition;
	public bool showMissionDescription;
}
