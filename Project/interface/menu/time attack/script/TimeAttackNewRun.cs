using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Interface.Menus;

public partial class TimeAttackNewRun : Menu
{
	[Export] AnimationPlayer newRunAnimator;
	[Export] private Description description;
	[Export] TimeAttack thisParent;
	[Export] SaveSelect saveSelect;
	[Export] TimeAttackStartRun startRun;
	[Export] TimeAttackLevelList levelList;
	[Export] private TextureRect buttonImage;
	[Export] private AnimationPlayer buttonImageAnimator;
	[Export] Array<TimeAttackButton> buttonList;
	private bool isActive;
	private int currentSelection;
	private int maxSelection = 3;


	protected override void SetUp()
	{
		currentSelection = 1;
	}

	public override void ShowMenu()
	{
		base.ShowMenu();
		currentSelection = 1;
		description.Text = buttonList[0].description;
		SaveManager.LoadTimeAttackData();
	}

	public override void OpenParentMenu()
	{
		base.OpenParentMenu();
	}

	protected override void ProcessMenu()
	{
		base.ProcessMenu();
	}

	protected override void UpdateSelection()
	{
		Vector2I input = new(Mathf.Sign(Input.GetAxis("ui_left", "ui_right")), Mathf.Sign(Input.GetAxis("ui_up", "ui_down")));
		StartSelectionTimer();

		ProcessMenuInput(input);
	}

	private void ProcessMenuInput(Vector2I input)
	{
		if (isActive)
		{
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
			{
				buttonList[i].DeselectButton();
			}
			buttonImageAnimator.Play("show");
			buttonList[currentSelection - 1].SelectButton();
		}
		else
			return;
	}

	protected override void Confirm()
	{
		if (isActive)
		{
			TimeAttackManager.Instance.ResetLevelCount();

			switch (currentSelection)
			{
				case 1:
					TimeAttackManager.Instance.SetRunType(TimeAttackManager.RunType.AnyP);
					break;
				case 2:
					TimeAttackManager.Instance.SetRunType(TimeAttackManager.RunType.GoalPercent);
					break;
				case 3:
					TimeAttackManager.Instance.SetRunType(TimeAttackManager.RunType.BossRush);
					break;
			}
			TimeAttackManager.Instance.SetRunActive(true);
			levelList.parentMenu = this;

			newRunAnimator.Play("confirm-" + currentSelection);
			currentSelection = 1;
		}
	}

	protected override void Cancel()
	{
		if (isActive)
		{
			TimeAttackManager.Instance.SetRunActive(false);
			newRunAnimator.Play("hide");

		}
	}

	public void SetActive() => isActive = true;
	public void SetInactive() => isActive = false;

	public override void OpenSubmenu()
	{
		switch (currentSelection)
		{
			case 1:
				_submenus[0].ShowMenu();
				break;
			case 2:
				OpenSaveSelect();
				break;
			case 3:
				OpenSaveSelect();
				break;
		}
		currentSelection = 1;
	}
	public void OpenSaveSelect()
	{
		saveSelect.Visible = true;
		saveSelect.parentMenu = this;
		saveSelect.ShowMenu();
	}

	public void OpenStartRun()
	{
		startRun.ShowMenu();
	}

	public void ChangeButtonImage() => buttonImage.Texture = buttonList[currentSelection - 1].image;


}
