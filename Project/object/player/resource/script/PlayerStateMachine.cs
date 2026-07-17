using Godot;
using Godot.Collections;

namespace Project.Gameplay;

public partial class PlayerStateMachine : Node
{
	[Export] private NodePath startingState;
	public PlayerState CurrentState { get; private set; }
	public PlayerState QueuedState { get; private set; }
	[Export] private Array<NodePath> stateParents;

	public void Initialize(PlayerController player)
	{
		for (int i = 0; i < stateParents.Count; i++)
		{
			foreach (Node child in GetNode(stateParents[i]).GetChildren(true))
			{
				if (child is not PlayerState)
					continue;

				(child as PlayerState).Initialize(player);
			}
		}

		ResetStateMachine();
	}

	public void UnloadStateMachine() => CurrentState?.ExitState();

	/// <summary> Resets the state machine to its initial state. </summary>
	public void ResetStateMachine() => ChangeState(GetNode<PlayerState>(startingState));

	/// <summary> Exit the current state and switch to a new state. Initates the write done by PlayerState to cache speed of previous state to be used for Bound Jump. </summary>
	public void ChangeState(PlayerState state)
	{
		QueuedState = state;
		if (CurrentState != state)
		{
        	CurrentState?.CacheMomentumOnExit();
        	CurrentState?.ExitState();
    	}

		QueuedState = null;
		CurrentState = state;
		CurrentState.EnterState();
	}

	public void ProcessPhysics()
	{
		if (StageSettings.Instance.IsLevelLoading)
			return;

		PlayerState newState = CurrentState.ProcessPhysics();
		if (newState != null)
			ChangeState(newState);
	}
}
