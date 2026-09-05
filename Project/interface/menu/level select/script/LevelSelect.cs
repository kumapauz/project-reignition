using Godot;
using Project.Core;
using Project.Gameplay;
using System.Collections.Generic;

namespace Project.Interface.Menus;

public partial class LevelSelect : Menu
{
	[Export] private SaveManager.WorldEnum world;
	[Export] private string areaKey;
	[Export] private Description description;
	[Export] private ReadyMenu readyMenu;
	[Export] private StatusMenu statusMenu;

	[Export] private Control cursor;
	[Export] private AnimationPlayer cursorAnimator;
	[Export] private Control navigationButtons;
	private float initialCursorPosition;
	private int cursorPosition;
	private Vector2 cursorWidthVelocity;
	private bool isNothingSelected;

	[Export] private Control options;
	private Vector2 optionVelocity;
	[Export] private Sprite2D scrollbar;
	[Export] private AnimationPlayer storyMarkerAnimator;

	public bool ContainsNewStage { get; private set; }

	private int scrollAmount;
	private float scrollRatio;
	private Vector2 scrollVelocity;

	[Export] private Jukebox jukebox;
	[Export] bool isModWorld = false;
	[Export] PackedScene levelOption;
	[Export] LevelDataResource defaultLevelModOption;
	[Export] Control nav_delete;

	private readonly int PageSize = 5;
	private const float ScrollSmoothing = .05f;
	private readonly List<LevelOption> levelOptions = [];

	[Export] private AnimationPlayer alertAnimator;
	private bool isAlertMenuActive = false;
	private bool isYesSelected = false;
	

	public bool HasNewLevel()
	{
		if (world == SaveManager.WorldEnum.Mods) // Don't track mod levels
			return false;

		foreach (Node node in options.GetChildren())
		{
			if (node is LevelOption levelOption)
			{
				levelOption.UpdateLevelData();

				if (levelOption.IsUnlocked && levelOption.ClearState == SaveManager.LevelSaveData.LevelStatus.New)
					return true;
			}
		}

		return false;
	}

	public bool IsWorldUnlocked()
	{
		if (DebugManager.Instance.UseDemoSave || TimeAttackManager.Instance.IsRunActive) //Fixes bug where time attack would cause stages to remain unlocked in story
		{
			/// For the demo, assume the world is unlocked if a stage is available to play.
			foreach (Node node in options.GetChildren())
			{
				if (node is LevelOption levelOption)
				{
					if (levelOption.IsUnlocked)
						return true;
				}
			}

			return false;
		}

		// For the full release--use the actual save data
		return SaveManager.ActiveGameData.IsWorldUnlocked(world);
	}

	protected override void SetUp()
	{
		if (isModWorld)
			ModSetUp();

		foreach (Node node in options.GetChildren())
		{
			if (node is LevelOption levelOption)
			{
				levelOption.MouseEntered += () => ReceiveMouseInput(levelOption);
				levelOption.MouseExited += () => ReceiveMouseInput(null);
				levelOptions.Add(levelOption);
			}
		}

		initialCursorPosition = cursor.Position.Y;
		jukebox.Closed += ProcessStoryMarkers;
		base.SetUp();
	}

	private void ModSetUp()
	{
		if (ModManager.Instance.LevelMods.Count > 0)
		{
			foreach (LevelDataResource mod in ModManager.Instance.LevelMods)
			{
				LevelOption newOption = levelOption.Instantiate<LevelOption>();
				newOption.data = mod;
				options.AddChild(newOption);
			}
		}
		else
		{
			LevelOption defaultOption = levelOption.Instantiate<LevelOption>();
			defaultOption.data = defaultLevelModOption;
			options.AddChild(defaultOption);
		}

	}

	protected override void ProcessMenu()
	{
		

		if (statusMenu != null && statusMenu.IsVisibleInTree() && isAlertMenuActive)
			return;

		if (Runtime.Instance.MouseScrollInput != 0)
		{
			VerticalSelection = Mathf.Clamp(VerticalSelection + Runtime.Instance.MouseScrollInput, 0, levelOptions.Count - 1);
			isNothingSelected = false;
			cursorAnimator.Play("loop");
			ChangeSelection();
			return;
		}

		// Quick scrolling
		if (Input.IsActionJustPressed("button_step_left"))
		{
			int targetSelection = Mathf.Max(VerticalSelection - (PageSize - 1), 0);
			while (VerticalSelection != targetSelection)
			{
				VerticalSelection = (int)Mathf.MoveToward(VerticalSelection, targetSelection, 1);
				ChangeSelection();
			}
			return;
		}

		if (Input.IsActionJustPressed("button_step_right"))
		{
			GD.Print("Scrolling down!");
			int targetSelection = Mathf.Min(VerticalSelection + (PageSize - 1), levelOptions.Count - 1);
			while (VerticalSelection != targetSelection)
			{
				VerticalSelection = (int)Mathf.MoveToward(VerticalSelection, targetSelection, 1);
				ChangeSelection();
			}
			return;
		}

		if (Input.IsActionJustPressed("ui_text_delete") && !isAlertMenuActive)
		{
			if (TimeAttackManager.Instance.IsRunActive)
				ShowAlertMenu();
		}

		if (levelOptions[VerticalSelection].IsUnlocked)
		{
			if (Runtime.Instance.IsActionJustPressed("sys_pause", "ui_accept") && menuMemory[MemoryKeys.ActiveMenu] != (int)MemoryKeys.TimeAttack)
			{
				if (isModWorld && ModManager.Instance.LevelMods.Count == 0) //Don't open the bgm menu when we don't have any mods
					return;
				menuMemory[MemoryKeys.ActiveMenu] = (int)MemoryKeys.Jukebox;
				OpenBGMMenu();
				DisableProcessing();
			}
		}

		base.ProcessMenu();
		UpdateListPosition(ScrollSmoothing);
	}

	public override void ShowMenu()
	{
		if (menuMemory[MemoryKeys.ActiveMenu] == (int)MemoryKeys.TimeAttack)
			base.SetUp();

		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
			nav_delete.Visible = true;
		else
			nav_delete.Visible = false;
			

		VerticalSelection = menuMemory[MemoryKeys.LevelSelect];
		RecalculateListPosition();
		UpdateListPosition(0);

		if (Runtime.Instance.IsUsingMouse)
		{
			isNothingSelected = true;
			cursorAnimator.Play("hide");
		}
		else
		{
			isNothingSelected = false;
			cursorAnimator.Play("loop");
		}
		cursorAnimator.Advance(0.0);

		animator.Play("show");
		storyLevelIndex = -1;
		storyMarkerAnimator.Play("RESET");
		storyMarkerAnimator.Advance(0.0);

		UpdateDescription();
		for (int i = 0; i < levelOptions.Count; i++)
		{
			levelOptions[i].ShowOption();

			if (SaveManager.ActiveGameData.CurrentStoryLevel != null &&
				levelOptions[i].data == SaveManager.ActiveGameData.CurrentStoryLevel)
			{
				storyLevelIndex = i;
				ProcessStoryMarkers();
			}
		}

		UpdateBgm();
	}

	private int storyMarkerVisibilitySign;
	private int storyLevelIndex = -1;
	private void ProcessStoryMarkers()
	{
		if (storyLevelIndex == -1)
			return;

		int centerPosition = scrollAmount + (PageSize / 2);
		int delta = centerPosition - storyLevelIndex;
		if (Mathf.Abs(delta) > PageSize / 2)
		{
			int targetSign = Mathf.Sign(delta);
			if (storyMarkerVisibilitySign != targetSign)
			{
				storyMarkerVisibilitySign = targetSign;
				storyMarkerAnimator.Play("RESET");
				storyMarkerAnimator.Advance(0.0);
				storyMarkerAnimator.Play(storyMarkerVisibilitySign > 0 ? "show-top" : "show-bottom");
			}
		}
		else if (storyMarkerVisibilitySign != 0)
		{
			HideStoryMarker();
		}
	}

	private void HideStoryMarker()
	{
		int centerPosition = scrollAmount + (PageSize / 2);
		int delta = centerPosition - storyLevelIndex;

		storyMarkerVisibilitySign = 0;
		storyMarkerAnimator.Play("RESET");
		storyMarkerAnimator.Advance(0.0);
		storyMarkerAnimator.Play(delta > 0 ? "hide-top" : "hide-bottom");
	}

	public void UpdateBgm()
	{
		bool canPlayBgm = !SaveManager.Config.useRetailMenuMusic && IsWorldUnlocked() && bgm.GetBgmResource() != null && !isModWorld;
		if (canPlayBgm && bgm?.Playing == false)
		{
			// Change to world specific level select music
			parentMenu.FadeBgm(1.5f);
			FadeBgm(1.5f, true, .2f); // Fade in bgm
			CurrentBgmTime = parentMenu.CurrentBgmTime; // Sync bgm
			readyMenu.SetBgmPlayer(bgm); // Update readymenu's bgm player
		}
		else if (!canPlayBgm)
		{
			// As a fallback, play the parent menu's bgm (won't do anything if parent bgm is already playing)
			parentMenu.PlayBgm();
			readyMenu.SetBgmPlayer(parentMenu.bgm);
		}
	}

	public override void HideMenu()
	{
		for (int i = 0; i < levelOptions.Count; i++)
			levelOptions[i].HideOption();
	}

	protected override void Confirm()
	{

		if (isAlertMenuActive)
		{
			GD.Print("Selecting option");
			if (isYesSelected)
			{
				isAlertMenuActive = false;
				alertAnimator.Advance(0.0);
				alertAnimator.Play("confirm");
			}
			else
			{
				isAlertMenuActive = false;
				alertAnimator.Advance(0.0);
				alertAnimator.Play("hide");
			}
			return;
		}
		if (ModManager.Instance.LevelMods.Count == 0 && isModWorld)
			return;

		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType != TimeAttackManager.RunType.SingleRun)
			return;

		if (isNothingSelected)
			return;

		if (!levelOptions[VerticalSelection].IsUnlocked)
			return;

		base.Confirm();
	}

	protected override void Cancel()
	{
		if (isAlertMenuActive)
		{
			isAlertMenuActive = false;
			CancelAlertMenu();
			return;
		}

		base.Cancel();

		// Revert bgm music
		if (bgm?.Playing == true)
		{
			FadeBgm(.5f); // Fade out bgm
			parentMenu.FadeBgm(.5f, true, .5f); // Fade in parent bgm
			parentMenu.CurrentBgmTime = CurrentBgmTime; // Sync bgm
		}
	}

	/// <summary> Shows the "Are you ready?" screen. </summary>
	public override void OpenSubmenu()
	{
		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
			TimeAttackManager.Instance.Level_Single = levelOptions[VerticalSelection].data;

		readyMenu.SetMapText(areaKey);
		readyMenu.SetMissionText(levelOptions[VerticalSelection].data.MissionTypeKey);
		readyMenu.parentMenu = this;
		readyMenu.LevelData = levelOptions[VerticalSelection].data;
		readyMenu.ShowMenu();
	}

	private void OpenBGMMenu()
	{
		if (storyMarkerVisibilitySign != 0)
			HideStoryMarker();

		jukebox.SelectedLevel = levelOptions[VerticalSelection].data;
		jukebox.ShowMenu();
	}

	protected override void UpdateSelection()
	{

		if (isAlertMenuActive)
		{
			int inputReturn = Mathf.Sign(Input.GetAxis("ui_left", "ui_right"));
			if ((inputReturn > 0 && isYesSelected) || (inputReturn < 0 && !isYesSelected))
			{
				isYesSelected = !isYesSelected;
				alertAnimator.Play(isYesSelected ? "select-yes" : "select-no");
			}

			return;
		}
		if (menuMemory[MemoryKeys.ActiveMenu] == (int)MemoryKeys.Jukebox)
			return;

		if (Mathf.IsZeroApprox(Input.GetAxis("ui_up", "ui_down")))
			return;

		if (isNothingSelected)
		{
			isNothingSelected = false;
			cursorAnimator.Play("loop");
			return;
		}

		if (levelOptions.Count == 1)
			return;

		VerticalSelection = WrapSelection(VerticalSelection + Mathf.Sign(Input.GetAxis("ui_up", "ui_down")), levelOptions.Count);
		ChangeSelection();
	}

	private void ChangeSelection()
	{
		menuMemory[MemoryKeys.LevelSelect] = VerticalSelection;
		animator.Play("select");
		animator.Seek(0, true);

		UpdateDescription();
		StartSelectionTimer();
		RecalculateListPosition();
		ProcessStoryMarkers();
	}

	private void UpdateDescription()
	{
		description.ShowDescription();
		description.Text = levelOptions[VerticalSelection].GetDescription();
	}

	private void RecalculateListPosition()
	{
		cursorPosition = VerticalSelection;
		if (levelOptions.Count > 5)
		{
			if (VerticalSelection < 3)
			{
				scrollRatio = 0;
				scrollAmount = 0;
			}
			else if (VerticalSelection >= levelOptions.Count - 3)
			{
				scrollRatio = 1;
				scrollAmount = levelOptions.Count - 5;
				cursorPosition = 4 - (levelOptions.Count - 1 - VerticalSelection);
			}
			else
			{
				scrollAmount = VerticalSelection - 2;
				scrollRatio = (VerticalSelection - 2) / (levelOptions.Count - 5.0f);
				cursorPosition = 2;
			}
		}
	}

	private void UpdateListPosition(float smoothing)
	{
		float targetScrollPosition = 360 * (VerticalSelection / (levelOptions.Count - 1f));
		scrollbar.Position = scrollbar.Position.SmoothDamp(Vector2.Right * targetScrollPosition, ref scrollVelocity, smoothing);

		cursor.Position = cursor.Position.SmoothDamp(new(cursor.Position.X, initialCursorPosition + (96 * cursorPosition)), ref cursorWidthVelocity, smoothing);
		options.Position = options.Position.SmoothDamp(Vector2.Up * ((96 * scrollAmount) - 32), ref optionVelocity, smoothing);
	}

	private void ReceiveMouseInput(LevelOption node)
	{
		if (!isProcessing)
			return;

		if (node == null)
		{
			isNothingSelected = true;
			cursorAnimator.Play("hide");
			return;
		}

		Runtime.Instance.IsUsingMouse = true;
		cursorAnimator.Play("loop");
		isNothingSelected = false;
		VerticalSelection = levelOptions.IndexOf(node);
		ChangeSelection();
	}

	private void ShowAlertMenu()
	{
		isAlertMenuActive = true;
		isYesSelected = false;

		alertAnimator.Advance(0.0);
		alertAnimator.Play("show");
	}

	private void CancelAlertMenu()
	{
		if (isYesSelected)
		{
			alertAnimator.Play("select-no");
			alertAnimator.Advance(0.0);
		}

		alertAnimator.Play("hide");
	}

	private void AlertMenuClosed()
	{
		isAlertMenuActive = false;
		EnableProcessing();
	}
	public List<LevelOption> GetLevelOptions()
	{
		return levelOptions;
	}
}
