using Godot;
using Project.Core;
using Project.Gameplay;

namespace Project.Interface.Menus;

public partial class LevelOption : Control
{
	[Signal]
	public delegate void NewLevelEventHandler();

	[Export]
	/// <summary> Reference to level's settings resource. </summary>
	public LevelDataResource data;

	[ExportGroup("Components")]
	[Export] private Label missionLabel;
	[Export] private Control fireSoulParent;
	[Export] private TextureRect[] fireSoulRects;
	[Export] private Texture2D fireSoulSprite;
	[Export] private Texture2D noFireSoulSprite;
	[Export] private Sprite2D light;
	[Export] private Control storyMarker;
	[Export] private Label newLabel;
	[Export] private TextureRect rank;
	[Export] private AnimationPlayer animator;

	[Export] private Control timeAttackLevelOption;
	[Export] private Label missionLabelTA;
	[Export] private Label areaLabelTA;
	[Export] private Label timeLabel;

	private readonly string NoMedalAnimation = "no-medal";
	private readonly string GoldAnimation = "gold";
	private readonly string SilverAnimation = "silver";
	private readonly string BronzeAnimation = "bronze";

	private readonly string ShowAnimation = "show";
	private readonly string ShowTAAnimation = "show-ta";
	private readonly string HideAnimation = "hide";
	private readonly string NewAnimation = "new";
	private readonly string ClearAnimation = "clear";
	private readonly string AttemptAnimation = "attempt";
	private readonly string LoopAnimation = "-loop";
	private readonly string StoryAnimation = "story";

	public bool IsUnlocked
	{
		get
		{
			if (string.IsNullOrEmpty(data.LevelPath)) return false; // Level doesn't exist.
			if (data.UnlockedByDefault) return true;
			if (DebugManager.Instance.UnlockAllStages) return true;
			if (TimeAttackManager.Instance.IsRunActive) return true;
			if (data.AreaKey == SaveManager.WorldEnum.Mods) return true;

			return SaveManager.ActiveGameData.IsStageUnlocked(data.LevelID);
		}
	}
	public SaveManager.LevelSaveData.LevelStatus ClearState { get; private set; }


	public string GetDescription() => IsUnlocked ? data.MissionDescriptionKey : "mission_description_locked";

	public void ShowOption()
	{
		ApplySettings();
		UpdateLevelData();
		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
			animator.Play(ShowTAAnimation);
		else
			animator.Play(ShowAnimation);

		if (data.AreaKey == SaveManager.WorldEnum.Mods)
			newLabel.Visible = false;
	}
	public void HideOption() => animator.Play(HideAnimation);

	private void ApplySettings()
	{
		float best = SaveManager.TimeData.GetBestTimeForLevel(data);
		if (best == -1)
			best = 0;

		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
			timeLabel.Text = ExtensionMethods.FormatTime(best);

		if (missionLabel != null)
		{
			if (!Engine.IsEditorHint() && !IsUnlocked)
				missionLabel.Text = "mission_locked";
			else
				missionLabel.Text = string.IsNullOrEmpty(data.MissionTypeKey) ? "Mission Name" : data.MissionTypeKey;
		}

		if (TimeAttackManager.Instance.IsRunActive)
		{
			fireSoulParent.Visible = false;
			light.Visible = false;
			storyMarker.Visible = false;
			newLabel.Visible = false;
			timeLabel.Visible = true;
		}
		else if (fireSoulParent != null)
			fireSoulParent.Visible = data.HasFireSouls && (Engine.IsEditorHint() || IsUnlocked);
	}

	/// <summary> Updates level's visual data based on the player's save data. </summary>
	public void UpdateLevelData()
	{
		if (IsUnlocked)
		{
			if (SaveManager.ActiveGameData.CurrentStoryLevel == data)
			{
				animator.Play(StoryAnimation);
				animator.AnimationSetNext(ShowAnimation, StoryAnimation + LoopAnimation);
			}
			else
			{
				animator.Play("RESET");
				animator.Advance(0.0);
				ClearState = SaveManager.ActiveGameData.LevelData.GetClearStatus(data.LevelID);
				switch (ClearState)
				{
					case SaveManager.LevelSaveData.LevelStatus.New:
						EmitSignal(SignalName.NewLevel);
						animator.Play(NewAnimation);
						break;
					case SaveManager.LevelSaveData.LevelStatus.Attempted:
						animator.Play(AttemptAnimation);
						break;
					case SaveManager.LevelSaveData.LevelStatus.Cleared:
						animator.Play(ClearAnimation);
						animator.AnimationSetNext(ShowAnimation, ClearAnimation + LoopAnimation);
						break;
				}
			}
		}
		else
		{
			animator.Play(AttemptAnimation); // Attempt animation also doubles as the locked animation
		}
		animator.Advance(0.0);

		if (data.HasFireSouls)
		{
			for (int i = 0; i < fireSoulRects.Length; i++)
			{
				bool isCollected = SaveManager.ActiveGameData.LevelData.IsFireSoulCollected(data.LevelID, i + 1);
				fireSoulRects[i].Texture = isCollected ? fireSoulSprite : noFireSoulSprite;
			}
		}
		if (TimeAttackManager.Instance.IsRunActive && TimeAttackManager.Instance.CurrentRunType == TimeAttackManager.RunType.SingleRun)
		{
			if (SaveManager.TimeData.HasRank(data))
				animator.Play(GoldAnimation);
			else
				animator.Play("no-medal");
		}
		else
		{
			switch (SaveManager.ActiveGameData.LevelData.GetRankClamped(data.LevelID))
			{
				case 1:
					animator.Play(BronzeAnimation);
					break;
				case 2:
					animator.Play(SilverAnimation);
					break;
				case 3:
					animator.Play(GoldAnimation);
					break;
				default:
					animator.Play(NoMedalAnimation);
					break;
			}
		}

		animator.Advance(0.0);
	}

	public void EnableTAInfo()
	{
		missionLabel.Visible = false;
		fireSoulParent.Visible = false;
		light.Visible = false;
		newLabel.Visible = false;
		rank.Visible = false;

		timeAttackLevelOption.Visible = true;
		missionLabelTA.Text = data.MissionTypeKey;
		areaLabelTA.Text = Tr(data.GetAreaKey());
	}

	public void DeleteTimesForLevel()
	{
		SaveManager.TimeData.DeleteTimesForLevel(data);
		timeLabel.Text = "00:00.00";
		UpdateLevelData();
		ApplySettings();
		
	}
}
