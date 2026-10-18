### Basic behavior for spawning players. Extend this class as necessary.
### Note that actual behavior must be calculated in subclasses' physics_process().
class_name PartyGameCharacterSpawner extends Node3D

@export_range(-1, 3, 1) var player_index : int = 0
@export var spawn_position : Node3D
@export var character_animator : CharacterAnimator
## Option score counter. Used to display scores to the screen.
@export var score_counter : ScoreCounter
## Option race tracker. Used to display positions and completion amount (laps or percentages) to the screen.
@export var race_tracker : RaceTracker
## Option animation tree. Use initialize_animation_tree() to set it up.
@export var animation_tree : AnimationTree

@export var deactivate_on_gameplay_finished : bool = true
var _is_spawn_finished : bool
var _is_gameplay_finished : bool

##########################
### Invincibility Code ###
##########################
## How much longer the player should be invincible.
var _invincibility_timer : float
## Returns whether this character is invincible or not.
func is_invincible() -> bool:
	return !is_zero_approx(_invincibility_timer)

## Override for setting a player's invincibility without manually calling the RPC.
func request_invincibility(duration : float = 2.0) -> void:
	if !is_multiplayer_authority():
		return
	
	rpc("start_invincibility", duration, NetworkTimeSynchronizer.get_time())

## Sets the player's invincibility timer.
@rpc("any_peer", "call_local", "reliable")
func start_invincibility(duration : float, tick : float) -> void:
	_invincibility_timer = duration - (NetworkTimeSynchronizer.get_time() - tick)
	process_invincibility()

@rpc("any_peer", "call_local", "reliable")
func cancel_invincibility() -> void:
	_invincibility_timer = 0.0
	process_invincibility()

## Updates the invincibility timer. Call this from process_movement_tick().
func process_invincibility() -> void:
	character_animator.process_invincibility_timer(_invincibility_timer)
	_invincibility_timer = move_toward(_invincibility_timer, 0.0, get_physics_process_delta_time())

func initialize_animation_tree(anim_prefix : String, anim_list : PackedStringArray) -> void:
	animation_tree.anim_player = animation_tree.get_path_to(character_animator.animator)
	var root : AnimationNodeBlendTree = animation_tree.tree_root as AnimationNodeBlendTree
	for anim in anim_list:
		if !root.has_node(anim):
			continue
		var target_anim: StringName = anim_prefix + anim
		if !character_animator.animator.has_animation(target_anim):
			print("Couldn't find animation %s in %s. Using the default party animation library instead." % [anim, anim_prefix])
			target_anim = "%s/%s" % [MinigameManager.instance.COMMON_ANIMATION_LIBRARY_PREFIX, anim]
		(root.get_node(anim) as AnimationNodeAnimation).animation = target_anim
	animation_tree.active = true
	MinigameManager.instance.minigame_finished.connect(Callable(self, "deactivate_animation_tree"))

func deactivate_animation_tree() -> void:
	animation_tree.active = false

## Gets the animation prefix for minigame animations.
func get_anim_prefix() -> String:
	if player_index == -1:
		return "%02d-" % MinigameManager.instance.minigame_resource.minigame_index
	
	return "%s/" % MinigameManager.ANIMATION_LIBRARY_PREFIX

## Gets the input suffix for the player currently controlling this object.
func get_input_suffix() -> String:
	return str(PartyManager.get_player_data(player_index).local_player_index)

func get_horizontal_input() -> float:
	return Input.get_axis("move_left%s" % get_input_suffix(), "move_right%s" % get_input_suffix())

func get_vertical_input() -> float:
	return Input.get_axis("move_down%s" % get_input_suffix(), "move_up%s" % get_input_suffix())

func get_input_axis() -> Vector2:
	return Vector2(get_horizontal_input(), get_vertical_input()).limit_length()

## Returns whether this character is a cpu or not.
func is_cpu() -> bool:
	return player_index == -1 || PartyManager.get_player_data(player_index).is_cpu_player()

## Returns the difficulty of this cpu player.
func get_cpu_difficulty() -> PlayerData.CPU_DIFFICULTY_ENUM:
	if player_index == -1:
		return PlayerData.CPU_DIFFICULTY_ENUM.EASY
	return PartyManager.get_player_data(player_index).cpu_difficulty

func _ready() -> void:
	if NetworkManager.is_online && player_index != -1:
		# Set up authority
		var data : PlayerData = PartyManager.get_player_data(player_index)
		if !data.is_cpu_player():
			set_multiplayer_authority(data.device)
	
	if player_index == -1:
		# This is a demo character
		on_demo_spawned()
	else:
		set_physics_process(false)
		
		if is_player_valid():
			MinigameManager.instance.gameplay_started.connect(Callable.create(self, "activate"))
			MinigameManager.instance.gameplay_finished.connect(Callable.create(self, "on_gameplay_finished"))
			MinigameManager.instance.minigame_finished.connect(Callable.create(self, "on_minigame_finished"))
		else:
			# This player index is not being used. (i.e. tournament palace)
			visible = false
			set_process(false)
			MinigameManager.instance.disable_splitscreen_player(player_index)
		
		# Instance Player Model
		character_animator = MinigameManager.instance.load_character_model(player_index)
		spawn_position.add_child(character_animator)
		
		if is_instance_valid(score_counter): # Initialize the score counter
			score_counter.initialize_score_counter(player_index)
		if is_instance_valid(race_tracker): # Initialize the race tracker
			race_tracker.initialize_race_tracker(player_index)
	
	character_animator.connect("animation_event", Callable.create(self, "process_animation_event"))
	on_spawn_finished()
	
	if is_minigame_host():
		on_host_spawned()

## Returns whether this player is the minigame host.
## Due to Tournament Palace, this is not the same as the network host.
func is_minigame_host() -> bool:
	return player_index == PartyManager.minigame_players[0]

func is_player_valid() -> bool:
	return PartyManager.minigame_players.has(player_index)

## Called after the host has spawned.
func on_host_spawned() -> void:
	pass

## Called after spawn logic has finished.
func on_spawn_finished() -> void:
	_is_spawn_finished = true
	if !is_player_valid() && player_index != -1:
		queue_free()

func on_gameplay_finished() -> void:
	_is_gameplay_finished = true
	if deactivate_on_gameplay_finished:
		deactivate()

func on_minigame_finished() -> void:
	cancel_invincibility()
	disable_tree()
	deactivate()

## Called after a demo object has spawned. Default behavior: connect the demo_transition_processed signal.
func on_demo_spawned() -> void:
	# Hide demo nodes during the transition
	print("Connected demo transition process signal")
	MinigameManager.instance.demo_transition_processed.connect(Callable.create(self, "disable_tree"))

## Disables the node and its children. Used to disable demo nodes.
func disable_tree() -> void:
	visible = false
	process_mode = Node.PROCESS_MODE_DISABLED

## Called when recieving an animation event. Override in subclass.
func process_animation_event(_info : int) -> void:
	print("Received unhandled animation event.")

func activate() -> void:
	set_physics_process(true)

func deactivate() -> void:
	set_process(false)
	set_physics_process(false)
