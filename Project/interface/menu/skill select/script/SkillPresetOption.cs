using Godot;
using Godot.Collections;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface.Menus;

public partial class SkillPresetOption : Control
{
	//public bool IsInvalid => string.IsNullOrEmpty(PresetName);
	public bool IsInvalid => CheckInvalid();

	/// <summary> The preset option's 0-based index number. </summary>
	public int Index { get; set; }
	/// <summary> The preset option's menu number. </summary>
	public int DisplayNumber => Index + 1;

	[Export] private Label presetLabel;

	[Export] private Label numLabel;

	[Export] private Label skillCountLabel;
	[Export] private Label skillCostLabel;

	[Export] private Label windAmtLabel;
	[Export] private Label fireAmtLabel;
	[Export] private Label darkAmtLabel;

	[Export] private AnimationPlayer animator;
	[Export] private AnimationPlayer animatorData;

	public string PresetName
	{
		get => SaveManager.ActiveGameData.presetNames[Index];
		set => SaveManager.ActiveGameData.presetNames[Index] = value;
	}

	public Array<SkillKey> Skills
	{
		get => SaveManager.ActiveGameData.presetSkills[Index];
		set => SaveManager.ActiveGameData.presetSkills[Index] = value;
	}

	public Dictionary<SkillKey, int> Augments
	{
		get => SaveManager.ActiveGameData.presetSkillAugments[Index];
		set => SaveManager.ActiveGameData.presetSkillAugments[Index] = value;
	}

	public bool CheckInvalid()
	{
		if (Skills.Count == 0 && Augments.Count == 0)
			return true;
		return false;
	}

	public void Initialize()
	{
		numLabel.Text = DisplayNumber.ToString("00");
		//if (IsInvalid)
		//{
		//animator.Play("no-preset");
		//return;
		//}

		Redraw();
		if (!IsInvalid)
			animatorData.Play("show");
		else
			animatorData.Play("show-invalid");
	}

	public void Redraw()
	{
		presetLabel.Text = PresetName;

		int skillCount = 0;
		int skillCost = 0;
		int windAmt = 0;
		int fireAmt = 0;
		int darkAmt = 0;

		// Update all the above totals
		for (int i = 0; i < Skills.Count; i++)
		{
			SkillResource baseSkill = Runtime.Instance.SkillList.GetSkill(Skills[i]);
			if (baseSkill == null)
				continue;

			int augmentIndex = SaveManager.ActiveSkillRing.GetAugmentIndex(Skills[i]);
			if (augmentIndex == 0)
			{
				switch (baseSkill.Element)
				{
					case SkillResource.SkillElement.Wind:
						windAmt++;
						break;

					case SkillResource.SkillElement.Fire:
						fireAmt++;
						break;

					case SkillResource.SkillElement.Dark:
						darkAmt++;
						break;
				}

				skillCost += baseSkill.Cost;
				skillCount++;
				continue;
			}

			switch (baseSkill.Augments[augmentIndex - 1].Element)
			{
				case SkillResource.SkillElement.Wind:
					windAmt++;
					break;

				case SkillResource.SkillElement.Fire:
					fireAmt++;
					break;

				case SkillResource.SkillElement.Dark:
					darkAmt++;
					break;
			}
			skillCost += baseSkill.Augments[augmentIndex - 1].Cost;
			skillCount++;
		}

		windAmtLabel.Text = "×" + windAmt.ToString("00");
		fireAmtLabel.Text = "×" + fireAmt.ToString("00");
		darkAmtLabel.Text = "×" + darkAmt.ToString("00");
		skillCountLabel.Text = skillCount.ToString("000");
		skillCostLabel.Text = skillCost.ToString("000");
	}

	public void SelectUp() => animator.Play("select-up");

	public void SelectDown() => animator.Play("select-down");

	public void SelectLeft() => animator.Play("select-left");

	public void SelectRight() => animator.Play("select-right");

	public void SavePreset() => animator.Play("save-preset");

	public void SelectPreset() => animator.Play("load-preset");

	public void Deselect() => animator.Play("deselect");

	public void DeselectInstant() => animator.Play("deselect_instant");
}
