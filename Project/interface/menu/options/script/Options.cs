using Godot;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface.Menus;

public partial class Options : Menu
{
	[Export] private Control cursor;
	[Export] private AnimationPlayer cursorAnimator;
	[Export] private Control contentContainer;
	private bool isCursorHidden;

	[Export] private AnimationPlayer resetAnimator;
	private int resetSelection;

	private int maxSelection;
	private int scrollOffset;

	private float cursorBasePosition;
	/// <summary> How much the cursor should move vertically for each option selected. </summary>
	private readonly int CursorOptionSeparation = 64;

	private void CalculateMaxSelection()
	{
		// Recalculate max selection
		switch (currentSubmenu)
		{
			case Submenus.Options:
				maxSelection = 7;
				break;
			case Submenus.Video:
				maxSelection = videoLabels.Length;
				break;
			case Submenus.Audio:
				maxSelection = audioLabels.Length;
				break;
			case Submenus.Language:
				maxSelection = languageLabels.Length;
				break;
			case Submenus.Control:
				maxSelection = 9;
				break;
			case Submenus.Interface:
				maxSelection = interfaceLabels.Length;
				break;
			case Submenus.Mods:
				maxSelection = modsLabels.Length + 1;
				break;
			case Submenus.Mouse:
				maxSelection = controlMouseLabels.Length;
				break;
			case Submenus.Gyro:
				maxSelection = gyroLabels.Length;
				break;
			case Submenus.Mapping:
				maxSelection = controlMappingOptions.Length;
				break;
			case Submenus.PartyMapping:
				maxSelection = partyMappingOptions.Length + ExtraPartyModeOptionCount;
				break;
			case Submenus.Test:
				maxSelection = 0;
				break;
		}
	}

	private Callable FullscreenToggleCallable => new(this, MethodName.ToggleFullscreen);
	public static readonly string MenuParameter = "menu_texture";

	private Submenus currentSubmenu = Submenus.Options;
	private enum Submenus
	{
		Options, // Main menu
		Video, // Menu for configuring video settings
		Audio,  // Menu for configuring audio volume
		Language, // Menu for localization and language
		Control, // Menu for configuring general control settings
		Interface, // Menu for configuring interface settings
		Mods, //Menu for configuring mod settings
		ResetSettings, // Submenu for resetting the configuration settings
		ResetControls, // Submenu for resetting the control settings
		Mouse, // Control submenu for configuring mouse mappings
		Gyro, // Control submenu for configuring gyro sensitivities
		Mapping, // Control submenu for configuring adventure mode's input mappings
		PartyMapping, // Control submenu for configuring party mode's input mappings
		Unbind, // Control sub-submenu for unbinding inputs
		Test // Control submenu for testing controls
	}

	protected override void SetUp()
	{
		bgm.Play();

		// Prevent the options menu from jumping displays when moving through the options menu
		SaveManager.Config.targetDisplay = DisplayServer.WindowGetCurrentScreen();
		SaveManager.Config.windowSize = GetClosestWindowSize();

		cursorBasePosition = cursor.Position.Y;
		SetUpControlOptions();
		ConnectMouseSignals();
		UpdateLabels();
		CalculateMaxSelection();
		UpdatePartyModeDevice(0);
		DebugManager.Instance.Connect(DebugManager.SignalName.FullscreenToggled, FullscreenToggleCallable);
	}

	public override void _ExitTree() => DebugManager.Instance.Disconnect(DebugManager.SignalName.FullscreenToggled, FullscreenToggleCallable);

	private void ToggleFullscreen()
	{
		UpdateLabels();
		DisableProcessing();
		GetTree().CreateTimer(0.1, true, false, true).Connect(SceneTreeTimer.SignalName.Timeout, new(this, MethodName.EnableProcessing));
	}

	public override void EnableProcessing()
	{
		base.EnableProcessing();

		if (Runtime.Instance.IsUsingMouse)
		{
			isCursorHidden = true;
			cursorAnimator.Play("RESET");
			return;
		}

		if (currentSubmenu != Submenus.Test)
			cursorAnimator.Play("show");
	}

	private void FlipBook(Submenus submenu, bool flipRight, int selection)
	{
		currentSubmenu = submenu;

		animator.Play(flipRight ? "flip-right" : "flip-left");
		animator.Seek(0.0, true);
		VerticalSelection = selection;
		disableCursorProcessing = true;
		if (!isCursorHidden)
			cursorAnimator.Play("hide");

		if (submenu == Submenus.PartyMapping)
			UpdateLabels();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!disableCursorProcessing)
			UpdateScrolling();

		base._PhysicsProcess(delta);
	}

	protected override void ProcessMenu()
	{
		UpdateCursor();

		if (Runtime.Instance.IsActionJustPressed("sys_pause", "ui_accept") && !Input.IsActionJustPressed("toggle_fullscreen"))
			Select();
		else if (Runtime.Instance.IsActionJustPressed("sys_clear", "ui_text_delete"))
			DeleteMapping();
		else
			base.ProcessMenu();

		if (isPlayerLocked)
			CallDeferred(MethodName.UpdatePlayerPosition);
	}

	[Export] private Node3D lockNode;
	private bool isPlayerLocked;
	private PlayerController Player => StageSettings.Player;
	private void LockPlayer()
	{
		isPlayerLocked = true;
		Player.Skills.EnableBreakSkills();
	}

	private void UnlockPlayer()
	{
		isPlayerLocked = false;
		Player.Skills.DisableBreakSkills();
	}

	private void UpdatePlayerPosition()
	{
		Vector3 lockPosition = Player.GlobalPosition;
		lockPosition.X = Mathf.Clamp(lockPosition.X, lockNode.GlobalPosition.X - 1.2f, lockNode.GlobalPosition.X + 1.2f);
		lockPosition.Z = lockNode.GlobalPosition.Z;
		Player.GlobalPosition = lockPosition;
		Player.ResetPhysicsInterpolation();
	}

	protected override void Confirm()
	{
		if (isCursorHidden && currentSubmenu != Submenus.ResetSettings && currentSubmenu != Submenus.ResetControls)
		{
			if (!Runtime.Instance.IsUsingMouse)
				UpdateVerticalSelection();

			return;
		}

		switch (currentSubmenu)
		{
			case Submenus.Options:
				ConfirmOption();
				break;
			case Submenus.Video:
				ConfirmVideoOption();
				break;
			case Submenus.Audio:
				ConfirmAudioOption();
				break;
			case Submenus.Language:
				if (SlideLanguageOption(1))
					ConfirmSFX();
				break;
			case Submenus.Control:
				ConfirmControlOption();
				break;
			case Submenus.Interface:
				ConfirmInterfaceOption();
				break;
			case Submenus.Mods:
				ConfirmModsOption();
				break;
			case Submenus.Mouse:
				ConfirmMouseOption();
				break;
			case Submenus.Gyro:
				ConfirmGyroOption();
				break;
			case Submenus.Mapping:
				ConfirmSFX();
				controlMappingOptions[VerticalSelection].CallDeferred(ControlOption.MethodName.StartListening);
				break;
			case Submenus.PartyMapping:
				if (SlidePartyMappingOption(1))
				{
					ConfirmSFX();
					return;
				}

				int selectedIndex = VerticalSelection - ExtraPartyModeOptionCount;
				if (!controlMappingOptions[selectedIndex].IsReady)
					return;

				ConfirmSFX();
				partyMappingOptions[selectedIndex].CallDeferred(ControlOption.MethodName.StartListening);
				break;
			case Submenus.Test:
				return;
			case Submenus.ResetSettings:
			case Submenus.ResetControls:
				if (resetSelection == -1)
					return;

				if (resetSelection == 1)
				{
					CancelResetMenu();
					return;
				}

				if (currentSubmenu == Submenus.ResetSettings)
					SaveManager.Config = new();

				SaveManager.ResetInputMap();
				resetAnimator.Play("confirm");
				currentSubmenu = currentSubmenu == Submenus.ResetSettings ? Submenus.Options : Submenus.Control;
				break;
		}

		UpdateLabels();
		SaveManager.ApplyConfig();
	}

	private void DeleteMapping()
	{
		if (currentSubmenu != Submenus.Mapping && currentSubmenu != Submenus.PartyMapping)
			return;

		ConfirmSFX();
		if (currentSubmenu == Submenus.Mapping)
		{
			if (!controlMappingOptions[VerticalSelection].IsReady)
				return;

			controlMappingOptions[VerticalSelection].ClearMapping();
			return;
		}

		int selectedIndex = VerticalSelection - ExtraPartyModeOptionCount;
		partyMappingOptions[selectedIndex].ClearMapping();
	}

	protected override void Cancel()
	{
		switch (currentSubmenu)
		{
			case Submenus.Options:
				CancelSFX();
				DisableProcessing();
				FadeBgm(.5f);
				SaveManager.SaveConfig();
				menuMemory[MemoryKeys.ActiveMenu] = (int)MemoryKeys.MainMenu;
				TransitionManager.QueueSceneChange(TransitionManager.MenuScenePath);
				TransitionManager.StartTransition(new()
				{
					color = Colors.Black,
					inSpeed = .5f,
				});
				break;
			case Submenus.Mouse:
				CancelSFX();
				FlipBook(Submenus.Control, true, 3);
				break;
			case Submenus.Gyro:
				CancelSFX();
				FlipBook(Submenus.Control, true, 4);
				break;
			case Submenus.Mapping:
				if (!controlMappingOptions[VerticalSelection].IsReady) return;

				CancelSFX();
				FlipBook(Submenus.Control, true, 5);
				break;
			case Submenus.PartyMapping:
				if (VerticalSelection >= ExtraPartyModeOptionCount &&
					!partyMappingOptions[VerticalSelection - ExtraPartyModeOptionCount].IsReady)
				{
					return;
				}

				CancelSFX();
				FlipBook(Submenus.Control, true, 6);
				break;
			case Submenus.Test:
				return;
			case Submenus.ResetSettings:
			case Submenus.ResetControls:
				CancelResetMenu();
				break;
			default:
				CancelSFX();
				FlipBook(Submenus.Options, true, (int)currentSubmenu - 1);
				break;
		}
	}

	private void CancelResetMenu()
	{
		if (resetSelection != 1)
		{
			resetAnimator.Play("select-no");
			resetAnimator.Advance(0.0);
		}

		resetSelection = 1;
		resetAnimator.Play("hide");
		currentSubmenu = currentSubmenu == Submenus.ResetSettings ? Submenus.Options : Submenus.Control;
	}

	private void Select()
	{
		if (currentSubmenu != Submenus.Test)
		{
			Confirm();
			return;
		}

		if (Player.IsLaunching)
			return;

		// Cancel test
		UnlockPlayer();
		animator.Play("test_end");
		currentSubmenu = Submenus.Control;
	}

	protected override void UpdateSelection()
	{
		if (currentSubmenu == Submenus.Test)
			return;

		if (maxSelection == 0)
			return;

		// Listening for inputs
		if (currentSubmenu == Submenus.Mapping && !controlMappingOptions[VerticalSelection].IsReady)
			return;

		// Listening for inputs
		if (currentSubmenu == Submenus.PartyMapping &&
			VerticalSelection >= ExtraPartyModeOptionCount &&
			!partyMappingOptions[VerticalSelection - ExtraPartyModeOptionCount].IsReady)
		{
			return;
		}

		Runtime.Instance.IsUsingMouse = false;
		if (currentSubmenu == Submenus.ResetSettings ||
			currentSubmenu == Submenus.ResetControls ||
			Mathf.IsZeroApprox(Input.GetAxis("ui_up", "ui_down")))
		{
			UpdateHorizontalSelection();
			return;
		}

		if (!isCursorHidden)
		{
			int targetSelection = VerticalSelection + Mathf.Sign(Input.GetAxis("ui_up", "ui_down"));
			VerticalSelection = WrapSelection(targetSelection, maxSelection);
		}

		StartSelectionTimer();
		UpdateVerticalSelection();
	}

	private void UpdateVerticalSelection()
	{
		isCursorHidden = false;
		animator.Play("select");
		animator.Seek(0, true);
		cursorAnimator.Play("show");
		cursorAnimator.AnimationSetNext("show", "loop");
		cursorAnimator.Seek(0, true);
	}

	[Export] private Control scrollBar;
	private Vector2 scrollBarVelocity;
	private const float scrollBarSmoothing = .2f;
	private void UpdateScrolling(bool snap = false)
	{
		// Reset scroll
		if (maxSelection < 8)
			scrollOffset = 0;

		if (!scrollBar.IsVisibleInTree() || snap)
		{
			scrollBar.Position = Vector2.Zero;
			scrollOffset = 0;

			if (!scrollBar.IsVisibleInTree())
				return;
		}

		if (VerticalSelection > scrollOffset + 7)
			scrollOffset = VerticalSelection - 7;
		else if (VerticalSelection < scrollOffset)
			scrollOffset = VerticalSelection;

		float scrollRatio = (float)VerticalSelection / (maxSelection - 1);
		Vector2 targetPosition = Vector2.Down * 880 * scrollRatio;

		if (snap)
		{
			scrollBarVelocity = Vector2.Zero;
			scrollBar.Position = targetPosition;
		}
		else
		{
			scrollBar.Position = scrollBar.Position.SmoothDamp(targetPosition, ref scrollBarVelocity, scrollBarSmoothing);
		}
	}

	private bool disableCursorProcessing;
	private void UpdateCursor()
	{
		if (disableCursorProcessing)
			return;

		int offset = VerticalSelection - scrollOffset;
		contentContainer.Position = Vector2.Up * scrollOffset * CursorOptionSeparation;
		cursor.Position = new(cursor.Position.X, cursorBasePosition + (offset * CursorOptionSeparation));
	}

	/// <summary> Changes the visible submenu. Called from the page flip animation. </summary>
	private void UpdateSubmenuVisibility()
	{
		CalculateMaxSelection();
		animator.Play(currentSubmenu.ToString().ToLower());
		animator.Advance(0.0);
		disableCursorProcessing = false;

		CallDeferred(MethodName.UpdateScrolling, true);
		CallDeferred(MethodName.UpdateCursor);
	}

	[Export] private Control[] mouseInputParents;
	[Export] private Label[] videoLabels;
	[Export] private Label[] audioLabels;
	[Export] private Label[] controlMouseLabels;
	[Export] private Label[] languageLabels;
	[Export] private Label[] controlLabels;
	[Export] private Label[] interfaceLabels;
	[Export] private Label[] modsLabels;
	[Export] private Label[] partyMappingLabels;
	[Export] private Label[] gyroLabels;

	private readonly string EnabledString = "option_enable";
	private readonly string DisabledString = "option_disable";
	private readonly string HoldString = "option_hold";
	private readonly string ToggleString = "option_toggle";
	private readonly string BothString = "option_both";
	private readonly string AttackString = "option_attack";
	private readonly string StompString = "option_stomp";
	private readonly string LowString = "option_low";
	private readonly string MediumString = "option_medium";
	private readonly string HighString = "option_high";
	private readonly string MuteString = "option_mute";
	private readonly string RetailStyle = "option_retail";
	private readonly string ReignitedStyle = "option_reignited";
	private readonly string HorizontalStyle = "option_horizontal";
	private readonly string VerticalStyle = "option_vertical";
	private readonly string FullscreenString = "option_fullscreen";
	private readonly string CustomString = "option_custom";
	private readonly string FullscreenNormalString = "option_normal_fullscreen";
	private readonly string FullscreenExclusiveString = "option_exclusive_fullscreen";
	private readonly string PlayerString = "option_player_number";
	private readonly string Aspect4x3 = "4:3";
	private readonly string Aspect16x9 = "16:9";
	private readonly string Aspect16x10 = "16:10";
	private readonly string Aspect21x9 = "21:9";
	private void UpdateLabels()
	{
		videoLabels[0].Text = Tr("option_display").Replace("0", (SaveManager.Config.targetDisplay + 1).ToString());

		if (SaveManager.Config.useFullscreen)
		{
			videoLabels[1].Text = videoLabels[2].Text = FullscreenString;
		}
		else
		{
			Vector2I resolution;
			switch (SaveManager.Config.aspectRatio)
			{
				case SaveManager.AspectRatio.FourByThree:
					videoLabels[1].Text = Aspect4x3;
					resolution = SaveManager.WindowSizes4x3[SaveManager.Config.windowSize];
					break;
				case SaveManager.AspectRatio.SixteenByTen:
					videoLabels[1].Text = Aspect16x10;
					resolution = SaveManager.WindowSizes16x10[SaveManager.Config.windowSize];
					break;
				case SaveManager.AspectRatio.TwentyoneByNine:
					videoLabels[1].Text = Aspect21x9;
					resolution = SaveManager.WindowSizes21x9[SaveManager.Config.windowSize];
					break;
				default:
					videoLabels[1].Text = Aspect16x9;
					resolution = SaveManager.WindowSizes[SaveManager.Config.windowSize];
					break;
			}

			videoLabels[2].Text = $"{resolution.X}:{resolution.Y}";
		}

		videoLabels[3].Text = SaveManager.Config.useExclusiveFullscreen ? FullscreenExclusiveString : FullscreenNormalString;
		if (SaveManager.Config.framerate == 0)
			videoLabels[4].Text = Tr("option_unlimited_fps");
		else
			videoLabels[4].Text = Tr("option_fps").Replace("0", SaveManager.FrameRates[SaveManager.Config.framerate].ToString());
		videoLabels[5].Text = SaveManager.Config.useVsync ? EnabledString : DisabledString;
		videoLabels[6].Text = $"{SaveManager.Config.renderScale}%";
		videoLabels[7].Text = SaveManager.Config.resizeMode.ToString();
		switch (SaveManager.Config.antiAliasing)
		{
			case 0:
				videoLabels[8].Text = DisabledString;
				break;
			case 1:
				videoLabels[8].Text = "FXAA";
				break;
			case 2:
				videoLabels[8].Text = "2x MSAA";
				break;
			case 3:
				videoLabels[8].Text = "4x MSAA";
				break;
			case 4:
				videoLabels[8].Text = "8x MSAA";
				break;
		}
		videoLabels[9].Text = GetQualityString(SaveManager.Config.bloomMode);

		if (SaveManager.Config.softShadowQuality == SaveManager.QualitySetting.Disabled)
			videoLabels[10].Text = "option_hard_shadows";
		else
			videoLabels[10].Text = GetQualityString(SaveManager.Config.softShadowQuality);
		videoLabels[11].Text = GetQualityString(SaveManager.Config.postProcessingQuality);
		videoLabels[12].Text = GetQualityString(SaveManager.Config.reflectionQuality);
		videoLabels[13].Text = SaveManager.Config.useMotionBlur ? EnabledString : DisabledString;
		videoLabels[14].Text = SaveManager.Config.useScreenShake ? $"{SaveManager.Config.screenShake}%" : DisabledString;

		audioLabels[0].Text = SaveManager.Config.isMasterMuted ? MuteString : $"{SaveManager.Config.masterVolume}%";
		audioLabels[1].Text = SaveManager.Config.isBgmMuted ? MuteString : $"{SaveManager.Config.bgmVolume}%";
		audioLabels[2].Text = SaveManager.Config.isSfxMuted ? MuteString : $"{SaveManager.Config.sfxVolume}%";
		audioLabels[3].Text = SaveManager.Config.isVoiceMuted ? MuteString : $"{SaveManager.Config.voiceVolume}%";
		audioLabels[4].Text = SaveManager.Config.useRetailMenuMusic ? RetailStyle : ReignitedStyle;

		languageLabels[1].Text = GetVoiceLanguageKey(SaveManager.Config.voiceLocale);
		languageLabels[3].Text = SaveManager.Config.isSubtitleDisabled ? DisabledString : EnabledString;
		languageLabels[2].Text = SaveManager.Config.isDialogDisabled ? DisabledString : EnabledString;
		languageLabels[4].Text = $"{SaveManager.Config.subtitleOpacity}%";
		languageLabels[5].Text = $"{SaveManager.Config.cutsceneOpacity}%";


		switch (SaveManager.Config.mouseControlMode)
		{
			case SaveManager.MouseControlModeEnum.Disabled:
				controlMouseLabels[0].Text = DisabledString;
				break;
			case SaveManager.MouseControlModeEnum.Absolute:
				controlMouseLabels[0].Text = "option_mouse_positional";
				break;
			case SaveManager.MouseControlModeEnum.Relative:
				controlMouseLabels[0].Text = "option_mouse_motional";
				break;
		}
		controlMouseLabels[1].Text = SaveManager.Config.isMouseVerticalEnabled ? EnabledString : DisabledString;
		controlMouseLabels[2].Text = $"{SaveManager.Config.mouseSensitivity}%";
		controlMouseLabels[3].Text = $"{SaveManager.Config.mouseDeadzone}%";
		controlMouseLabels[4].Text = $"{SaveManager.Config.mouseHorizontalRange}%";
		controlMouseLabels[5].Text = $"{SaveManager.Config.mouseVerticalRange}%";
		controlMouseLabels[6].Text = $"{SaveManager.Config.mouseVerticalOffset}%";

		gyroLabels[0].Text = SaveManager.Config.isGyroEnabled ? EnabledString : DisabledString;
		gyroLabels[1].Text = $"{SaveManager.Config.gyroSensitivity}%";

		controlLabels[0].Text = $"{Mathf.RoundToInt(SaveManager.Config.deadZone * 100)}%";
		controlLabels[1].Text = SaveManager.Config.useHoldBreakMode ? HoldString : ToggleString;
		controlLabels[2].Text = SaveManager.Config.jumpButtonMode switch
		{
			SaveManager.JumpButtonModeEnum.Attack => AttackString,
			SaveManager.JumpButtonModeEnum.Stomp => StompString,
			SaveManager.JumpButtonModeEnum.Disabled => DisabledString,
			_ => BothString,
		};

		partyMappingLabels[0].Text = Tr(PlayerString).Replace("0", partyPlayerIndex.ToString());
		partyMappingLabels[1].Text = partyMappingOptions[0].GetDevice();

		// Update interface labels
		interfaceLabels[0].Text = SaveManager.Config.useProjectReignitionBranding ? ReignitedStyle : RetailStyle;
		switch (SaveManager.Config.hudStyle)
		{
			case SaveManager.HudStyle.Retail:
				interfaceLabels[1].Text = RetailStyle;
				break;
			case SaveManager.HudStyle.Reignition:
				interfaceLabels[1].Text = ReignitedStyle;
				break;
		}

		StringName buttonStyle = "option_controller_auto";
		switch (SaveManager.Config.controllerType)
		{
			case SaveManager.ControllerType.PlayStation:
				buttonStyle = "option_controller_ps";
				break;
			case SaveManager.ControllerType.Xbox:
				buttonStyle = "option_controller_xbox";
				break;
			case SaveManager.ControllerType.Nintendo:
				buttonStyle = "option_controller_nintendo";
				break;
			case SaveManager.ControllerType.Steam:
				buttonStyle = "option_controller_steam";
				break;
		}
		interfaceLabels[2].Text = buttonStyle;

		switch (SaveManager.Config.buttonStyle)
		{
			case SaveManager.ButtonStyle.Style1:
				buttonStyle = "option_style1";
				break;
			case SaveManager.ButtonStyle.Style2:
				buttonStyle = "option_style2";
				break;
		}
		interfaceLabels[3].Text = buttonStyle;

		interfaceLabels[4].Text = SaveManager.Config.isUsingHorizontalSoulGauge ? HorizontalStyle : VerticalStyle;
		interfaceLabels[5].Text = SaveManager.Config.isActionPromptsEnabled ? EnabledString : DisabledString;

		modsLabels[0].Text = SaveManager.Config.areLevelModsEnabled ? EnabledString : DisabledString;
		modsLabels[1].Text = SaveManager.Config.areCharaModsEnabled ? EnabledString : DisabledString;
		modsLabels[2].Text = SaveManager.Config.areLangModsEnabled ? EnabledString : DisabledString;
	}

	private string GetVoiceLanguageKey(LocalizationResource voiceLanguage) => $"lang_{voiceLanguage.LocaleId}";

	private string GetQualityString(SaveManager.QualitySetting setting)
	{
		return setting switch
		{
			SaveManager.QualitySetting.Low => LowString,
			SaveManager.QualitySetting.Medium => MediumString,
			SaveManager.QualitySetting.High => HighString,
			_ => DisabledString,
		};
	}

	[Export] private ControlOption[] controlMappingOptions;
	[Export] private ControlOption[] partyMappingOptions;
	private int partyPlayerIndex = 1; // Index of the player whose controls are currently being edited
	private readonly int ExtraPartyModeOptionCount = 2; // Offset for playerIndex and controllerIndex
	private void SetUpControlOptions()
	{
		foreach (ControlOption controlOption in controlMappingOptions)
			controlOption.SwapMapping += RedrawControlOptions;

		foreach (ControlOption controlOption in partyMappingOptions)
			controlOption.SwapMapping += RedrawControlOptions;
	}

	private void RedrawControlOptions(StringName id, InputEvent e)
	{
		if (string.IsNullOrEmpty(id)) // Button was remapped to the same button
			CancelSFX();
		else // Actual remap has occurred
			ConfirmSFX();

		if (e == null)
			return;

		foreach (ControlOption controlOption in controlMappingOptions)
		{
			if (controlOption.ActionName == id)
				controlOption.ReceiveInput(e, true);
		}

		foreach (ControlOption controlOption in partyMappingOptions)
		{
			if (controlOption.ActionName == id)
				controlOption.ReceiveInput(e, true);
		}
	}

	private void UpdateHorizontalSelection()
	{
		if (Mathf.IsZeroApprox(Input.GetAxis("ui_left", "ui_right"))) return;

		int direction = Mathf.Sign(Input.GetAxis("ui_left", "ui_right"));

		if (currentSubmenu == Submenus.ResetSettings || currentSubmenu == Submenus.ResetControls)
		{
			if ((direction > 0 && resetSelection != 1) || (direction < 0 && resetSelection != 0))
			{
				resetSelection = direction > 0 ? 1 : 0;
				UpdateResetMenuVisuals();
			}
			return;
		}

		bool settingUpdated = false;
		switch (currentSubmenu)
		{
			case Submenus.Video:
				settingUpdated = SlideVideoOption(direction);
				break;
			case Submenus.Audio:
				settingUpdated = SlideAudioOption(direction);
				break;
			case Submenus.Language:
				settingUpdated = SlideLanguageOption(direction);
				break;
			case Submenus.Mouse:
				settingUpdated = SlideMouseOption(direction);
				break;
			case Submenus.Gyro:
				settingUpdated = SlideGyroOption(direction);
				break;
			case Submenus.Control:
				settingUpdated = SlideControlOption(direction);
				break;
			case Submenus.Interface:
				settingUpdated = SlideInterfaceOption(direction);
				break;
			case Submenus.Mods:
				settingUpdated = SlideModsOption(direction);
				break;
			case Submenus.PartyMapping:
				settingUpdated = SlidePartyMappingOption(direction);
				break;
		}

		if (!settingUpdated) return;

		ConfirmSFX();
		StartSelectionTimer();
		UpdateLabels();
		SaveManager.ApplyConfig();
	}

	private bool SlideVideoOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			if (DisplayServer.GetScreenCount() <= 1)
				return false;

			SaveManager.Config.targetDisplay = WrapSelection(SaveManager.Config.targetDisplay + direction, DisplayServer.GetScreenCount());
		}
		else if (VerticalSelection == 1)
		{
			SaveManager.Config.aspectRatio = (SaveManager.AspectRatio)WrapSelection((int)SaveManager.Config.aspectRatio + direction, (int)SaveManager.AspectRatio.Count);
			SaveManager.Config.windowSize = GetClosestWindowSizeClamped();

			if (SaveManager.Config.useFullscreen)
				SaveManager.Config.useFullscreen = !SaveManager.Config.useFullscreen;
		}
		else if (VerticalSelection == 2)
		{
			int fullscreenResolution = GetLargestWindowSize() + 1;

			// Switch out of fullscreen mode
			if (SaveManager.Config.useFullscreen)
			{
				SaveManager.Config.useFullscreen = !SaveManager.Config.useFullscreen;
				SaveManager.Config.windowSize = fullscreenResolution;
			}

			SaveManager.Config.windowSize += direction;

			// Prevent user from choosing an impossible resolution
			if (SaveManager.Config.windowSize < 0)
				SaveManager.Config.windowSize = fullscreenResolution;
			else if (SaveManager.Config.windowSize > fullscreenResolution)
				SaveManager.Config.windowSize = 0;

			// Enter fullscreen mode
			if (SaveManager.Config.windowSize == fullscreenResolution)
			{
				SaveManager.Config.useFullscreen = true;
				SaveManager.Config.windowSize = GetLargestWindowSize();
			}
		}
		else if (VerticalSelection == 3)
		{
			SaveManager.Config.useExclusiveFullscreen = !SaveManager.Config.useExclusiveFullscreen;
		}
		else if (VerticalSelection == 4)
		{
			SaveManager.Config.framerate = WrapSelection(SaveManager.Config.framerate + direction, SaveManager.FrameRates.Length);
		}
		else if (VerticalSelection == 5)
		{
			SaveManager.Config.useVsync = !SaveManager.Config.useVsync;
		}
		else if (VerticalSelection == 6)
		{
			SaveManager.Config.renderScale = WrapSelection(SaveManager.Config.renderScale + direction * 10, 150, 10);
		}
		else if (VerticalSelection == 7)
		{
			int resizeMode = (int)SaveManager.Config.resizeMode;
			// TODO Enable Metal rendering backend on AppleOS
			resizeMode = WrapSelection(resizeMode + direction, (int)RenderingServer.ViewportScaling3DMode.Fsr2 + 1);
			SaveManager.Config.resizeMode = (RenderingServer.ViewportScaling3DMode)resizeMode;
		}
		else if (VerticalSelection == 8)
		{
			SaveManager.Config.antiAliasing = WrapSelection(SaveManager.Config.antiAliasing + direction, 3);
		}
		else if (VerticalSelection == 9)
		{
			int bloomMode = (int)SaveManager.Config.bloomMode;
			bloomMode = WrapSelection(bloomMode + direction, (int)SaveManager.QualitySetting.Count);
			if (bloomMode == (int)SaveManager.QualitySetting.Medium) // Skip medium setting
				bloomMode = WrapSelection(bloomMode + direction, (int)SaveManager.QualitySetting.Count);
			SaveManager.Config.bloomMode = (SaveManager.QualitySetting)bloomMode;
		}
		else if (VerticalSelection == 10)
		{
			int softShadowQuality = (int)SaveManager.Config.softShadowQuality;
			softShadowQuality = WrapSelection(softShadowQuality + direction, (int)SaveManager.QualitySetting.Count);
			SaveManager.Config.softShadowQuality = (SaveManager.QualitySetting)softShadowQuality;
		}
		else if (VerticalSelection == 11)
		{
			int postProcessingQuality = (int)SaveManager.Config.postProcessingQuality;
			postProcessingQuality = WrapSelection(postProcessingQuality + direction, (int)SaveManager.QualitySetting.Count);
			SaveManager.Config.postProcessingQuality = (SaveManager.QualitySetting)postProcessingQuality;
			StageSettings.Instance.UpdateQualitySettings();
		}
		else if (VerticalSelection == 12)
		{
			int reflectionQuality = (int)SaveManager.Config.reflectionQuality;
			reflectionQuality = WrapSelection(reflectionQuality + direction, (int)SaveManager.QualitySetting.Count);
			SaveManager.Config.reflectionQuality = (SaveManager.QualitySetting)reflectionQuality;
		}
		else if (VerticalSelection == 13)
		{
			SaveManager.Config.useMotionBlur = !SaveManager.Config.useMotionBlur;
		}
		else if (VerticalSelection == 14)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.screenShake, direction))
				return false;

			SaveManager.Config.useScreenShake = true;
			SaveManager.Config.screenShake = SlidePercentage(SaveManager.Config.screenShake, direction);
		}

		return true;
	}

	private Vector2I[] GetWindowSizeArray()
	{
		Vector2I[] windows;
		switch (SaveManager.Config.aspectRatio)
		{
			case SaveManager.AspectRatio.FourByThree:
				windows = SaveManager.WindowSizes4x3;
				break;
			case SaveManager.AspectRatio.SixteenByTen:
				windows = SaveManager.WindowSizes16x10;
				break;
			case SaveManager.AspectRatio.TwentyoneByNine:
				windows = SaveManager.WindowSizes21x9;
				break;
			default:
				windows = SaveManager.WindowSizes;
				break;
		}

		return windows;
	}

	private int GetLargestWindowSize()
	{
		Vector2I[] windows = GetWindowSizeArray();
		for (int i = windows.Length - 1; i >= 0; i--)
		{
			if (windows[i] >= DisplayServer.ScreenGetSize())
				continue;

			return i;
		}

		return -1;
	}

	private int GetClosestWindowSize()
	{
		Vector2I[] windows = GetWindowSizeArray();
		Vector2I currentWindowSize = GetTree().Root.Size;
		int smallestDelta = int.MaxValue;
		int returnValue = windows.Length - 1;

		for (int i = windows.Length - 1; i >= 0; i--)
		{
			Vector2I currentSize = windows[i];
			int currentDelta = Mathf.Abs(currentWindowSize.X - currentSize.X) + Mathf.Abs(currentWindowSize.Y - currentSize.Y);
			if (currentDelta > smallestDelta)
				return returnValue;

			returnValue = i;
			smallestDelta = currentDelta;
		}

		return returnValue;
	}

	private int GetClosestWindowSizeClamped() => Mathf.Min(GetLargestWindowSize(), GetClosestWindowSize());

	private int GetWindowSize()
	{
		if (SaveManager.Config.useFullscreen) // Don't change when in fullscreen mode
			return SaveManager.Config.windowSize;

		Vector2I[] windows = GetWindowSizeArray();
		Vector2I currentWindowSize = GetTree().Root.Size;
		for (int i = 0; i < windows.Length; i++)
		{
			if (currentWindowSize == windows[i])
				return i;
		}

		return -1;
	}

	private bool SlideAudioOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.masterVolume, direction))
				return false;

			SaveManager.Config.isMasterMuted = false;
			SaveManager.Config.masterVolume = SlidePercentage(SaveManager.Config.masterVolume, direction);
		}
		else if (VerticalSelection == 1)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.bgmVolume, direction))
				return false;

			SaveManager.Config.isBgmMuted = false;
			SaveManager.Config.bgmVolume = SlidePercentage(SaveManager.Config.bgmVolume, direction);
		}
		else if (VerticalSelection == 2)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.sfxVolume, direction))
				return false;

			SaveManager.Config.isSfxMuted = false;
			SaveManager.Config.sfxVolume = SlidePercentage(SaveManager.Config.sfxVolume, direction);
		}
		else if (VerticalSelection == 3)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.voiceVolume, direction))
				return false;

			SaveManager.Config.isVoiceMuted = false;
			SaveManager.Config.voiceVolume = SlidePercentage(SaveManager.Config.voiceVolume, direction);
		}
		else
		{
			SaveManager.Config.useRetailMenuMusic = !SaveManager.Config.useRetailMenuMusic;
		}

		return true;
	}

	private int SlidePercentage(int current, int direction, int min = 0, int max = 100) => WrapSelection(current + direction * 5, max, min);
	private bool IsSlideVolumeValid(int current, int direction) => (current > 0 && direction == -1) || (current < 100 && direction == 1);

	private bool SlideLanguageOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			int lang = WrapSelection(SaveManager.GetCurrentTextLocaleIndex() + direction, SaveManager.Instance.TextLocalizations.Count);
			SaveManager.Config.textLocale = SaveManager.Instance.TextLocalizations[lang];
			return true;
		}

		if (VerticalSelection == 1)
		{
			int lang = WrapSelection(SaveManager.GetCurrentVoiceLocaleIndex() + direction, SaveManager.Instance.VoiceLocalizations.Count);
			SaveManager.Config.voiceLocale = SaveManager.Instance.VoiceLocalizations[lang];
			return true;
		}

		if (VerticalSelection == 2)
		{
			SaveManager.Config.isDialogDisabled = !SaveManager.Config.isDialogDisabled;
			return true;
		}

		if (VerticalSelection == 3)
		{
			SaveManager.Config.isSubtitleDisabled = !SaveManager.Config.isSubtitleDisabled;
			return true;
		}

		if (VerticalSelection == 4)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.subtitleOpacity, direction))
				return false;

			SaveManager.Config.subtitleOpacity = SlidePercentage(SaveManager.Config.subtitleOpacity, direction);
			return true;
		}

		if (VerticalSelection == 5)
		{
			if (!IsSlideVolumeValid(SaveManager.Config.cutsceneOpacity, direction))
				return false;

			SaveManager.Config.cutsceneOpacity = SlidePercentage(SaveManager.Config.cutsceneOpacity, direction);
			return true;
		}

		return false;
	}

	private bool SlideControlOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			float deadZone = SaveManager.Config.deadZone;
			deadZone = WrapSelection(deadZone + (direction * .1f), .9f, .1f);
			SaveManager.Config.deadZone = deadZone;
			SaveManager.ApplyInputMap();
			return true;
		}
		else if (VerticalSelection == 1)
		{
			SaveManager.Config.useHoldBreakMode = !SaveManager.Config.useHoldBreakMode;
			return true;
		}
		else if (VerticalSelection == 2)
		{
			SaveManager.Config.jumpButtonMode = (SaveManager.JumpButtonModeEnum)WrapSelection((int)SaveManager.Config.jumpButtonMode + direction, (int)SaveManager.JumpButtonModeEnum.Count);
			return true;
		}

		return false;
	}

	private bool SlideInterfaceOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			SaveManager.Config.useProjectReignitionBranding = !SaveManager.Config.useProjectReignitionBranding;
			return true;
		}
		else if (VerticalSelection == 1)
		{
			// TODO Add E3 HUD option
			if (SaveManager.Config.hudStyle == SaveManager.HudStyle.Reignition)
				SaveManager.Config.hudStyle = SaveManager.HudStyle.Retail;
			else
				SaveManager.Config.hudStyle = SaveManager.HudStyle.Reignition;
			return true;
		}
		else if (VerticalSelection == 2)
		{
			int type = WrapSelection((int)SaveManager.Config.controllerType + direction, (int)SaveManager.ControllerType.Count);
			SaveManager.Config.controllerType = (SaveManager.ControllerType)type;
			return true;
		}
		else if (VerticalSelection == 3)
		{
			int style = WrapSelection((int)SaveManager.Config.buttonStyle + direction, (int)SaveManager.ButtonStyle.Count);
			SaveManager.Config.buttonStyle = (SaveManager.ButtonStyle)style;
			return true;
		}
		else if (VerticalSelection == 4)
		{
			SaveManager.Config.isUsingHorizontalSoulGauge = !SaveManager.Config.isUsingHorizontalSoulGauge;
			return true;
		}
		else if (VerticalSelection == 5)
		{
			SaveManager.Config.isActionPromptsEnabled = !SaveManager.Config.isActionPromptsEnabled;
			return true;
		}

		return false;
	}

	private bool SlideModsOption(int _)
	{
		if (VerticalSelection == 0)
			SaveManager.Config.areLevelModsEnabled = !SaveManager.Config.areLevelModsEnabled;
		else if (VerticalSelection == 1)
			SaveManager.Config.areCharaModsEnabled = !SaveManager.Config.areCharaModsEnabled;
		else if (VerticalSelection == 2)
			SaveManager.Config.areLangModsEnabled = !SaveManager.Config.areLangModsEnabled;
		else if (VerticalSelection == 3)
			OS.ShellOpen(ProjectSettings.GlobalizePath("user://mods/"));

		return true;
	}

	private bool SlideMouseOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			SaveManager.Config.mouseControlMode =
				(SaveManager.MouseControlModeEnum)WrapSelection((int)SaveManager.Config.mouseControlMode + direction,
				(int)SaveManager.MouseControlModeEnum.Count);
			return true;
		}
		else if (VerticalSelection == 1)
		{
			SaveManager.Config.isMouseVerticalEnabled = !SaveManager.Config.isMouseVerticalEnabled;
			return true;
		}
		else if (VerticalSelection == 2)
		{
			SaveManager.Config.mouseSensitivity = SlidePercentage(SaveManager.Config.mouseSensitivity, direction, 0, 200);
			return true;
		}
		else if (VerticalSelection == 3)
		{
			SaveManager.Config.mouseDeadzone = SlidePercentage(SaveManager.Config.mouseDeadzone, direction);
			SaveManager.Config.mouseHorizontalRange = Mathf.Max(SaveManager.Config.mouseHorizontalRange, SaveManager.Config.mouseDeadzone);
			SaveManager.Config.mouseVerticalRange = Mathf.Max(SaveManager.Config.mouseVerticalRange, SaveManager.Config.mouseDeadzone);
			return true;
		}
		else if (VerticalSelection == 4)
		{
			SaveManager.Config.mouseHorizontalRange = SlidePercentage(SaveManager.Config.mouseHorizontalRange, direction);
			return true;
		}
		else if (VerticalSelection == 5)
		{
			SaveManager.Config.mouseVerticalRange = SlidePercentage(SaveManager.Config.mouseVerticalRange, direction);
			return true;
		}
		else if (VerticalSelection == 6)
		{
			SaveManager.Config.mouseVerticalOffset = SlidePercentage(SaveManager.Config.mouseVerticalOffset, direction, -100);
			return true;
		}

		return false;
	}

	private bool SlideGyroOption(int direction)
	{
		if (VerticalSelection == 0)
		{
			SaveManager.Config.isGyroEnabled = !SaveManager.Config.isGyroEnabled;
			return true;
		}
		else if (VerticalSelection == 1)
		{
			SaveManager.Config.gyroSensitivity = WrapSelection(SaveManager.Config.gyroSensitivity + (direction * 10), 200);
			return true;
		}

		return false;
	}

	private bool SlidePartyMappingOption(int direction)
	{
		if (VerticalSelection < ExtraPartyModeOptionCount)
		{
			if (VerticalSelection == 0)
			{
				// Change player index
				partyPlayerIndex += direction;
				if (partyPlayerIndex > 4)
					partyPlayerIndex = 1;
				else if (partyPlayerIndex < 1)
					partyPlayerIndex = 4;
				foreach (ControlOption controlOption in partyMappingOptions)
				{
					controlOption.PartyModeControllerIndex = partyPlayerIndex;
					controlOption.RedrawBinding();
				}
			}

			UpdatePartyModeDevice(direction);
			return true;
		}

		return false;
	}

	private void UpdatePartyModeDevice(int direction)
	{
		// Change device
		if (VerticalSelection == 1)
		{
			int deviceIndex = SaveManager.Config.partyModeDevices[partyPlayerIndex - 1];
			deviceIndex += direction;
			if (deviceIndex < 0)
				deviceIndex = 7;
			else if (deviceIndex > 7)
				deviceIndex = 0;
			SaveManager.Config.partyModeDevices[partyPlayerIndex - 1] = deviceIndex;
		}

		foreach (ControlOption controlOption in controlMappingOptions)
			controlOption.UpdateDevice();

		foreach (ControlOption controlOption in partyMappingOptions)
			controlOption.UpdateDevice();
	}

	private void ConfirmOption()
	{
		if (VerticalSelection == 6)
		{
			currentSubmenu = Submenus.ResetSettings;
			ShowResetMenu();
			return;
		}

		ConfirmSFX();
		FlipBook((Submenus)VerticalSelection + 1, false, 0);
	}

	private void ShowResetMenu()
	{
		if (Runtime.Instance.IsUsingMouse)
		{
			resetAnimator.Play("select-none");
			resetSelection = -1;
		}
		else
		{
			resetAnimator.Play("select-no");
			resetSelection = 1;
		}
		resetAnimator.Advance(0.0);

		resetAnimator.Play(currentSubmenu == Submenus.ResetSettings ? "text-settings" : "text-controls");
		resetAnimator.Advance(0.0);
		resetAnimator.Play("show");
	}

	private void ConfirmVideoOption()
	{
		if (VerticalSelection == 2) // Toggle fullscreen mode
		{
			SaveManager.Config.useFullscreen = !SaveManager.Config.useFullscreen;
			SaveManager.Config.windowSize = GetLargestWindowSize();
		}
		else if (VerticalSelection == 14)
		{
			SaveManager.Config.useScreenShake = !SaveManager.Config.useScreenShake;
		}
		else
		{
			SlideVideoOption(1);
		}

		ConfirmSFX();
	}

	private void ConfirmAudioOption()
	{
		if (VerticalSelection == 0)
			SaveManager.Config.isMasterMuted = !SaveManager.Config.isMasterMuted;
		else if (VerticalSelection == 1)
			SaveManager.Config.isBgmMuted = !SaveManager.Config.isBgmMuted;
		else if (VerticalSelection == 2)
			SaveManager.Config.isSfxMuted = !SaveManager.Config.isSfxMuted;
		else if (VerticalSelection == 3)
			SaveManager.Config.isVoiceMuted = !SaveManager.Config.isVoiceMuted;
		else
			SlideAudioOption(1);

		ConfirmSFX();
	}

	private void ConfirmControlOption()
	{
		switch (VerticalSelection)
		{
			case 3:
				FlipBook(Submenus.Mouse, false, 0);
				break;
			case 4:
				FlipBook(Submenus.Gyro, false, 0);
				break;
			case 5:
				FlipBook(Submenus.Mapping, false, 0);
				break;
			case 6:
				FlipBook(Submenus.PartyMapping, false, 0);
				break;
			case 7:
				FlipBook(Submenus.Test, false, VerticalSelection);
				break;
			case 8:
				currentSubmenu = Submenus.ResetControls;
				ShowResetMenu();
				return;
			default:
				SlideControlOption(1);
				break;
		}

		ConfirmSFX();
	}

	private void ConfirmInterfaceOption()
	{
		SlideInterfaceOption(1);
		ConfirmSFX();
	}

	private void ConfirmModsOption()
	{
		if (!DirAccess.DirExistsAbsolute(SaveManager.ModDirectory))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.ModDirectory);

		if (!DirAccess.DirExistsAbsolute(SaveManager.MusicModDirectory))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.MusicModDirectory);

		if (!DirAccess.DirExistsAbsolute(SaveManager.LevelsModDirectory))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.LevelsModDirectory);

		if (!DirAccess.DirExistsAbsolute(SaveManager.LangModDirectory))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.LangModDirectory);

		if (!DirAccess.DirExistsAbsolute(SaveManager.CharactersModDirectory))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.CharactersModDirectory);

		if (!DirAccess.DirExistsAbsolute(SaveManager.ExtrasModDirectory))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.ExtrasModDirectory);

		SlideModsOption(1);
		ConfirmSFX();
	}

	private void ConfirmMouseOption()
	{
		SlideMouseOption(1);
		ConfirmSFX();
	}

	private void ConfirmGyroOption()
	{
		SlideGyroOption(1);
		ConfirmSFX();
	}

	private void ConfirmSFX()
	{
		animator.Play("confirm");
		animator.Advance(0.0);
	}

	private void CancelSFX()
	{
		animator.Play("cancel");
		animator.Advance(0.0);
	}

	private void ReceiveMouseHorizontalInput(int selection)
	{
		if (!isProcessing)
			return;

		if (currentSubmenu == Submenus.ResetSettings || currentSubmenu == Submenus.ResetControls)
		{
			resetSelection = selection;
			UpdateResetMenuVisuals();
			Runtime.Instance.IsUsingMouse = true;
			return;
		}
	}

	private void ReceiveMouseInput(Node selection)
	{
		if (!isProcessing)
			return;

		if (currentSubmenu == Submenus.ResetSettings || currentSubmenu == Submenus.ResetControls)
			return;

		if (selection == null)
		{
			isCursorHidden = true;
			cursorAnimator.Play("hide");
			return;
		}

		Runtime.Instance.IsUsingMouse = true;
		VerticalSelection = selection.GetIndex();
		UpdateVerticalSelection();
	}

	private void UpdateResetMenuVisuals()
	{
		if (resetSelection == -1)
		{
			resetAnimator.Play("select-none");
			return;
		}

		resetAnimator.Play(resetSelection == 0 ? "select-yes" : "select-no");
	}

	/// <summary> Connects all the mouse signals programmatically. </summary>
	private void ConnectMouseSignals()
	{
		foreach (Control node in mouseInputParents)
			ConnectMouseSignals(node);
	}

	private void ConnectMouseSignals(Node parentNode)
	{
		for (int i = 0; i < parentNode.GetChildCount(); i++)
		{
			Control node = parentNode.GetChildOrNull<Control>(i);

			if (node == null)
				continue;

			node.MouseFilter = MouseFilterEnum.Stop;
			node.MouseEntered += () => ReceiveMouseInput(node);
			node.MouseExited += () => ReceiveMouseInput(null);
		}
	}
}
