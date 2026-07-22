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
	private bool isActive;
	private int currentSelection;
	private int maxSelection = 2;
	private bool isRunInProgress = false;

	protected override void SetUp()
	{
		currentSelection = 1;
	}

	public override void ShowMenu()
	{
		TimeAttackManager.Instance.SetRunActive(true);
		SaveManager.ActiveSaveSlotIndex = SaveManager.SaveSlotCount; //Saves skills and presets on a hidden file
		SaveManager.ActiveSkillRing.LoadFromActiveData();
		SaveManager.ActiveGameData.level = 99;
		SaveManager.ActiveGameData.UnlockAllWorlds();
		SaveManager.ActiveSkillRing.UpdateTotalSkillPoints();

		SaveManager.LoadTimeAttackData();//Creates a new timeattack file if there isn't one
		SaveManager.SaveTimeAttackData();
		SaveManager.SaveGameData();

		if (SaveManager.TimeData.RunInProgress != null)
		{
			if (SaveManager.TimeData.RunInProgress.Count > 0)
			{
				maxSelection = 3;
				isRunInProgress = true;
			}
			else
			{
				maxSelection = 2;
				isRunInProgress = false;
			}
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


		currentSelection = 1;
		description.Text = buttonList[0].description;

		menuMemory[MemoryKeys.ActiveMenu] = (int)MemoryKeys.TimeAttack;

		if (!bgm.Playing)
			bgm.Play();
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
		if (isReturnMenuActive)
		{
			int inputReturn = Mathf.Sign(Input.GetAxis("ui_left", "ui_right"));
			if ((inputReturn > 0 && isReturnSelected) || (inputReturn < 0 && !isReturnSelected))
			{
				isReturnSelected = !isReturnSelected;
				returnAnimator.Play(isReturnSelected ? "select-yes" : "select-no");
			}

			return;
		}

		Vector2I input = new(Mathf.Sign(Input.GetAxis("ui_left", "ui_right")), Mathf.Sign(Input.GetAxis("ui_up", "ui_down")));
		StartSelectionTimer();
		ProcessMenuInput(input);
	}

	private void ProcessMenuInput(Vector2I input)
	{
		if (!isActive)
			return;

		if (input.X == 0 && input.Y == 0)
			return;

		if (input.X != 0 && input.Y == 0)
			return;

		currentSelection += input.Y;
		if (currentSelection > maxSelection || currentSelection < 1)
			currentSelection = WrapSelection(currentSelection, maxSelection, 1);

		if (input.X == 0)
		{
			description.Text = buttonList[currentSelection - 1].description;
			description.ShowDescription();
		}

		for (int i = 0; i < buttonList.Count; i++)
			buttonList[i].DeselectButton();


		buttonImageAnimator.Play("show");
		if (isRunInProgress)
		{
			buttonList[currentSelection - 1].SelectButton();
			return;
		}


		switch (currentSelection)
		{
			case 1:
				buttonList[0].SelectButton();
				break;
			case 2:
				buttonList[2].SelectButton();
				description.Text = buttonList[2].description;
				break;
		}
	}

	protected override void Confirm()
	{
		if (!isActive)
			return;

		if (isReturnMenuActive)
		{
			if (isReturnSelected) //Yes
			{
				isReturnMenuActive = false;
				returnAnimator.Play("confirm");
				timeAttackAnimator.Play("confirm-1yes");
			}
			else //No
			{
				isReturnMenuActive = false;
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
					base.FadeBgm(0.3f);
					TimeAttackManager.Instance.SetRunType(TimeAttackManager.RunType.SingleRun);
					timeAttackAnimator.Play("confirm-3");
					currentSelection = 1;

					SaveManager.ActiveGameData.equippedSkills = SaveManager.TimeData.equippedSkillsSingle;
					SaveManager.ActiveGameData.equippedAugments = SaveManager.TimeData.equippedAugmentsSingle;
					break;
			}
			return;
		}

		switch (currentSelection)
		{
			case 2://Single Run
				base.FadeBgm(0.5f);
				TimeAttackManager.Instance.SetRunType(TimeAttackManager.RunType.SingleRun);
				SaveManager.ActiveGameData.equippedSkills = SaveManager.TimeData.equippedSkillsSingle;
				SaveManager.ActiveGameData.equippedAugments = SaveManager.TimeData.equippedAugmentsSingle;
				break;
		}
		timeAttackAnimator.Play("confirm-" + currentSelection);
		currentSelection = 1;
	}

	protected override void Cancel()
	{
		if (!isActive)
			return;

		if (isReturnMenuActive)
		{
			CancelReturnMenu();
			return;
		}

		SaveManager.SaveTimeAttackData();
		SaveManager.SaveGameData();
		TimeAttackManager.Instance.SetRunActive(false);

		currentSelection = 1;
		OpenParentMenu();
	}

	private void ContinueRun()
	{
		SaveManager.ActiveGameData.equippedSkills = SaveManager.TimeData.equippedSkillsContinue;
		SaveManager.ActiveGameData.equippedAugments = SaveManager.TimeData.equippedAugmentsContinue;
		TimeAttackManager.Instance.SetRunActive(true);
		TimeAttackManager.Instance.SetReturnTimes();
		TimeAttackManager.Instance.LoadLevel(TimeAttackManager.Instance.GetCurrentLevel());
	}

	[Export]
	private AnimationPlayer returnAnimator;
	private bool isReturnMenuActive = false;
	private bool isReturnSelected;

	private void ShowReturnMenu()
	{
		isReturnMenuActive = true;
		isReturnSelected = true;

		returnAnimator.Advance(0.0);

		returnAnimator.Play("select-yes");
		returnAnimator.Advance(0.0);

		returnAnimator.Play("show");
	}
	private void CancelReturnMenu()
	{

		if (isReturnSelected)
		{
			returnAnimator.Play("select-no");
			returnAnimator.Advance(0.0);
		}

		isReturnMenuActive = false;
		returnAnimator.Play("hide");
	}

	private void AlertMenuClosed()
	{
		isReturnMenuActive = false;
		EnableProcessing();
	}

	public override void PlayReturnAnim() => timeAttackAnimator.Play("show");
	public void SetActive() => isActive = true;
	public void SetInactive() => isActive = false;

	public void ChangeButtonImage() => buttonImage.Texture = buttonList[currentSelection - 1].image;

}
