using Godot;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface;

public partial class PauseMenu : Node
{
	public static bool AllowInputs = true;
	private bool isNothingSelected;

	[Signal] public delegate void OnSceneChangeSelectedEventHandler();

	[Export] private Control mouseOptionParent;
	[Export] AnimationPlayer pageAnimator;
	[Export] AnimationPlayer statusAnimator;
	[Export] AnimationPlayer selectionAnimator;

	[Export] private AnimationPlayer pauseCursorAnimator;
	[Export] private AudioStreamPlayer selectSfx;
	[Export] private AudioStreamPlayer cancelSfx;
	[Export] private Menus.Description description;
	[Export] private Label restart;

	[ExportGroup("Status Menu")]
	[Export] private Label[] values;

	[ExportSubgroup("Mission Menu")]
	[Export] private Label missionTypeLabel;
	[Export] private Label missionDescriptionLabel;
	[Export] private Control fireSoulParent;
	[Export] private TextureRect[] fireSoulRects;
	[Export] private Texture2D fireSoulSprite;
	[Export] private Texture2D noFireSoulSprite;
	[Export] private TextureRect rankRect;
	[Export] private Texture2D[] rankSprites;

	[ExportSubgroup("Skill Menu")]
	[Export] private Label noSkillLabel;
	[Export] private VBoxContainer skillContainer;
	[Export] private Control skillCursor;
	[Export] private AnimationPlayer skillCursorAnimator;
	[Export] private PackedScene pauseSkillScene;
	[Export] private Label areaLabel;
	private PauseSkill[] skills;
	private float skillContainerStartingOffset;
	private int skillScrollOffset;
	[Export] private Sprite2D skillScrollbar;
	private Vector2 scrollVelocity;
	private readonly float ScrollSmoothing = .05f;
	private readonly float SkillScrollInterval = 60;


	[ExportGroup("Control Methods")]
	[Export] private AnimationPlayer controlMethodAnimator;
	private bool isControlMethodToggleAvailable;

	private bool isActive;
	private enum Submenu
	{
		Pause,
		Skill
	}
	private Submenu submenu;
	private StageSettings Stage => StageSettings.Instance;

	private bool isConfirmButtonBuffered;
	private bool isCancelButtonBuffered;
	private bool isHidden;

	public override void _Ready()
	{
		pageAnimator.Play("init");
		pageAnimator.Advance(0.0);

		for (int i = 0; i < mouseOptionParent.GetChildCount(); i++)
		{
			Control node = mouseOptionParent.GetChildOrNull<Control>(i);
			node.MouseEntered += () => ReceiveMouseInput(node, false);
			node.MouseExited += () => ReceiveMouseInput(null, false);
		}

		areaLabel.Text = Stage.Data.GetAreaKey();

		// Set up the skill menu
		noSkillLabel.Visible = SaveManager.ActiveSkillRing.EquippedSkills.Count == 0;
		skillScrollbar.GetParent<NinePatchRect>().Visible = SaveManager.ActiveSkillRing.EquippedSkills.Count != 0;

		skills = new PauseSkill[SaveManager.ActiveSkillRing.EquippedSkills.Count];
		skillContainerStartingOffset = skillContainer.Position.Y;
		skillContainer.SetDeferred("size", new Vector2(skillContainer.Size.X, skills.Length * 60));

		// Generate skill list
		foreach (SkillKey key in SaveManager.ActiveSkillRing.EquippedSkills)
		{
			PauseSkill pauseSkill = pauseSkillScene.Instantiate<PauseSkill>();
			pauseSkill.Skill = Runtime.Instance.SkillList.GetSkill(key);
			if (pauseSkill.Skill.HasAugments)
				pauseSkill.Skill = pauseSkill.Skill.GetAugment(SaveManager.ActiveSkillRing.GetAugmentIndex(key));
			pauseSkill.Initialize();
			pauseSkill.MouseEntered += () => ReceiveMouseInput(pauseSkill, true);
			skillContainer.AddChild(pauseSkill);
		}

		isHidden = false;

		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType != TimeAttackManager.RunType.SingleRun)
		{
			Label glow = restart.GetChild(0) as Label;
			restart.Text = "ta_restartrun";
			glow.Text = "ta_restartrun";

		}
	}

	public override void _PhysicsProcess(double _)
	{
		if (!AllowInputs || !Stage.IsLevelIngame || TransitionManager.IsTransitionActive) return;

		if (skillScrollbar.IsVisibleInTree())
			UpdateSkillScrollbar();

		if (Runtime.Instance.IsActionJustPressed("sys_pause", "ui_accept") &&
			!Input.IsActionJustPressed("toggle_fullscreen"))
		{
			if (IsQuickRestart())
				return;

			CallDeferred(MethodName.TogglePause);
			return;
		}

		if (!GetTree().Paused)
			return;

		UpdateBuffers();

		if (!canMoveCursor)
			return;

		if (Runtime.Instance.IsActionJustPressed("sys_sort", "sys_sort"))
		{
			ToggleControlMethod();
			return;
		}

		if (isConfirmButtonBuffered)
		{
			isConfirmButtonBuffered = false;
			Confirm();
			return;
		}

		if (isCancelButtonBuffered)
		{
			isCancelButtonBuffered = false;
			Cancel();
			return;
		}

		int sign = Mathf.Sign(Input.GetAxis("ui_up", "ui_down"));
		if (sign != 0)
			ChangeSelection(sign);
	}

	private bool IsQuickRestart()
	{
		if (!Input.IsActionPressed("button_step_left") || !Input.IsActionPressed("button_step_right")) // Quick restart
			return false;

		TransitionManager.QueueSceneChange(string.Empty);
		TransitionManager.StartTransition(new TransitionData()
		{
			color = Colors.Black,
			inSpeed = 0.2f,
			outSpeed = 0.5f,
			loadAsynchronously = true,
			disableAutoTransition = true
		});

		return true;
	}

	private void UpdateSkillScrollbar()
	{
		float denominator = SaveManager.ActiveSkillRing.EquippedSkills.Count - 1;
		if (denominator <= 0)
			return;

		float targetPosition = 312 * (skillSelection / denominator);
		skillScrollbar.Position = skillScrollbar.Position.SmoothDamp(Vector2.Right * targetPosition, ref scrollVelocity, ScrollSmoothing);
	}

	private void UpdateBuffers()
	{
		if (isNothingSelected)
			return;

		if (Runtime.Instance.IsActionJustPressed("sys_select", "ui_select") ||
			(Runtime.Instance.IsUsingMouse && Input.IsActionJustPressed("mouse_left")))
		{
			isConfirmButtonBuffered = true;
			isCancelButtonBuffered = false;
		}

		if (Runtime.Instance.IsActionJustPressed("sys_cancel", "ui_cancel", "escape"))
		{
			isConfirmButtonBuffered = false;
			isCancelButtonBuffered = true;
		}
	}

	private void OnControllerChanged(int controllerIndex)
	{
		if (Runtime.Instance.IsUsingController)
			isControlMethodToggleAvailable = Input.HasJoyMotionSensors(controllerIndex);
		else
			isControlMethodToggleAvailable = true;

		controlMethodAnimator.Play(isControlMethodToggleAvailable ? "show" : "hide");
		controlMethodAnimator.Advance(0.0);

		if (!isControlMethodToggleAvailable) // Gyro is unavailable
			SaveManager.Config.isGyroEnabled = false;

		UpdateControlLabels();
	}

	private void ToggleControlMethod()
	{
		if (!isControlMethodToggleAvailable)
			return;

		if (Runtime.Instance.IsUsingController)
			SaveManager.Config.isGyroEnabled = !SaveManager.Config.isGyroEnabled;
		else
		{
			int value = WrapSelection((int)SaveManager.Config.mouseControlMode + 1, (int)SaveManager.MouseControlModeEnum.Count);
			SaveManager.Config.mouseControlMode = (SaveManager.MouseControlModeEnum)value;
			GD.Print(SaveManager.Config.mouseControlMode);
		}

		selectSfx.Play();
		UpdateControlLabels();
	}

	private void UpdateControlLabels()
	{
		if (Runtime.Instance.IsUsingController)
			controlMethodAnimator.Play(SaveManager.Config.isGyroEnabled ? "gyro-enable" : "gyro-disable");
		else
		{
			GD.Print($"mouse-{SaveManager.Config.mouseControlMode}".ToLower());
			controlMethodAnimator.Play($"mouse-{SaveManager.Config.mouseControlMode}".ToLower());
		}
	}

	private void Confirm()
	{
		if (submenu == Submenu.Skill)
			return;

		if (currentSelection == 2) // You can't select the status menu
			return;

		AllowInputs = false;
		selectionAnimator.Play($"confirm-{currentSelection}");
	}

	private void Cancel()
	{
		isCancelButtonBuffered = false;

		if (submenu == Submenu.Pause)
		{
			TogglePause();
			return;
		}

		submenu = Submenu.Pause;
		skillCursorAnimator.Play("hide");
		pauseCursorAnimator.Play("show");
		selectionAnimator.Play("show-skill");
		currentSelection = 3;

		if (skills.Length != 0)
			description.HideDescription();
	}

	private void ChangeSelection(int direction)
	{
		canMoveCursor = false;
		Runtime.Instance.IsUsingMouse = false;
		if (isNothingSelected)
		{
			UpdateSelection(currentSelection, true);
			return;
		}

		int targetSelection = currentSelection + direction;
		if (submenu == Submenu.Skill) // Allow wrapping when viewing skills
		{
			if (targetSelection < 0)
				targetSelection = skills.Length - 1;
			else if (targetSelection == skills.Length)
				targetSelection = 0;
		}
		else
		{
			targetSelection = WrapSelection(targetSelection, 5);
		}

		if (targetSelection != currentSelection)
			UpdateSelection(targetSelection, true);
	}

	private int WrapSelection(int selection, int max)
	{
		selection %= max;
		if (selection < 0)
			selection += max;
		else if (selection >= max)
			selection -= max;

		return selection;
	}

	/// <summary> Actually applies the current selection (called from the animator). </summary>
	private void ApplySelection()
	{
		if (submenu != Submenu.Pause || currentSelection == 2)
			return;

		if (currentSelection == 0) // Resume
		{
			isActive = false;
			ApplyPause();
		}
		else if (currentSelection == 1) // Restart
		{
			if (TimeAttackManager.Instance.IsRunActive)
			{
				TimeAttackManager.Instance.RestartRun();
				if (TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
					ReloadStage();
			}
			else
			{
				ReloadStage();
			}
		}
		else if (currentSelection == 3) // Open the Skill Menu
		{
			submenu = Submenu.Skill;
			pauseCursorAnimator.Play("hide");
			skillCursorAnimator.Advance(0.0);
			UpdateSelection(skillSelection); // Remember previously selected skill
		}
		else if (currentSelection == 4) // Quit by opening the EXP menu
		{
			SaveManager.SaveGameData();
			SoundManager.instance.StageMusicPlayer.Stop();
			SoundManager.instance.StageMusicPlayer.SetBgmResource(null); //Fixes a bug where the stage music will continue playing after a level quit
			if (TimeAttackManager.Instance.IsRunActive)
			{
				if (TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
					TimeAttackManager.Instance.LoadTimeAttack(true);
				else
					TimeAttackManager.Instance.LoadTimeAttack();
			}
			else
			{
				TransitionManager.Instance.QueuedScene = TransitionManager.MenuScenePath;
				EmitSignal(SignalName.OnSceneChangeSelected);
			}
		}
	}

	private void ReloadStage()
	{
		TransitionManager.Instance.QueuedScene = string.Empty;
		SoundManager.instance.StageMusicPlayer.Stop();
		EmitSignal(SignalName.OnSceneChangeSelected);
	}

	private void UpdateStatusMenuData()
	{
		// Status menu
		values[0].Text = SaveManager.ActiveGameData.level.ToString("00");
		values[1].Text = "×" + Stage.CurrentRingCount.ToString("000") + "/999";
		values[2].Text = ExtensionMethods.FormatMenuNumber(SaveManager.ActiveGameData.LevelData.GetHighScore(Stage.Data.LevelID));
		values[3].Text = ExtensionMethods.FormatMenuNumber(Stage.TotalScore);
		values[4].Text = ExtensionMethods.FormatTime(SaveManager.ActiveGameData.LevelData.GetBestTime(Stage.Data.LevelID));
		values[5].Text = Stage.DisplayTime;
		values[6].Text = ExtensionMethods.FormatMenuNumber(Stage.CurrentEXP);
		values[7].Text = ExtensionMethods.FormatMenuNumber(SaveManager.ActiveGameData.exp);
		values[8].Text = StageSettings.Player.Skills.TextDisplay;

		// Mission menu
		missionTypeLabel.Text = Stage.Data.MissionTypeKey;
		missionDescriptionLabel.Text = Stage.Data.MissionDescriptionKey;

		UpdateFireSouls();
		int rank = SaveManager.ActiveGameData.LevelData.GetRankClamped(Stage.Data.LevelID);
		rankRect.Texture = rankSprites[rank];
	}

	private void UpdateFireSouls()
	{
		fireSoulParent.Visible = Stage.Data.HasFireSouls;
		if (!Stage.Data.HasFireSouls)
			return;

		for (int i = 0; i < fireSoulRects.Length; i++)
		{
			bool isSaveCollected = SaveManager.ActiveGameData.LevelData.IsFireSoulCollected(Stage.Data.LevelID, i + 1);
			bool isCheckpointCollected = StageSettings.Instance.IsFireSoulCheckpointFlagSet(i);

			fireSoulRects[i].Texture = (isSaveCollected || isCheckpointCollected) ? fireSoulSprite : noFireSoulSprite;
			fireSoulRects[i].SelfModulate = (isCheckpointCollected && !isSaveCollected) ? new(1f, 1f, 1f, .5f) : Colors.White;
		}
	}

	/// <summary> Selected menu option. </summary>
	private int currentSelection;
	/// <summary> Current Selected skill option. </summary>
	private int skillSelection;
	/// <summary> Can the cursor currently be moved? </summary>
	private bool canMoveCursor;
	private void EnableCursorMovement()
	{
		canMoveCursor = true;
		if (submenu == Submenu.Skill)
			skillCursorAnimator.Play("loop");
	}
	private void UpdateCursorPosition()
	{
		if (submenu == Submenu.Pause)
		{
			pauseCursorAnimator.Play("show");
			pauseCursorAnimator.Seek(0.0, true);
			return;
		}

		int visualSelection = skillSelection - skillScrollOffset;
		if (skillSelection > skillScrollOffset + 6)
		{
			visualSelection = 6;
			skillScrollOffset = skillSelection - visualSelection;
		}
		else if (skillSelection < skillScrollOffset)
		{
			visualSelection = 0;
			skillScrollOffset = skillSelection;
		}

		skillContainer.Position = Vector2.Up * (skillScrollOffset * SkillScrollInterval - skillContainerStartingOffset);
		skillCursor.Position = Vector2.Down * visualSelection * SkillScrollInterval;
		skillCursorAnimator.Play("show");
		skillCursorAnimator.Seek(0.0, true);
	}

	private void UpdateSelection(int selection, bool playSFX = default)
	{
		if (playSFX)
			selectSfx.Play();

		isNothingSelected = false;
		currentSelection = selection;

		if (submenu == Submenu.Pause)
		{
			pauseCursorAnimator.Play("move");
			selectionAnimator.Play($"select-{selection}", 0.1);
			selectionAnimator.Advance(0.0);

			UpdateStatusMenu();
			return;
		}

		skillSelection = selection;
		UpdateCursorPosition();
		UpdateSkillDescription();
	}

	private void UpdateStatusMenu()
	{
		string targetAnimation = "mission";
		if (currentSelection == 2)
			targetAnimation = "status";
		else if (currentSelection == 3)
			targetAnimation = "skill";

		if (statusAnimator.AssignedAnimation.Equals(targetAnimation))
			return;

		statusAnimator.Play(targetAnimation);
	}

	private void UpdateSkillDescription()
	{
		PauseSkill pauseSkill = skillContainer.GetChild<PauseSkill>(currentSelection);
		if (pauseSkill == null)
			return;

		description.Text = pauseSkill.Skill.DescriptionKey;
		description.ShowDescription();
	}

	private void EnableInteraction() => AllowInputs = true;
	private float unpausedSpeed;
	private void TogglePause()
	{
		if (!Stage.IsLevelIngame && !isActive) // Prevent softlock when pausing on the same frame as completing a mission
			return;

		submenu = Submenu.Pause;
		canMoveCursor = false; // Disable cursor movement
		AllowInputs = false; // Disable pause inputs during the animation

		isActive = !isActive;
		pageAnimator.Play(isActive ? "show" : "hide");
		statusAnimator.Play(isActive ? "mission" : "hide");

		if (isActive) // Reset selection
		{
			// Reset cursors
			skillCursorAnimator.Play("RESET");
			selectionAnimator.Play("RESET");
			selectionAnimator.Advance(0.0);
			selectionAnimator.Play("show");

			UpdateSelection(0);
			UpdateCursorPosition();
			UpdateStatusMenuData();
			unpausedSpeed = (float)Engine.TimeScale;
			Engine.TimeScale = 1.0f;
			OnControllerChanged(Runtime.Instance.ActiveController);
			Runtime.Instance.ControllerChanged += OnControllerChanged;
		}
		else if (!TransitionManager.IsTransitionActive)
		{
			if (!cancelSfx.Playing)
				cancelSfx.Play();

			Engine.TimeScale = unpausedSpeed;
			Runtime.Instance.ControllerChanged -= OnControllerChanged;
		}
	}

	private void ApplyPause()
	{
		GetTree().Paused = isActive;
		SoundManager.instance.IsStageMusicPaused = isActive;
	}

	private void ReceiveMouseInput(Node node, bool isSkill)
	{
		if (!AllowInputs || !isActive)
			return;

		if (submenu == Submenu.Skill)
		{
			if (!isSkill)
				return;

			UpdateSelection(node.GetIndex());
			Runtime.Instance.IsUsingMouse = true;
			return;
		}

		if (isSkill)
			return;

		if (node == null)
		{
			pauseCursorAnimator.Play("hide");
			selectionAnimator.Play($"select-none", 0.1);
			isNothingSelected = true;
			return;
		}

		UpdateSelection(node.GetIndex());
		Runtime.Instance.IsUsingMouse = true;
	}
}