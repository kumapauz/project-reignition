using Godot;
using Godot.Collections;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface.Menus;

/// <summary>
/// Plays an event (cutscene) with the correct audio depending on the localization settings
/// </summary>
[Tool]
public partial class EventPlayer : Node
{
	[Signal] public delegate void EventFinishedEventHandler();

	[ExportToolButton("Auto Setup")] public Callable AutoSetupCallable => new(this, MethodName.AutoSetup);

	[ExportGroup("Cutscene Settings")]
	/// <summary> Automatically load the given level when in Adventure Mode. Leave empty to return to the main menu. </summary>
	[Export(PropertyHint.File, "*.tres")] private LevelDataResource adventureLevelAutoload;
	/// <summary> Automatically load the given event when in Adventure Mode. Leave empty to return to the main menu. </summary>
	[Export(PropertyHint.File, "*.tscn")] private string adventureEventAutoload;
	[Export(PropertyHint.File, "*.ogg")] private string englishAudioPath;
	[Export] private string localizationKeyPrefix;
	[Export] private bool isCgCutscene;
	[Export] private bool isNestedCutscene;
	[Export] public Color transitionColor = Colors.Black;
	[Export] public float transitionSpeed = 0.5f;
	[Export] public Resource musicResource;

	[ExportGroup("Components")]
	[Export] private AnimationPlayer animator;
	[Export] private AnimationPlayer interfaceAnimator;
	[Export] private AudioStreamPlayer audioPlayer;
	[Export] private VideoStreamFileLoadPlayer videoPlayer;
	private bool isInterfaceVisible;

	[ExportGroup("Subtitles")]
	[Export] private AnimationPlayer subtitleAnimator;
	/// <summary> Subtitles used to preview cutscene in the editor. </summary>
	[Export] private Label subtitleLabel;
	[Export] private Control subtitleRoot;
	private int subtitleKeyIndex = 0;
	private int subtitleDialogIndex = 0;
	private double subtitleLastUpdateTime;
	private bool subtitleIsPlaybackInitialized;

	private bool IsSpecialBook => Menu.menuMemory[Menu.MemoryKeys.ActiveMenu] == (int)Menu.MemoryKeys.SpecialBook;

	private bool isCutsceneFinished;
	private bool isFadingBgm;
	private float interfaceVisibilityTimer;
	/// <summary> How long the pause button needs to be held to skip the cutscene. </summary>
	private readonly float InterfaceVisiblityLength = 1f;
	private readonly float SubtitleOffset = 0.2f;

	public override void _Ready()
	{
		subtitleRoot.SelfModulate = Colors.White.Lerp(Colors.Transparent, SaveManager.Config.cutsceneOpacity * 0.01f);
		subtitleAnimator.Play("RESET");
		subtitleAnimator.Advance(0);

		if (Engine.IsEditorHint())
			return;

		interfaceAnimator.Play(IsSpecialBook ? "special-book" : "cutscene");
		interfaceAnimator.Advance(0.0);
		interfaceAnimator.Play(isCgCutscene ? "cg" : "storybook");
		interfaceAnimator.Advance(0.0);

		LoadLocalization();

		if (!isNestedCutscene && musicResource != null)
		{
			if (SoundManager.instance.StageMusicPlayer.GetBgmResource() != musicResource)
			{
				SoundManager.instance.UpdateBgmResource(musicResource as BGMResource);
				SoundManager.instance.StageMusicPlayer.Stop();
			}
		}

		if (!isNestedCutscene)
			CallDeferred(MethodName.StartCutscene);

		if (IsSpecialBook)
			return;

		// Set up menu memory to match level data (Adventure Mode only)
		if (adventureLevelAutoload != null)
		{
			Menu.menuMemory[Menu.MemoryKeys.ActiveMenu] = (int)Menu.MemoryKeys.LevelSelect;
			Menu.menuMemory[Menu.MemoryKeys.WorldSelect] = (int)adventureLevelAutoload.AreaKey;
			Menu.menuMemory[Menu.MemoryKeys.LevelSelect] = adventureLevelAutoload.LevelIndex - 1;
		}
	}

	public override void _EnterTree()
	{
		if (Engine.IsEditorHint())
			return;

		DebugManager.Instance.IsCutsceneActive = true;
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint())
			return;

		DebugManager.Instance.IsCutsceneActive = false;
	}

	private void LoadLocalization()
	{
		StringName targetLocale = SaveManager.Config.voiceLocale.LocaleId;
		LoadAudioTrack(targetLocale);

		if (animator == null)
			return;

		// Load timing animation
		if (!animator.HasAnimation(targetLocale))
			targetLocale = "en";

		animator.AssignedAnimation = targetLocale;
	}

	private void LoadAudioTrack(string targetLocale)
	{
		if (string.IsNullOrEmpty(englishAudioPath)) // No audio to load
			return;

		string targetAudioPath = ResourceUid.UidToPath(englishAudioPath);
		if (targetAudioPath.Contains("/en/")) // localizable audio
		{
			targetAudioPath = targetAudioPath.Replace("/en/", $"/{targetLocale}/");

			if (!ResourceLoader.Exists(targetAudioPath)) // Revert to english
			{
				GD.PushError($"Couldn't find audio at {targetAudioPath}!");
				targetAudioPath = englishAudioPath;
			}
		}

		if (audioPlayer.Stream != null && audioPlayer.Stream.ResourcePath.Equals(targetAudioPath))
			return;

		// Load audio
		audioPlayer.Stream = ResourceLoader.Load<AudioStreamOggVorbis>(targetAudioPath);
	}

	private void AutoSetup()
	{
		string name = Name.ToString().ToCamelCase();

		// Get event number
		string eventNumber = string.Empty;
		for (int i = name.Length - 1; i >= 0; i--)
		{
			if (name[i] < '0' || name[i] > '9')
				break;

			eventNumber = $"{name[i]}{eventNumber}";
		}

		if (string.IsNullOrEmpty(eventNumber))
		{
			GD.PrintErr("Couldn't find an event number in the node's name! Cancelling auto-setup.");
			return;
		}

		while (eventNumber.Length < 2)
			eventNumber = $"0{eventNumber}";

		string targetAudioPath = $"res://video/event/en/{name}.ogg";
		if (ResourceLoader.Exists(targetAudioPath))
			englishAudioPath = targetAudioPath;

		localizationKeyPrefix = $"event{eventNumber}_";

		videoPlayer.SetVideoFilePath($"res://video/event/stream/E00{eventNumber}.mp4");

		animator = GetChildOrNull<AnimationPlayer>(-1);
		if (animator != null) // Animator is already set up.
			return;

		animator = new AnimationPlayer
		{
			Name = "AnimationPlayer"
		};
		AddChild(animator);
		animator.Owner = GetTree().EditedSceneRoot;

		// Create default anim
		Animation enAnim = new();
		enAnim.AddTrack(Animation.TrackType.Method);
		enAnim.TrackSetPath(0, ".");
		enAnim.Step = 0.1f;

		LoadAudioTrack("en");
		if (audioPlayer.Stream != null)
		{
			enAnim.Length = Mathf.CeilToInt(audioPlayer.Stream.GetLength());
			audioPlayer.Stream = null;
		}

		// Create animation library
		AnimationLibrary animLibrary = new();
		animLibrary.AddAnimation("en", enAnim);

		animator.AddAnimationLibrary(string.Empty, animLibrary);
	}

	private void StartCutscene()
	{
		videoPlayer.Play();
		audioPlayer.Play();

		if (animator != null)
		{
			animator.Seek(0.0);
			animator.Play();
		}
	}

	public override void _PhysicsProcess(double _)
	{
		ResyncEditorIndex();

		if (Engine.IsEditorHint())
			return;

		if (isNestedCutscene)
			return;

		if (isFadingBgm && !SoundManager.FadeAudioPlayer(SoundManager.instance.StageMusicPlayer, 0.5f))
			SoundManager.instance.SetStageMusicVolume(0f);

		if (isCutsceneFinished)
		{
			SoundManager.FadeAudioPlayer(audioPlayer, 0.5f);
			return;
		}

		if (TransitionManager.IsTransitionActive)
			return;

		if (!isInterfaceVisible)
		{
			CheckInterfaceVisiblity();
			return;
		}

		if (IsSpecialBook && Runtime.Instance.IsActionJustPressed("sys_cancel", "ui_cancel", "escape"))
		{
			OnEventFinished(true);
			return;
		}
		else if (!IsSpecialBook && Runtime.Instance.IsActionPressed("sys_pause", "ui_accept") && !Input.IsActionJustPressed("toggle_fullscreen"))
		{
			OnEventFinished(true);
			return;
		}

		if (OS.IsDebugBuild() && !Mathf.IsEqualApprox(Engine.TimeScale, 1.0))
		{
			if (audioPlayer != null && animator != null)
				audioPlayer.Seek((float)animator.CurrentAnimationPosition + (float)AudioServer.GetTimeSinceLastMix()); // Resync audio (for debug TURBO support)
		}

		if (Input.IsAnythingPressed())
			return;

		interfaceVisibilityTimer = Mathf.MoveToward(interfaceVisibilityTimer, 0f, PhysicsManager.physicsDelta);
		if (Mathf.IsZeroApprox(interfaceVisibilityTimer))
		{
			isInterfaceVisible = false;
			interfaceAnimator.Play("hide_interface", 0.1f);
		}
	}

	private void CheckInterfaceVisiblity()
	{
		if (!Input.IsAnythingPressed())
			return;

		isInterfaceVisible = true;
		interfaceVisibilityTimer = InterfaceVisiblityLength;
		interfaceAnimator.Play("show_interface", 0f);
	}

	/// <summary> Called after the cutscene has finished playing. </summary>
	public void OnEventFinished()
	{
		if (isCutsceneFinished) // Must have been manually skipped
			return;

		OnEventFinished(false);
	}
	public void OnEventFinished(bool isCanceled)
	{
		if (Engine.IsEditorHint())
			return;

		if (isNestedCutscene) // Don't do anything for nested cutscenes
			return;

		if (IsSpecialBook || isCanceled)
			FadeOutCreditsMusic();

		isCutsceneFinished = true;
		EmitSignal(SignalName.EventFinished);

		if (!IsSpecialBook && adventureLevelAutoload != null)
		{
			// Load to level
			TransitionManager.QueueSceneChange(adventureLevelAutoload.LevelPath);
			TransitionManager.StartTransition(new()
			{
				inSpeed = 1f,
				color = Colors.Black,
				loadAsynchronously = true,
				disableAutoTransition = true,
				showMissionDescription = true
			});
			TransitionManager.Instance.SetMissionDescriptionText(adventureLevelAutoload.MissionTypeKey, adventureLevelAutoload.MissionDescriptionKey);
			TransitionManager.Instance.UpdateLoadingText("load_level");
			return;
		}

		string targetScene = TransitionManager.MenuScenePath;
		if (IsSpecialBook)
			targetScene = TransitionManager.SpecialBookScenePath;
		else if (!string.IsNullOrEmpty(adventureEventAutoload))
			targetScene = adventureEventAutoload;

		if (targetScene.Equals(TransitionManager.MenuScenePath))
		{
			TransitionManager.Instance.QueuedScene = targetScene;
			NotificationManager.Instance.StartNotifications();
			return;
		}

		TransitionManager.QueueSceneChange(targetScene);
		TransitionManager.StartTransition(new TransitionData()
		{
			color = isCanceled ? Colors.Black : transitionColor,
			inSpeed = isCanceled ? 0.5f : transitionSpeed,
			outSpeed = 0.5f,
		});
	}

	#region Editor
	/// <summary> Method used simply for editor keyframing. </summary>
	private void ShowSubtitles() { }

	private void ShowSubtitlesFromScript()
	{
		if (Engine.IsEditorHint())
		{
			subtitleLabel.Text = $"{localizationKeyPrefix}{subtitleDialogIndex}";
			subtitleRoot.Visible = true;
		}
		else
		{
			subtitleLabel.Text = SoundManager.instance.FormatText(Tr($"{localizationKeyPrefix}{subtitleDialogIndex}"));
			subtitleAnimator.Play(subtitleRoot.Visible ? "show-text" : "show");
		}
	}

	/// <summary> Method used simply for editor keyframing. </summary>
	private void HideSubtitles() { }

	private void HideSubtitlesFromScript()
	{
		if (Engine.IsEditorHint())
			subtitleRoot.Visible = false;
		else
			subtitleAnimator.Play("hide");
	}

	private void PlayFromEditor()
	{
		animator.Play();
		if (Engine.IsEditorHint())
			LoadAudioTrack(animator.CurrentAnimation);

		audioPlayer.Play((float)animator.CurrentAnimationPosition);
		InitializeEditorIndex();
	}

	private void PauseFromEditor()
	{
		audioPlayer.Stop();
		audioPlayer.Stream = null;
		animator.Pause();
		subtitleIsPlaybackInitialized = false;
	}

	private void InitializeEditorIndex()
	{
		if (string.IsNullOrEmpty(animator.CurrentAnimation))
			return;

		subtitleKeyIndex = 0;
		subtitleDialogIndex = 0;
		subtitleLastUpdateTime = GetCurrentTime();
		subtitleAnimator.Play("RESET");
		subtitleAnimator.Advance(0);

		Animation currentAnimation = animator.GetAnimation(animator.CurrentAnimation);
		for (int i = 0; i < currentAnimation.TrackGetKeyCount(0); i++)
		{
			if (currentAnimation.TrackGetKeyTime(0, i) > subtitleLastUpdateTime)
				break;

			ProcessEditorKeyframe(currentAnimation.TrackGetKeyValue(0, i).As<Dictionary>());
		}

		subtitleIsPlaybackInitialized = true;
	}

	private void ResyncEditorIndex()
	{
		if (animator == null)
			return;

		if (string.IsNullOrEmpty(animator.CurrentAnimation))
		{
			if (subtitleIsPlaybackInitialized)
				PauseFromEditor();

			return;
		}

		if (!subtitleIsPlaybackInitialized)
		{
			PlayFromEditor();
			return;
		}

		Animation currentAnimation = animator.GetAnimation(animator.CurrentAnimation);
		if (subtitleKeyIndex >= currentAnimation.TrackGetKeyCount(0) ||
			currentAnimation.TrackGetKeyTime(0, subtitleKeyIndex) > GetCurrentTime())
		{
			return;
		}

		ProcessEditorKeyframe(currentAnimation.TrackGetKeyValue(0, subtitleKeyIndex).As<Dictionary>());
		subtitleLastUpdateTime = GetCurrentTime();
	}

	private double GetCurrentTime()
	{
		if (Engine.IsEditorHint())
			return animator.CurrentAnimationPosition;

		return animator.CurrentAnimationPosition + SubtitleOffset;
	}

	private void ProcessEditorKeyframe(Dictionary key)
	{
		subtitleKeyIndex++;

		StringName method = key["method"].As<StringName>();

		if (method.Equals(MethodName.ShowSubtitles))
		{
			subtitleDialogIndex++;
			ShowSubtitlesFromScript();
			return;
		}

		if (method.Equals(MethodName.HideSubtitles))
			HideSubtitlesFromScript();
	}

	/// <summary> Called from an animation during the final cutscene. </summary>
	private void PlayCreditsMusic() => SoundManager.instance.StartBgm(!SoundManager.instance.StageMusicPlayer.Playing);

	private void FadeOutCreditsMusic() => isFadingBgm = true;
	#endregion
}
