using Godot;
using System;
using Godot.Collections;
using Project.Gameplay;
using System.Linq;
using System.Collections.Generic;

namespace Project.Core;

public partial class TimeAttackManager : Node
{

	public static TimeAttackManager Instance;
	public enum RunType
	{
		AnyP,
		GoalPercent,
		SingleRun,
		BossRush,
	}

	public RunType CurrentRunType { get; private set; }

	private Array<float> CurrentRunTimes;

	[Export] private LevelDataResource[] Levels_AnyPercent;
	[Export] private LevelDataResource[] Levels_GoalPercent;
	[Export] private LevelDataResource[] Levels_BossRush;
	public LevelDataResource Level_Single;
	public int CurrentLevel { get; private set; }
	public bool IsRunActive { get; private set; }
	public bool LoadIntoSingle { get; private set; }


	public override void _EnterTree()
	{
		Instance = this;
	}
	public void SetRunType(RunType type)
	{
		CurrentRunType = type;
	}

	public LevelDataResource[] GetCurrentRunLevels(RunType type)
	{
		switch (type)
		{
			case RunType.AnyP:
				return Levels_AnyPercent;
			case RunType.GoalPercent:
				return Levels_GoalPercent;
			case RunType.BossRush:
				return Levels_BossRush;
		}
		return Levels_AnyPercent;
	}
	public LevelDataResource[] GetCurrentRunLevels()
	{
		switch (CurrentRunType)
		{
			case RunType.AnyP:
				return Levels_AnyPercent;
			case RunType.GoalPercent:
				return Levels_GoalPercent;
			case RunType.BossRush:
				return Levels_BossRush;
		}
		return Levels_AnyPercent;
	}

	///<summary> Gets all levels of the selected run </summary>
	public LevelDataResource[] GetCurrentRun()
	{
		return GetCurrentRunLevels(CurrentRunType);
	}

	///<summary> Gets the current level of the run being played </summary>
	public LevelDataResource GetCurrentLevel()
	{
		if (CurrentRunType != RunType.SingleRun)
			return GetCurrentRunLevels(CurrentRunType)[CurrentLevel];
		else
			return Level_Single;
	}
	///<summary> Gets the next level of the run being played </summary>
	public LevelDataResource GetNextLevel()
	{
		return GetCurrentRunLevels(CurrentRunType)[CurrentLevel + 1];
	}

	///<summary> Are we on the last level? </summary>
	public bool IsLastLevel()
	{
		if (GetCurrentLevel() == GetCurrentRunLevels().Last() || CurrentRunType == RunType.SingleRun)
			return true;
		else
			return false;
	}
	public void IncreaseLevel() => CurrentLevel += 1;
	public void ResetLevelCount() => CurrentLevel = 0;

	public void SetRunActive(bool isActive) => IsRunActive = isActive;
	public void LoadLevel(LevelDataResource level)
	{
		SoundManager.instance.StageMusicPlayer.Stop();
		TransitionManager.QueueSceneChange(level.LevelPath);
		TransitionManager.StartTransition(new()
		{
			inSpeed = 1f,
			outSpeed = 0.5f,
			color = Colors.Black,
			loadAsynchronously = true,
			disableAutoTransition = true,
			showMissionDescription = true
		});
		TransitionManager.Instance.SetMissionDescriptionText(level.MissionTypeKey, level.MissionDescriptionKey);
		TransitionManager.Instance.UpdateLoadingText("load_level");
	}

	public void LoadResults()
	{
		TransitionManager.QueueSceneChange(TransitionManager.TimeAttackResultsPath);
		TransitionManager.StartTransition(new()
		{
			inSpeed = 0.2f,
			outSpeed = 0.5f,
			color = Colors.Black,
			disableAutoTransition = false
		});
		ClearCurrentSavedRun();
	}

	public void LoadTimeAttack()
	{
		TransitionManager.QueueSceneChange(TransitionManager.TimeAttackScenePath);
		TransitionManager.StartTransition(new()
		{
			inSpeed = 0.2f,
			outSpeed = 0.5f,
			color = Colors.Black,
			disableAutoTransition = false
		});
	}

	//<summary>Makes it so Time Attack immediately loads into Single Run mode</summary>
	public void LoadTimeAttack(bool loadIntoSingle)
	{
		LoadIntoSingle = loadIntoSingle;
		LoadTimeAttack();
	}

	public void RestartRun()
	{
		ResetRunTimes();
		ResetLevelCount();
		ClearCurrentSavedRun();
		ClearCurrentRun();
		if (Instance.CurrentRunType != RunType.SingleRun)
			LoadLevel(GetCurrentLevel());
	}

	public void ClearCurrentSavedRun()
	{
		SaveManager.TimeData.CurrentPlacement = 0;
		SaveManager.TimeData.RunInProgress = [];
		SaveManager.SaveTimeAttackData();
	}

	public void ClearCurrentRun()
	{
		CurrentLevel = 0;
		if (GetCurrentRun() != null)
			CurrentRunTimes = new Array<float>();

	}

	public void AddTime(float time)
	{
		CurrentRunTimes.Add(time);
		SaveManager.TimeData.RunInProgress = CurrentRunTimes;
	}

	public Array<float> GetCurrentRunTimes()
	{
		return CurrentRunTimes;
	}
	public float GetTotalRunTime()
	{
		return CurrentRunTimes.Sum();
	}

	public bool IsPersonalBest(float time)
	{
		switch (CurrentRunType)
		{
			case RunType.AnyP:
				List<List<float>> anyP = new List<List<float>>();
				for (int i = 0; i < SaveManager.TimeData.AnyP.Count; i++)
				{
					anyP.Add(new List<float>());
					for (int k = 0; k < SaveManager.TimeData.AnyP[i].Count; k++)
					{
						anyP[i].Add(SaveManager.TimeData.AnyP[i][k]);
					}
				}
				anyP = anyP.OrderBy(list => list.Sum()).ToList();

				if (time == anyP[0].Sum())
					return true;
				break;
			case RunType.GoalPercent:
				List<List<float>> goalP = new List<List<float>>();

				for (int i = 0; i < SaveManager.TimeData.GoalP.Count; i++)
				{
					goalP.Add(new List<float>());
					for (int k = 0; k < SaveManager.TimeData.GoalP[i].Count; k++)
					{
						goalP[i].Add(SaveManager.TimeData.GoalP[i][k]);
					}
				}
				if (time == goalP[0].Sum())
					return true;
				break;
			case RunType.BossRush:

				List<List<float>> bossRush = new List<List<float>>();
				for (int i = 0; i < SaveManager.TimeData.BossRush.Count; i++)
				{
					bossRush.Add(new List<float>());
					for (int k = 0; k < SaveManager.TimeData.BossRush[i].Count; k++)
					{
						bossRush[i].Add(SaveManager.TimeData.BossRush[i][k]);
					}
				}

				bossRush = bossRush.OrderBy(list => list.Sum()).ToList();
				if (time == bossRush[0].Sum())
					return true;
				break;
		}
		return false;
	}

	public void SetReturnTimes()
	{
		CurrentRunTimes = SaveManager.TimeData.RunInProgress;
		CurrentRunType = SaveManager.TimeData.CurrentRunType;
		CurrentLevel = SaveManager.TimeData.CurrentPlacement;
	}

	public void ShouldLoadIntoSingle(bool single) => LoadIntoSingle = single;
	private void ResetRunTimes()
	{
		CurrentRunTimes = new Array<float>();
	}




}
