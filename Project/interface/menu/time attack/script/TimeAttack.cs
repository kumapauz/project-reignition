using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Interface.Menus;

public partial class TimeAttack : Menu
{
	[Export] AnimationPlayer timeAttackAnimator;
	[Export] private Description description;
	[Export] private TimeAttackReady readyMenu;
	[Export] private TextureRect buttonImage;
	[Export] private AnimationPlayer buttonImageAnimator;
	[Export] Array<TimeAttackButton> buttonList;
	[Export] AnimationPlayer navigationButtonPlayer;
	private int currentSelection = 1;
	private int maxSelection = 2;
	private bool isRunInProgress = false;

	public override void ShowMenu()
	{
		TimeAttackManager.Instance.SetRunActive(true);
		SaveManager.ActiveSaveSlotIndex = SaveManager.SaveSlotCount; //Saves skills and presets on a hidden file
		SaveManager.ActiveSkillRing.LoadFromActiveData();
		SaveManager.ActiveGameData.level = 99;
		SaveManager.ActiveGameData.UnlockAllWorlds();
		SaveManager.ActiveSkillRing.UpdateTotalSkillPoints();
		SaveManager.ActiveGameData.LevelData.SetClearStatus("np_last", SaveManager.LevelSaveData.LevelStatus.Cleared); //Makes it so no story indicators will show up in time attack single run

		SaveManager.LoadTimeAttackData();//Creates a new timeattack file if there isn't one
		SaveManager.SaveTimeAttackData();
		SaveManager.SaveGameData();

		if (SaveManager.TimeData.RunInProgress != null)
		{
			isRunInProgress = SaveManager.TimeData.RunInProgress.Count > 0;
			maxSelection = isRunInProgress ? 3 : 2;
		}

		if (!TimeAttackManager.Instance.LoadIntoSingle)
		{
			if (!isRunInProgress)
				animator.Play("show");
			else if (isRunInProgress)
				animator.Play("showcontinue");
		}
		else
		{
			TimeAttackManager.Instance.ShouldLoadIntoSingle(false);
			animator.Play("show-single");
		}

		menuMemory[MemoryKeys.ActiveMenu] = (int)MemoryKeys.TimeAttack;

		if (!bgm.Playing)
			bgm.Play();
	}

	public override void EnableProcessing()
	{
		base.EnableProcessing();

		if (isAlertMenuActive)
			return;

		RedrawSelection();
	}

	public override void OpenParentMenu()
	{
		// Return to main menu
		FadeBgm(.5f);

		menuMemory[MemoryKeys.ActiveMenu] = (int)MemoryKeys.MainMenu; // Set up menu memory so the main menu loads after the scene transition
		TransitionManager.QueueSceneChange(TransitionManager.MenuScenePath);
		TransitionManager.StartTransition(new()
		{
			color = Colors.Black,
			inSpeed = .5f,
		});
	}

	protected override void UpdateSelection()
	{
		if (isAlertMenuActive)
		{
			isNothingSelected = false;
			int inputReturn = Mathf.Sign(Input.GetAxis("ui_left", "ui_right"));
			if ((inputReturn > 0 && isContinueSelected) || (inputReturn < 0 && !isContinueSelected))
			{
				isContinueSelected = !isContinueSelected;
				returnAnimator.Play(isContinueSelected ? "select-yes" : "select-no");
			}

			return;
		}

		Vector2I input = new(Mathf.Sign(Input.GetAxis("ui_left", "ui_right")), Mathf.Sign(Input.GetAxis("ui_up", "ui_down")));
		StartSelectionTimer();
		ProcessMenuInput(input);
	}

	private void ProcessMenuInput(Vector2I input)
	{
		if (input.Y == 0)
			return;

		Runtime.Instance.IsUsingMouse = false;
		currentSelection += input.Y;
		if (currentSelection > maxSelection || currentSelection < 1)
			currentSelection = WrapSelection(currentSelection, maxSelection, 1);

		RedrawSelection();
	}

	private void RedrawSelection()
	{
		for (int i = 0; i < buttonList.Count; i++)
			buttonList[i].DeselectButton();

		buttonImageAnimator.Play("show");
		
		if (isRunInProgress)
		{
			buttonList[currentSelection - 1].SelectButton();
			buttonImage.Texture = buttonList[currentSelection - 1].image;
			description.Text = buttonList[currentSelection - 1].description;
		}
		else
		{
			switch (currentSelection)
			{
				case 1:
					buttonList[0].SelectButton();
					buttonImage.Texture = buttonList[0].image;
					description.Text = buttonList[0].description;
					break;
				case 2:
					buttonList[2].SelectButton();
					description.Text = buttonList[2].description;
					buttonImage.Texture = buttonList[2].image;
					description.Text = buttonList[2].description;
					break;
			}
		}

		description.ShowDescription();
	}

	protected override void Confirm()
	{
		if (isAlertMenuActive)
		{
			if (isNothingSelected)
				return;

			if (isContinueSelected) //Yes
			{
				isAlertMenuActive = false;
				returnAnimator.Play("confirm");
				timeAttackAnimator.Play("confirm-1yes");
			}
			else //No
			{
				isAlertMenuActive = false;
				returnAnimator.Play("hide");
			}

			return;
		}

		TimeAttackManager.Instance.SetRunActive(true);

		if (isRunInProgress)
		{
			switch (currentSelection)
			{
				case 1://New Run
					timeAttackAnimator.Play("confirm-1continue");
					ShowReturnMenu();
					break;
				case 2://Continue Run
					timeAttackAnimator.Play("confirm-2continue");
					ContinueRun();
					break;
				case 3://Single Run
					timeAttackAnimator.Play("confirm-3");
					StartSingleRun();
					break;
			}
			return;
		}

		switch (currentSelection)
		{
			case 2://Single Run
				StartSingleRun();
				break;
		}
		timeAttackAnimator.Play("confirm-" + currentSelection);
	}

	private void StartSingleRun()
	{
		navigationButtonPlayer.Play("hide");
		FadeBgm(0.5f);
		TimeAttackManager.Instance.SetRunType(TimeAttackManager.RunType.SingleRun);
		SaveManager.ActiveGameData.equippedSkills = SaveManager.TimeData.equippedSkillsSingle;
		SaveManager.ActiveGameData.equippedAugments = SaveManager.TimeData.equippedAugmentsSingle;
	}

	protected override void Cancel()
	{
		if (isAlertMenuActive)
		{
			CancelReturnMenu();
			return;
		}

		SaveManager.SaveTimeAttackData();
		SaveManager.SaveGameData();
		TimeAttackManager.Instance.SetRunActive(false);

		OpenParentMenu();
	}

	private void ContinueRun()
	{
		SaveManager.ActiveGameData.equippedSkills = SaveManager.TimeData.equippedSkillsContinue;
		SaveManager.ActiveGameData.equippedAugments = SaveManager.TimeData.equippedAugmentsContinue;
		SaveManager.TimeData.Tries += 1;
		SaveManager.SaveTimeAttackData();
		TimeAttackManager.Instance.SetRunActive(true);
		TimeAttackManager.Instance.SetReturnTimes();
		TimeAttackManager.Instance.LoadLevel(TimeAttackManager.Instance.GetCurrentLevel());
	}

	[Export]
	private AnimationPlayer returnAnimator;
	private bool isAlertMenuActive = false;
	private bool isContinueSelected;
	private bool isNothingSelected;

	private void ShowReturnMenu()
	{
		isAlertMenuActive = true;
		isContinueSelected = true;

		isNothingSelected = Runtime.Instance.IsUsingMouse;
		returnAnimator.Play(isNothingSelected ? "select-none" : "select-yes");
		returnAnimator.Advance(0.0);

		returnAnimator.Play("show");
	}

	private void CancelReturnMenu()
	{
		if (isContinueSelected)
		{
			returnAnimator.Play("select-no");
			returnAnimator.Advance(0.0);
		}

		returnAnimator.Play("hide");
		
	}

	private void AlertMenuClosed()
	{
		isAlertMenuActive = false;

		if (!isContinueSelected)
			EnableProcessing();
	}

	public override void PlayReturnAnim() => timeAttackAnimator.Play("show");

	private void ReceiveMouseInput(int selection)
	{
		if (isAlertMenuActive)
		{
			isNothingSelected = selection == -1;
			if (isNothingSelected)
			{
				returnAnimator.Play("select-none");
			}
			else
			{
				isContinueSelected = selection == 0;
				returnAnimator.Play(isContinueSelected ? "select-yes" : "select-no");
			}
			return;
		}

		if (selection == 3 && !isRunInProgress)
			selection--;

		if (currentSelection == selection)
			return;

		Runtime.Instance.IsUsingMouse = true;
		currentSelection = selection;

		if (isProcessing)
			RedrawSelection();
	}
}
