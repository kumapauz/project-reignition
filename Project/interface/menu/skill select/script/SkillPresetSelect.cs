using Godot;
using Godot.Collections;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface.Menus;

public partial class SkillPresetSelect : Menu
{
	[Export] private PackedScene presetOption;
	[Export] private VBoxContainer presetContainer;

	[Export] private Node2D cursor;
	[Export] private Sprite2D scrollbar;

	[Export] private Label saveLabel;

	[Export] private LineEdit nameEditor;

	[Export] private AnimationPlayer submenuAnimator;
	[Export] private AnimationPlayer nameEditorAnimator;
	[Export] private AnimationPlayer alertAnimator;

	[Export] private AudioStreamPlayer confirmSFX;
	[Export] private AudioStreamPlayer cancelSFX;
	[Export] private AudioStreamPlayer selectSFX;
	[Export] private AudioStreamPlayer failSFX;

	private SkillRing ActiveSkillRing => SaveManager.ActiveSkillRing;

	private int scrollAmount;
	private float scrollRatio;
	private Vector2 scrollVelocity;
	private Vector2 containerVelocity;
	private const float ScrollSmoothing = .1f;
	private readonly int ScrollInterval = 276;
	private readonly int PageSize = 3;

	private int cursorPosition;
	private Vector2 cursorVelocity;
	private const float CursorSmoothing = .1f;
	private bool isNothingSelected;

	private Array<SkillPresetOption> presetList = [];

	public bool isSubMenuActive;

	public bool isEditingName;
	private int subIndex;

	private bool isAlertMenuActive;
	private int alertSelection;

	protected override void SetUp()
	{
		//  Create Preset Option nodes
		for (int i = 0; i < SaveManager.PresetCount; i++)
		{
			SkillPresetOption newPreset = presetOption.Instantiate<SkillPresetOption>();
			newPreset.Index = i;
			newPreset.MouseEntered += () => ReceiveMouseInput(newPreset);
			newPreset.MouseExited += () => ReceiveMouseInput(null);

			presetList.Add(newPreset);
			presetContainer.AddChild(newPreset);
		}
	}

	public override void _Process(double _)
	{
		float targetScrollPosition = 360 * scrollRatio;
		scrollbar.Position = scrollbar.Position.SmoothDamp(Vector2.Right * targetScrollPosition, ref scrollVelocity, ScrollSmoothing);

		//  Update cursor position
		float targetCursorPosition = cursorPosition * ScrollInterval;
		cursor.Position = cursor.Position.SmoothDamp(Vector2.Down * targetCursorPosition, ref cursorVelocity, CursorSmoothing);

		Vector2 targetContainerPosition = new(presetContainer.Position.X, -scrollAmount * ScrollInterval);
		presetContainer.Position = presetContainer.Position.SmoothDamp(targetContainerPosition, ref containerVelocity, ScrollSmoothing);
	}

	protected override void ProcessMenu()
	{
		if (isEditingName)
		{
			if (Runtime.Instance.IsActionJustPressed("sys_pause", "ui_accept") && !Input.IsActionJustPressed("toggle_fullscreen"))
			{
				Rename();
				return;
			}

			if (Input.IsKeyPressed(Key.Escape))
			{
				cancelSFX.Play();
				StopRenaming();
				return;
			}
		}

		if (Runtime.Instance.MouseScrollInput != 0 && !isSubMenuActive)
		{
			int previousSelection = VerticalSelection;
			VerticalSelection = Mathf.Clamp(VerticalSelection + Runtime.Instance.MouseScrollInput, 0, presetList.Count - 1);

			if (previousSelection != VerticalSelection)
			{
				presetList[previousSelection].DeselectInstant();
				selectSFX.Play();
				MoveCursor(Runtime.Instance.MouseScrollInput, VerticalSelection);
				UpdateScrollAmount(Runtime.Instance.MouseScrollInput, true);
			}
		}

		if (Runtime.Instance.IsActionJustPressed("sys_pause", "ui_accept") && !Input.IsActionJustPressed("toggle_fullscreen"))
			Confirm();

		base.ProcessMenu();
	}

	private void UpdateScrollAmount(int inputSign, bool forceScroll = false)
	{
		int listSize = presetList.Count;

		if (listSize <= PageSize)
		{
			//  Disable scrolling
			scrollAmount = 0;
			scrollRatio = 0;
			return;
		}

		if (forceScroll)
			scrollAmount += inputSign;
		else if (VerticalSelection == 0 || VerticalSelection == listSize - 1)
			cursorPosition = scrollAmount = VerticalSelection;
		else if ((inputSign < 0 && cursorPosition == 0) || (inputSign > 0 && cursorPosition == PageSize - 1))
			scrollAmount += inputSign;
		else
			cursorPosition += inputSign;

		scrollAmount = Mathf.Clamp(scrollAmount, 0, listSize - PageSize);
		cursorPosition = Mathf.Clamp(cursorPosition, 0, PageSize - 1);
		scrollRatio = (float)VerticalSelection / (presetList.Count - 1);
	}

	public override void ShowMenu()
	{
		animator.Play("show");

		for (int i = 0; i < SaveManager.PresetCount; i++)
			if (presetList[i].IsInvalid)
				SaveEmptyPreset(i);
		LoadPresets();

		base.ShowMenu();
	}

	public void LoadPresets()
	{
		for (int i = 0; i < presetList.Count; i++)
			presetList[i].Initialize();

		presetList[VerticalSelection].SelectRight();
	}

	private void SaveEmptyPreset(int preset)
	{
		presetList[preset].PresetName = "New Preset";

		presetList[preset].Skills.Clear();
		presetList[preset].Augments.Clear();
		presetList[preset].Initialize();
	}

	protected override void UpdateSelection()
	{
		if (isAlertMenuActive)
		{
			int input = Mathf.Sign(Input.GetAxis("ui_left", "ui_right"));
			if ((input > 0 && alertSelection != 1) || (input < 0 && alertSelection != 0))
			{
				alertSelection = input > 0 ? 1 : 0;
				UpdateAlertMenuVisuals();
			}

			return;
		}

		int inputSign = Mathf.Sign(Input.GetAxis("ui_up", "ui_down"));
		if (inputSign == 0)
			return;

		if (isEditingName)
			return;

		if (isSubMenuActive)
		{
			subIndex = WrapSelection(subIndex + inputSign, 5);
			MoveSubCursor();
			return;
		}

		presetList[VerticalSelection].DeselectInstant();
		VerticalSelection = WrapSelection(VerticalSelection + inputSign, presetList.Count);
		selectSFX.Play();
		MoveCursor(inputSign, VerticalSelection);
		UpdateScrollAmount(inputSign);
	}

	protected override void Confirm()
	{
		if (isEditingName)
			return;

		if (isSubMenuActive)
		{
			if (subIndex == -1)
				return;

			switch (subIndex)
			{
				case 1:
					SaveSkills(VerticalSelection, false);
					submenuAnimator.Play("select-save");
					break;
				case 0:
					LoadSkills(VerticalSelection);
					break;
				case 2:
					if (!IsInvalid(VerticalSelection))
						StartRenaming();
					else
						failSFX.Play();
					break;
				case 3:
					if (!IsInvalid(VerticalSelection))
					{
						// Show alert menu
						if (Runtime.Instance.IsUsingMouse)
						{
							alertSelection = -1;
							alertAnimator.Play("select-none");
						}
						else
						{
							alertSelection = 1;
							alertAnimator.Play("select-no");
						}
						alertAnimator.Advance(0.0);

						isAlertMenuActive = true;
						isSubMenuActive = false;

						alertAnimator.Play("show");
						alertAnimator.Advance(0.0);
					}
					else
						failSFX.Play();
					break;
				case 4:
					submenuAnimator.Play("hide");
					isSubMenuActive = false;
					break;
			}

			return;
		}

		if (isAlertMenuActive)
		{
			if (alertSelection == -1)
				return;

			if (alertSelection == 0)
			{
				DeletePreset(VerticalSelection);
				isSubMenuActive = true;
				isAlertMenuActive = false;
				alertAnimator.Play("confirm");
			}
			else
			{
				isSubMenuActive = true;
				isAlertMenuActive = false;
				alertAnimator.Play("hide");
			}

			return;
		}

		//  Show the submenu
		if (Runtime.Instance.IsUsingMouse)
		{
			subIndex = -1;
			submenuAnimator.Play(IsInvalid(VerticalSelection) ? "select-none-invalid" : "select-none");
		}
		else
		{
			subIndex = 0;
			submenuAnimator.Play(IsInvalid(VerticalSelection) ? "select-load-invalid" : "select-load");
			//submenuAnimator.Play("select-load");
		}
		submenuAnimator.Advance(0.0);


		submenuAnimator.Play("show");
		isSubMenuActive = true;
	}

	private void UpdateAlertMenuVisuals()
	{
		if (alertSelection == -1)
		{
			alertAnimator.Play("select-none");
			return;
		}

		alertAnimator.Play(alertSelection == 0 ? "select-yes" : "select-no");
	}

	protected override void Cancel()
	{
		if (isAlertMenuActive)
		{
			alertSelection = 1;
			alertAnimator.Play("select-no");
			alertAnimator.Advance(0.0);
			isSubMenuActive = true;
			isAlertMenuActive = false;
			alertAnimator.Play("hide");
			return;
		}

		if (isEditingName)
			return;

		if (isSubMenuActive)
		{
			submenuAnimator.Play("hide");
			isSubMenuActive = false;
		}
		else
		{
			// Return to skill editing
			animator.Play("hide");
			OpenParentMenu();
		}
	}

	public void MoveSubCursor()
	{
		string targetAnimation = string.Empty;
		switch (subIndex)
		{
			case -1:
				targetAnimation = "select-none";
				break;
			case 0:
				targetAnimation = "select-load";
				break;
			case 1:
				targetAnimation = "select-save";
				break;
			case 2:
				targetAnimation = "select-rename";
				break;
			case 3:
				targetAnimation = "select-delete";
				break;
			case 4:
				targetAnimation = "select-cancel";
				break;
		}

		if (IsInvalid(VerticalSelection))
			targetAnimation += "-invalid";


		submenuAnimator.Play(targetAnimation);

		selectSFX.Play();
		StartSelectionTimer();
	}

	private void MoveCursor(int dir, int index)
	{
		if (dir == 0)
			presetList[index].SelectRight();
		else if (dir < 0)
			presetList[index].SelectUp();
		else
			presetList[index].SelectDown();

		StartSelectionTimer();
	}

	private void SaveSkills(int preset, bool renameOnly)
	{
		//  Storing our equipped skills into our current preset
		if (string.IsNullOrEmpty(presetList[preset].PresetName))
			presetList[preset].PresetName = "New Preset";

		presetList[preset].PresetName = presetList[preset].PresetName.TrimEnd('\n'); // Remove the newline code
		SaveManager.ActiveGameData.presetNames[preset] = presetList[preset].PresetName;

		if (!renameOnly)
		{
			presetList[preset].Skills = SaveManager.ActiveGameData.equippedSkills.Duplicate();
			presetList[preset].Augments = SaveManager.ActiveGameData.equippedAugments.Duplicate();
		}

		// Never save modded character selections
		presetList[preset].Skills.Remove(SkillKey.Character);
		presetList[preset].Augments.Remove(SkillKey.Character);

		//  Save our new data to the file and play the animation to initialize the on-screen data
		if (subIndex == 1) // Only play the save animation if we are selecting save
			presetList[preset].SavePreset();
		else
			presetList[preset].Initialize();

		SaveManager.SaveGameData();
	}

	private void LoadSkills(int preset)
	{
		bool isCharacterEquipped = ActiveSkillRing.IsSkillEquipped(SkillKey.Character);
		int characterIndex = ActiveSkillRing.GetAugmentIndex(SkillKey.Character);

		SaveManager.ActiveGameData.equippedSkills = presetList[preset].Skills.Duplicate();
		SaveManager.ActiveGameData.equippedAugments = presetList[preset].Augments.Duplicate();

		if (isCharacterEquipped) // Preserve modded character selection
		{
			SaveManager.ActiveGameData.equippedSkills.Add(SkillKey.Character);
			SaveManager.ActiveGameData.equippedAugments.Add(SkillKey.Character, characterIndex);
		}

		ActiveSkillRing.LoadFromActiveData();
		presetList[preset].SelectPreset();
	}

	private void StartRenaming()
	{
		isEditingName = true;
		confirmSFX.Play();

		nameEditor.Text = SaveManager.ActiveGameData.presetNames[VerticalSelection];
		nameEditor.CaretColumn = nameEditor.Text.Length;
		nameEditor.SelectAll();
		if (!nameEditor.HasFocus())
			nameEditor.CallDeferred(Control.MethodName.GrabFocus);
		nameEditorAnimator.Play("show");
	}

	private void Rename()
	{
		presetList[VerticalSelection].PresetName = nameEditor.Text;
		confirmSFX.Play();
		SaveSkills(VerticalSelection, true);
		StopRenaming();
	}

	private void StopRenaming()
	{
		nameEditor.CallDeferred(Control.MethodName.ReleaseFocus);
		nameEditorAnimator.Play("hide");
		isEditingName = false;
	}

	private void DeletePreset(int preset)
	{
		//  A null/empty preset means it's already been deleted.
		if (string.IsNullOrEmpty(SaveManager.ActiveGameData.presetNames[preset]))
			return;

		confirmSFX.Play();

		presetList[preset].PresetName = "";
		presetList[preset].Skills = null;
		presetList[preset].Augments = null;

		SaveManager.SaveGameData();
		presetList[preset].Initialize();

		submenuAnimator.CurrentAnimation = "select-delete-invalid";
		submenuAnimator.Seek(0.0, true, true); // Grays out the options menu
		SaveEmptyPreset(preset);
	}

	public void AlertMenuClosed()
	{
		isAlertMenuActive = false;
		EnableProcessing();
	}

	private bool IsInvalid(int index) => presetList[index].IsInvalid;

	private void ReceiveSubmenuMouseInput(int index)
	{
		if (!isProcessing || !isSubMenuActive)
			return;

		subIndex = index;
		MoveSubCursor();
	}

	private void ReceiveAlertMouseInput(int index)
	{
		if (!isProcessing || !isAlertMenuActive)
			return;

		alertSelection = index;
		UpdateAlertMenuVisuals();
	}

	private void ReceiveMouseInput(Node node)
	{
		if (!isProcessing)
			return;

		if (isSubMenuActive || isAlertMenuActive)
			return;

		Runtime.Instance.IsUsingMouse = true;
		if (node == null)
		{
			isNothingSelected = true;
			presetList[VerticalSelection].Deselect();
			return;
		}

		isNothingSelected = false;
		int sign = node.GetIndex() - VerticalSelection;
		if (sign != 0)
			presetList[VerticalSelection].DeselectInstant();

		VerticalSelection = node.GetIndex();
		selectSFX.Play();
		MoveCursor(sign, VerticalSelection);
	}
}
