using Godot;
using System.Collections.Generic;
using Project.Gameplay.Triggers;
using Project.Interface;
using System.Text.RegularExpressions;
using Project.Gameplay;

namespace Project.Core;

public partial class SoundManager : Control
{
	public static SoundManager instance;

	public enum AudioBuses
	{
		Master,
		Main,
		Duck,
		Bgm,
		Voice,
		SfxAdjustment,
		Sfx,
		GameSfx,
		BreakSfx,
		Cutscene,
		Count
	}

	/// <summary> Extra flag for audio ducking during the rank quote. </summary>
	public bool IsRankQuotePlaying { get; set; }

	private float currentDuckVolume = 0f;
	/// <summary> How much to duck the audio when dialog is active. </summary>
	private readonly float DuckVolumeDB = -4f;
	private readonly float DuckOutSpeed = 20f;
	private readonly float DuckInSpeed = 10f;

	public override void _Ready()
	{
		instance = this;
		subtitleAnimator.Play("RESET");
		InitializePearlSFX();

		buttonPromptCharacterIndexes = new int[buttonPrompts.Length];

		// Cancel Dialog when switching to a new scene
		TransitionManager.Instance.SceneChanged += CancelDialog;
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateSfxGroups();
		UpdateAudioDucking();
	}

	private void UpdateAudioDucking()
	{
		bool isDuckActive = (IsDialogActive || IsRankQuotePlaying) && !SaveManager.Config.isVoiceMuted && SaveManager.Config.voiceVolume != 0;
		float targetVolumeDb = isDuckActive ? DuckVolumeDB : 0f;
		float targetSpeed = isDuckActive ? DuckInSpeed : DuckOutSpeed;
		currentDuckVolume = Mathf.MoveToward(currentDuckVolume, targetVolumeDb, targetSpeed * (float)GetPhysicsProcessDeltaTime());
		AudioServer.SetBusVolumeDb((int)AudioBuses.Duck, currentDuckVolume);
	}

	#region Audio Bus
	/// <summary> Sets whether the break channel is muted or not (for muting environments) </summary>
	public static bool IsBreakChannelMuted
	{
		set => AudioServer.SetBusMute((int)AudioBuses.BreakSfx, value);
	}

	/// <summary> Changes the volume of an audio bus channel. </summary>
	public static void SetAudioBusVolume(AudioBuses bus, int volumePercentage, bool isMuted = default)
	{
		if (volumePercentage == 0)
			isMuted = true;

		AudioServer.SetBusMute((int)bus, isMuted); // Mute or unmute
		AudioServer.SetBusVolumeLinear((int)bus, volumePercentage * .01f);
	}
	#endregion

	#region Dialog
	public bool IsSubtitlesActive { get; private set; }
	public bool IsDialogActive => IsSubtitlesActive && CurrentSpeaker != SpeakerEnum.None;
	[Export] private Label subtitleLabel;
	[Export] private NavigationButton[] buttonPrompts;
	private int buttonPromptIndex; // The current button prompt being calculated
	private int[] buttonPromptCharacterIndexes; // 
	[Export] private ColorRect subtitleLetterbox;
	[Export] private AnimationPlayer subtitleAnimator;
	[Export] private AudioStreamPlayer dialogChannel;
	[Export] private Timer delayTimer;
	private int currentDialogIndex;
	private DialogTrigger currentDialog;
	private Queue<DialogTrigger> dialogQueue = [];
	public void QueueDialog(DialogTrigger dialog) => dialogQueue.Enqueue(dialog);

	public void ClearQueue() => dialogQueue.Clear();

	public void PlayDialog(DialogTrigger dialog)
	{
		if (dialog.DialogCount == 0 || SaveManager.ActiveSkillRing.IsSkillEquipped(Gameplay.SkillKey.Character)
			|| DebugManager.Instance.DisableDialog || SaveManager.Config.isDialogDisabled)
		{
			return; // No dialog
		}

		Visible = !SaveManager.Config.isSubtitleDisabled && !dialog.disableSubtitles;

		IsSubtitlesActive = true;
		subtitleLabel.Text = string.Empty;

		currentDialog = dialog;
		currentDialogIndex = GetInitialDialogIndex();
		UpdateDialog(true);
	}

	private int GetInitialDialogIndex()
	{
		if (!currentDialog.randomize)
			return 0;

		if (IsSonicSfxVoiceChannelActive)
		{
			// Prioritize others (i.e. Shahra) when Sonic is already speaking from a sound effect
			for (int i = 0; i < currentDialog.DialogCount; i++)
			{
				if (currentDialog.textKeys[i].EndsWith(SonicVoiceSuffix))
					continue;

				return i;
			}
		}

		// Pure random value, used when Sonic isn't already speaking or only Sonic's dialog is available
		return Runtime.randomNumberGenerator.RandiRange(0, currentDialog.DialogCount - 1);
	}

	public void CancelDialog()
	{
		UpdateCharacterDialog();
		if (!IsSubtitlesActive) return;

		dialogQueue.Clear();
		delayTimer.Stop();
		dialogChannel.Stop();

		CallDeferred(MethodName.DisableDialog);
	}
	public void OnDialogDelayComplete() => UpdateDialog(false);

	public void OnDialogFinished()
	{
		currentDialogIndex++;
		if (currentDialog.randomize || currentDialogIndex >= currentDialog.DialogCount) // Start next dialog line
		{
			CallDeferred(MethodName.DisableDialog);
			return;
		}

		if (currentDialog.IsCutscene)
		{
			if (currentDialog.HasDelay(currentDialogIndex))
				subtitleAnimator.Play("deactivate-cutscene");
			OnSubtitleAnimationFinished();
			return;
		}

		subtitleAnimator.Play("deactivate");
	}

	private void DisableDialog()
	{
		IsSubtitlesActive = false;
		subtitleAnimator.Play("deactivate");

		UpdateCharacterDialog();

		// Disconnect signals
		if (delayTimer.IsConnected(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogDelayComplete)))
			delayTimer.Disconnect(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogDelayComplete));

		if (delayTimer.IsConnected(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogFinished)))
			delayTimer.Disconnect(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogFinished));

		if (dialogChannel.IsConnected(AudioStreamPlayer.SignalName.Finished, new Callable(this, MethodName.OnDialogFinished)))
			dialogChannel.Disconnect(AudioStreamPlayer.SignalName.Finished, new Callable(this, MethodName.OnDialogFinished));
	}

	private void OnSubtitleAnimationFinished()
	{
		if (IsSubtitlesActive)
		{
			UpdateDialog(true);
			return;
		}

		AdvanceDialogQueue();
	}

	public void AdvanceDialogQueue()
	{
		if (dialogQueue.Count != 0) // Start queued dialog if it exists
			PlayDialog(dialogQueue.Dequeue());
	}

	private void UpdateDialog(bool processDelay)
	{
		ResetButtonPrompts();
		InitializeSubtitleOpacity();
		// Must have been interrupted
		if (dialogChannel.IsConnected(AudioStreamPlayer.SignalName.Finished, new Callable(this, MethodName.OnDialogFinished)))
			dialogChannel.Disconnect(AudioStreamPlayer.SignalName.Finished, new Callable(this, MethodName.OnDialogFinished));

		UpdateCharacterDialog();

		if (processDelay && currentDialog.HasDelay(currentDialogIndex)) // Wait for dialog delay (if applicable)
		{
			delayTimer.Start(currentDialog.delays[currentDialogIndex]);
			delayTimer.Connect(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogDelayComplete), (uint)ConnectFlags.OneShot);
			return;
		}

		subtitleAnimator.Play(currentDialogIndex == 0 ? "activate" : "activate-text");

		string key = currentDialog.textKeys[currentDialogIndex];
		AudioStream targetStream = null;
		if (IsInstanceValid(Gameplay.StageSettings.Instance))
			targetStream = Gameplay.StageSettings.Instance.dialogLibrary.GetDialogStream(key, SaveManager.GetCurrentVoiceLocaleIndex());

		if (targetStream != null) // Using audio
		{
			dialogChannel.Stream = targetStream;
			subtitleLabel.Text = FormatText(Tr(currentDialog.textKeys[currentDialogIndex]));
			CallDeferred(MethodName.UpdateButtonPromptPosition);
			dialogChannel.Play();
			if (!currentDialog.HasLength(currentDialogIndex))// Use audio length
			{
				dialogChannel.Connect(AudioStreamPlayer.SignalName.Finished, new Callable(this, MethodName.OnDialogFinished), (uint)ConnectFlags.OneShot);
				return;
			}
		}
		else  // Text-only keys
		{
			if (!currentDialog.HasLength(currentDialogIndex)) // Skip
			{
				GD.PushWarning("Text-only dialog doesn't have a specified length. Skipping.");
				OnDialogFinished();
				return;
			}

			// Experimental: Allow audio to keep playing? For long hint dialogs.
			// dialogChannel.Stream = null; // Disable dialog channel

			if (string.IsNullOrEmpty(key) || key.EndsWith("*")) // Cutscene Support - To avoid busywork in editor
				key = currentDialog.textKeys[0].Replace("*", (currentDialogIndex + 1).ToString());
			subtitleLabel.Text = FormatText(Tr(key)); // Update subtitles
			CallDeferred(MethodName.UpdateButtonPromptPosition);
		}

		// If we've made it this far, we're using the custom specified time
		if (!delayTimer.IsConnected(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogFinished)))
			delayTimer.Connect(Timer.SignalName.Timeout, new Callable(this, MethodName.OnDialogFinished), (uint)ConnectFlags.OneShot);
		delayTimer.Start(currentDialog.displayLength[currentDialogIndex]);
	}

	private void ResetButtonPrompts()
	{
		buttonPromptIndex = 0;
		for (int i = 0; i < buttonPrompts.Length; i++)
			buttonPrompts[i].Visible = false;
	}

	/// <summary> Replaces curly braces with quotation marks for cutscene subtitles. </summary>
	public string FormatText(string text)
	{
		text = text.Replace('{', '"');
		text = text.Replace('}', '"');
		text = FormatButtonPrompts(text);
		// TODO Support multiple buttons in a single text
		return text;
	}

	/// <summary> Recursively formats text, replacing button prompts with spaces. </summary>
	public string FormatButtonPrompts(string text)
	{
		Match regexMatch = ButtonPromptRegex().Match(text);
		if (!regexMatch.Success)
			return text;

		buttonPrompts[buttonPromptIndex].Visible = true;
		string inputKey = regexMatch.Groups[0].Value.Substring(1, regexMatch.Groups[0].Length - 2);
		if (inputKey == "button_crouch")
			inputKey = SaveManager.ActiveSkillRing.IsSkillEquipped(Gameplay.SkillKey.ChargeJump) ? "button_jump" : "button_action";

		buttonPrompts[buttonPromptIndex].SetInputKey(inputKey);
		text = text.Replace(regexMatch.Captures[0].Value, ButtonSpaceReplacement); // 5 Spaces
		buttonPromptCharacterIndexes[buttonPromptIndex] = regexMatch.Captures[0].Index + 2;
		buttonPromptIndex++;
		return FormatText(text);
	}

	[GeneratedRegex("\\[(.*?)\\]")]
	private static partial Regex ButtonPromptRegex();
	private readonly string ButtonSpaceReplacement = "     ";

	private void InitializeSubtitleOpacity()
	{
		if (currentDialog.IsCutscene)
			subtitleLetterbox.Color = new Color(0.0f, 0.0f, 0.0f, SaveManager.Config.cutsceneOpacity * 0.01f);
		else
			subtitleLetterbox.Color = new Color(0.0f, 0.0f, 0.0f, SaveManager.Config.subtitleOpacity * 0.01f);
	}

	private void UpdateButtonPromptPosition()
	{
		for (int i = 0; i < buttonPrompts.Length; i++)
		{
			if (!buttonPrompts[i].Visible)
				return;

			Vector2 buttonPromptOffset = -buttonPrompts[i].Size * 0.5f;
			Rect2 charBounds = subtitleLabel.GetCharacterBounds(buttonPromptCharacterIndexes[i]);
			buttonPrompts[i].GlobalPosition = subtitleLabel.GlobalPosition + charBounds.GetCenter() + buttonPromptOffset;
		}
	}


	/// <summary> The current character speaking. </summary>
	public SpeakerEnum CurrentSpeaker { get; private set; }
	public enum SpeakerEnum
	{
		None, // No character is speaking
		Sonic,
		Shahra,
		Erazor, // Includes alf-layla
		Knuckles
	}

	public bool IsSonicSfxVoiceChannelActive { get; set; }
	[Signal]
	public delegate void CharacterSpeechFinishedEventHandler(SpeakerEnum speaker);
	[Signal]
	public delegate void CharacterSpeechStartedEventHandler(SpeakerEnum speaker);
	private void UpdateCharacterDialog() // Checks whether Sonic is the one speaking, and mutes his gameplay audio.
	{
		SpeakerEnum previousSpeaker = CurrentSpeaker;
		CurrentSpeaker = RecalculateCurrentSpeaker();
		if (CurrentSpeaker != previousSpeaker)
		{
			EmitSignal(SignalName.CharacterSpeechFinished, (int)previousSpeaker);
			EmitSignal(SignalName.CharacterSpeechStarted, (int)CurrentSpeaker);
		}
	}

	private const string SonicVoiceSuffix = "so";
	private const string ShahraVoiceSuffix = "sh";
	private const string KnucklesVoiceSuffix = "kn";
	private const string ErazorVoiceSuffix = "er";
	private SpeakerEnum RecalculateCurrentSpeaker()
	{
		if (!IsSubtitlesActive)
			return SpeakerEnum.None;

		string currentKey = currentDialog.textKeys[currentDialogIndex];
		if (currentKey.EndsWith(SonicVoiceSuffix))
			return SpeakerEnum.Sonic;

		if (currentKey.EndsWith(ShahraVoiceSuffix))
			return SpeakerEnum.Shahra;

		if (currentKey.EndsWith(KnucklesVoiceSuffix))
			return SpeakerEnum.Knuckles;

		if (currentKey.EndsWith(ErazorVoiceSuffix))
			return SpeakerEnum.Erazor;

		return SpeakerEnum.None;
	}

	#endregion

	#region SFX
	/// <summary>
	/// Fade a sound to -80f, then stop the audio player. Returns true if audio player is still playing.
	/// </summary>
	public static bool FadeAudioPlayer(AudioStreamPlayer audioPlayer, float fadeTime = 1.0f)
	{
		if (audioPlayer.Playing) // Already stopped playing
		{
			if (Mathf.IsZeroApprox(fadeTime))
			{
				audioPlayer.Stop();
			}
			else
			{
				audioPlayer.VolumeDb = Mathf.MoveToward(audioPlayer.VolumeDb, -80, 80 * (1.0f / fadeTime) * PhysicsManager.physicsDelta);
				if (Mathf.IsEqualApprox(audioPlayer.VolumeDb, -80))
					audioPlayer.Stop();
			}
		}

		return audioPlayer.Playing;
	}

	/// <summary>
	/// Fade a sound to -80f, then stop the audio player. Returns true if audio player is still playing.
	/// </summary>
	public static bool FadeAudioPlayer(AudioStreamPlayer3D audioPlayer, float fadeTime = 1.0f)
	{
		if (audioPlayer.Playing) // Already stopped playing
		{
			if (Mathf.IsZeroApprox(fadeTime))
			{
				audioPlayer.Stop();
			}
			else
			{
				audioPlayer.VolumeDb = Mathf.MoveToward(audioPlayer.VolumeDb, -80, 80 * (1.0f / fadeTime) * PhysicsManager.physicsDelta);
				if (Mathf.IsEqualApprox(audioPlayer.VolumeDb, -80))
					audioPlayer.Stop();
			}
		}

		return audioPlayer.Playing;
	}

	// Item pickups are played in the SoundManager to avoid volume increase when collecting more than one at a time.
	[Export]
	private AudioStreamPlayer ringSFX;
	private bool canPlayRingSfx;
	public void PlayRingSFX()
	{
		if (!canPlayRingSfx) // Prevent multiple ring sound effects playing on the same frame
			return;

		ringSFX.Play();
		canPlayRingSfx = false;
	}
	[Export]
	private AudioStreamPlayer richRingSFX;
	public void PlayRichRingSFX() => richRingSFX.Play();

	[Export]
	private Node pearlSFX;
	private readonly List<AudioStreamPlayer> pearlSFXList = [];
	public int PearlSoundEffectIndex { get; set; }
	[Export]
	private AudioStreamPlayer richPearlSFX;
	[Export]
	private Timer pearlTimer;
	private const float PEARL_AUDIO_DUCK_STRENGTH = .8f;

	private void InitializePearlSFX()
	{
		for (int i = 0; i < pearlSFX.GetChildCount(); i++)
		{
			AudioStreamPlayer audioPlayer = pearlSFX.GetChildOrNull<AudioStreamPlayer>(i);
			if (audioPlayer != null)
				pearlSFXList.Add(audioPlayer);
		}
	}

	public void ResetPearlSFX() => PearlSoundEffectIndex = 0;
	public void PlayPearlSFX()
	{
		pearlSFXList[PearlSoundEffectIndex].Play();
		PearlSoundEffectIndex++;
		if (PearlSoundEffectIndex >= pearlSFXList.Count)
			PearlSoundEffectIndex = pearlSFXList.Count - 1;

		float volume = (PearlSoundEffectIndex - 1f) / pearlSFXList.Count * PEARL_AUDIO_DUCK_STRENGTH;
		volume = Mathf.LinearToDb(1 - volume);

		for (int i = 0; i < pearlSFXList.Count; i++) // Audio Ducking
		{
			pearlSFXList[i].VolumeDb = volume;
		}

		pearlTimer.WaitTime = 3f; // Reset pearl sfx after 3 seconds
		pearlTimer.Start();
	}

	public void PlayRichPearlSFX() => richPearlSFX.Play();

	public void StopAllPearlSFX()
	{
		for (int i = 0; i < pearlSFXList.Count; i++)
			pearlSFXList[i].Stop();
	}

	private float sfxGroupTimer;
	private readonly Dictionary<StringName, int> sfxGroups = [];
	private readonly Dictionary<StringName, float> sfxGroupTimers = [];
	/// <summary> Minimum amount of time that must pass before a sfx group can play again. </summary>
	private readonly float groupSfxSpacing = 0.5f;

	private void UpdateSfxGroups()
	{
		if (sfxGroups.Count != 0)
			sfxGroupTimer += PhysicsManager.physicsDelta;

		canPlayRingSfx = true;
	}

	public bool CanPlaySfxInGroup(StringName key, int maxPolyphony)
	{
		if (!sfxGroups.ContainsKey(key))
			return true;

		if (Mathf.Abs(sfxGroupTimer - sfxGroupTimers[key]) < groupSfxSpacing)
			return false;

		return sfxGroups[key] < maxPolyphony;
	}

	public float AddGroupSfx(StringName key)
	{
		if (!sfxGroups.ContainsKey(key))
		{
			sfxGroups.Add(key, 0);
			sfxGroupTimers.Add(key, sfxGroupTimer);
		}

		sfxGroups[key]++;
		sfxGroupTimers[key] = sfxGroupTimer;
		return CalculateGroupSfxVolumeDb(key);
	}

	public float RemoveGroupSfx(StringName key)
	{
		if (sfxGroups.TryGetValue(key, out int value))
		{
			sfxGroups[key] = value - 1;
			if (sfxGroups[key] < 0)
			{
				sfxGroups.Remove(key);
				sfxGroupTimers.Remove(key);
			}
		}

		return CalculateGroupSfxVolumeDb(key);
	}

	public float CalculateGroupSfxVolumeDb(StringName key)
	{
		if (sfxGroups.TryGetValue(key, out int value)) // Calculate target db volume
			return Mathf.LinearToDb(1.0f / value);

		return 0.0f; // Don't modify db
	}
	#endregion

	#region BGM
	[Export] public BGMPlayer StageMusicPlayer { get; private set; }
	public void UpdateBgmResource(BGMResource bgmResource)
	{
		if (StageMusicPlayer.GetBgmResource() == bgmResource)
			return;

		StageMusicPlayer.SetBgmResource(bgmResource);
		StageMusicPlayer.LoadBgmResource();
	}

	// Called when countdown starts to keep things in sync, regardless of load times.
	public void StartBgm(bool forceRestart)
	{
		if (StageMusicPlayer.Playing && !forceRestart) // Persistent BGM
			return;

		StageMusicPlayer.Play();
	}

	public bool IsStageMusicPaused
	{
		get => StageMusicPlayer.StreamPaused != false;
		set => StageMusicPlayer.StreamPaused = value;
	}

	public void SetStageMusicVolume(float db) => StageMusicPlayer.VolumeDb = db;
	#endregion
}
