### Instance this node into every minigame.
class_name MinigameManager extends Node

static var instance: MinigameManager

## Emitted when all network peers are ready to start the game. Start gameplay demos or call start_minigame() here.
signal peers_loaded
## Start reading player inputs and processing cpu players here. 
signal gameplay_started
## Emitted when a demo transition is processed.
signal demo_transition_processed
## Stop processing players here.
signal gameplay_finished
## Teleport players to their results position here.
signal minigame_finished
## Specifically fires when the number of registered "completed" players surpasses the set requirement.
signal players_completed
## Character Animators will play their result animations when this signal emits.
signal results_started
## Emitted whenever a player's score is changed.
signal on_score_updated(player_index: int, score: int)
## Emitted whenever a player's score is changed.
signal on_time_updated(player_index: int, time: float)

## Stores the network time when gameplay was started.
var gameplay_start_tick: float

func process_demo_transition() -> void:
	splitscreen_parent.visible = screen_mode == SCREEN_MODE.SPLITSCREEN
	demo_transition_processed.emit()

@export_group("Resource Settings")
@export var minigame_resource: MinigameResource
## Minigame specific animation library to attach to player when adding them to the scene.
@export var anim_library: AnimationLibrary
## Animations common to all minigames.
@export var common_anim_library: AnimationLibrary
const ANIMATION_LIBRARY_PREFIX: String = "MINIGAME"
const COMMON_ANIMATION_LIBRARY_PREFIX: String = "COMMON_MINIGAME"

@export_group("View Settings")
## How should the screen be set up for this mini-game?
@export var screen_mode: SCREEN_MODE
enum SCREEN_MODE {
	SHARED, # A single screen for everybody. Use this for sequential or group mini-games.
	SPLITSCREEN # Each player gets a corner of the screen. Use this for simultaneous screens.
}
@export var use_horizontal_duel_screen: bool
## Transition to use when exiting demo.
@export var demo_transition_mode: DEMO_TRANSITION
enum DEMO_TRANSITION {
	NONE, # Start with screen already split
	FULLSCREEN, # Show a fullscreen demo first (global environment, normally played by a majin)
	FULLSCREEN_PLAYER1 # Show a fullscreen demo first (use player 1's environment)
}

@export_group("Result Settings")
@export var rank_mode: RANK_MODE
enum RANK_MODE {
	SCORE, # Use scores to rank players; highest score wins
	TIME # Use times to rank players; lowest time wins
}
## Disable this if you have something in the game that needs to finish before we can play the results.
@export var autoplay_results: bool = true
## Disable this if you want the to do something after the transition. Call play_results_animation manually if you turn this off.
@export var autoplay_results_animation: bool = true
## Number of "survivors" that must be left for the minigame to autocomplete.
@export_range(0, 3, 1) var autocomplete_survivor_count: int = 0
## Option camera to use for the results screen.
@export var results_camera : Camera3D
## Should the players use their canoe minigame animations?
@export var is_canoe_minigame : bool
## Tracks whether we're ready to play the results screen or not (based on non-game elements).
var is_results_queued: bool
## Tracks whether we've already finished the minigame and entered the results screen.
var is_results_active: bool

@export_group("Components")
## Optional animator that plays when all peers are loaded. Use this for camera pans.
@export var intro_animator: AnimationPlayer
@export var splitscreen_parent: Control
@export var animator: AnimationPlayer

## Where to spawn players.
@export var subviewport_worlds: Array[SubViewport]
## Where to spawn players during the results screen.
@export var results_location: Array[Node3D]
## Labels for the mini-game winners.
@export var winner_labels: Array[SyncedLabel]

@export var score_popup_scene: PackedScene
## Pool for score popups
var score_popup_pool: Array[ScorePopup]
## List of active popups
var active_popups: Array[ScorePopup]
## Number of players that have completed the current mini-game.
var completed_player_count: int

## Tracks whether the minigame ended in a tie.
var is_tie: bool

## Tracks the player's scores.
var player_scores: Array[int]
## Tracks the player's times.
var player_times: Array[float]
##Tracks whether the minigame is finished
var is_minigame_finished: bool = false
const INITIAL_SCORE_POPUP_POOL_SIZE: int = 10

func _init() -> void:
	instance = self
	minigame_finished.connect(Callable(PauseManager, "request_cancel_pause_menu"))
	
	player_scores.resize(PartyManager.MAX_PLAYER_COUNT)
	player_times.resize(PartyManager.MAX_PLAYER_COUNT)
	for i in player_times.size():
		player_times[i] = INF
	
	if !PartyManager.is_player_data_initialized():
		PartyManager.initialize_offline_player_data()
		PartyManager.initialize_debug_characters()

func _ready() -> void:
	for i in range(INITIAL_SCORE_POPUP_POOL_SIZE):
		generate_score_popup()
	
	for i in results_location.size():
		results_location[i].visible = false
	
	if PartyManager.current_mode == PartyManager.CURRENT_MODE_ENUM.TOURNAMENT_PALACE:
		# Change splitscreen mode if in Tournament Palace (2 players)
		animator.play("duel_horizontal" if use_horizontal_duel_screen else "duel_vertical")
	else:
		animator.play("free-for-all")
	animator.advance(0.0)
	
	if demo_transition_mode == DEMO_TRANSITION.FULLSCREEN_PLAYER1:
		animator.play("demo-p1-init")
	elif screen_mode == SCREEN_MODE.SHARED || demo_transition_mode == DEMO_TRANSITION.FULLSCREEN:
		animator.play("demo-init") # NOTE: This animation is the same as a split-screen demo.
	animator.advance(0.0)
	
	if NetworkManager.is_online:
		NetworkManager.peers_loaded.connect(Callable(self, "start_party_game"), CONNECT_DEFERRED)
	else:
		call_deferred("start_party_game")

## Disables a splitscreen player.  
func disable_splitscreen_player(index: int) -> void:
	subviewport_worlds[index].get_parent().visible = false
	subviewport_worlds[index].set_process(false)
	subviewport_worlds[index].set_physics_process(false)

func _exit_tree() -> void:
	if NetworkManager.peers_loaded.is_connected(Callable(self, "start_party_game")):
		NetworkManager.peers_loaded.disconnect(Callable(self, "start_party_game"))

func start_party_game() -> void:
	print("peers are loaded!")
	peers_loaded.emit()
	if is_instance_valid(intro_animator) && intro_animator.has_animation("intro"):
		print("playing intro animation!")
		intro_animator.play("intro")

func load_character_model(player_index: int) -> CharacterAnimator:
	var scene: PackedScene = load(PartyManager.get_player_data(player_index).character_data.model_file) as PackedScene
	var character: CharacterAnimator = scene.instantiate() as CharacterAnimator
	if common_anim_library != null:
		character.load_animation_library(COMMON_ANIMATION_LIBRARY_PREFIX, common_anim_library)
	
	if anim_library != null:
		character.load_animation_library(ANIMATION_LIBRARY_PREFIX, anim_library)
	
	return character

## Plays an animation, synced across the network.
@rpc("any_peer", "call_local", "reliable")
func play_animation(anim: String) -> void:
	animator.play(anim)

func generate_score_popup() -> void:
	var new_popup: Node = score_popup_scene.instantiate()
	add_child(new_popup)
	new_popup.repool.connect(Callable.create(self, "on_repool_score_popup").bind(new_popup))
	score_popup_pool.append(new_popup)

## Repools a score popup after it fades away.
func on_repool_score_popup(popup: ScorePopup) -> void:
	score_popup_pool.append(popup)
	var index: int = active_popups.find(popup)
	if index != -1:
		active_popups.remove_at(index)

func request_score_popup(player_index: int, amount: int, pos: Vector2) -> void:
	if !PartyManager.minigame_players.has(player_index):
		return
	
	if screen_mode == SCREEN_MODE.SPLITSCREEN: # Account for splitscreen
		pos += subviewport_worlds[player_index].get_parent().position
	
	rpc("_score_popup", player_index, amount, pos, NetworkTimeSynchronizer.get_time())

func request_score_popup_abort(player_index: int, time: float) -> void:
	rpc("_score_popup_abort", player_index, time)

@rpc("any_peer", "call_local", "reliable")
func _score_popup(player_index: int, amount: int, pos: Vector2, time: float) -> void:
	if score_popup_pool.is_empty():
		generate_score_popup()
	
	var popup: ScorePopup = score_popup_pool[0]
	score_popup_pool.remove_at(0)
	active_popups.append(popup)
	popup.show_popup(player_index, amount, pos, time)

@rpc("any_peer", "call_local", "reliable")
func _score_popup_abort(player_index: int, time: float) -> void:
	for popup: ScorePopup in active_popups:
		if popup.player_index == player_index && is_equal_approx(popup.spawn_time, time):
			popup.abort_popup()
			break

func request_score_change(player_index: int, amount: int = 1) -> void:
	if player_index < 0 || player_index > PartyManager.MAX_PLAYER_COUNT:
		return
	rpc("_change_score", player_index, amount)

func request_time_change(player_index: int, time: float) -> void:
	if player_index < 0 || player_index > PartyManager.MAX_PLAYER_COUNT:
		return
	rpc("_change_time", player_index, time)

## Changes the score of a player.
@rpc("any_peer", "call_local", "reliable")
func _change_score(player_index: int, amount: int) -> void:
	player_scores[player_index] += amount
	on_score_updated.emit(player_index, player_scores[player_index])

## Changes the time of a player.
@rpc("any_peer", "call_local", "reliable")
func _change_time(player_index: int, time: float) -> void:
	player_times[player_index] = time
	on_time_updated.emit(player_index, player_times[player_index])

## Adds one completed player and checks whether we should finish the mini-game.
func register_completed_player() -> void:
	completed_player_count += 1
	if is_results_active && completed_player_count >= PartyManager.minigame_players.size() - autocomplete_survivor_count:
		players_completed.emit()
		return
	
	if completed_player_count >= PartyManager.minigame_players.size() - autocomplete_survivor_count:
		players_completed.emit()
		request_minigame_finish()

func request_minigame_start() -> void:
	print("Starting Minigame!")
	if NetworkManager.is_hosting_game:
		rpc("start_minigame", NetworkManager.calculate_transition_tick())

## Plays the "START!" animation.
@rpc("any_peer", "call_local", "reliable")
func start_minigame(tick: float) -> void:
	var target_animation: String = "minigame-start"
	if demo_transition_mode != DEMO_TRANSITION.NONE:
		target_animation = "demo-fade" # Transition to split-screen
	
	var callable: Callable = Callable(self, "play_animation").bind(target_animation)
	get_tree().create_timer(NetworkManager.calculate_transition_delay(tick)).timeout.connect(callable)

func request_minigame_finish(from_timer: bool = false) -> void:
	print("Finishing Minigame!")
	if NetworkManager.is_hosting_game:
		rpc("finish_minigame", from_timer)

## Attempts to start the results screen from another animation.
func attempt_autoplay_results() -> void:
	if !NetworkManager.is_hosting_game:
		return
	if autoplay_results:
		rpc("play_animation", "results-start")
	else:
		is_results_queued = true

## Call this after all minigame objects are finished processing.
@rpc("any_peer", "call_local", "reliable")
func request_autoplay_results() -> void:
	if !NetworkManager.is_hosting_game:
		return
	autoplay_results = true
	if is_results_queued:
		attempt_autoplay_results()

## Plays the "GAME SET!" animation, then starts the results screen.
@rpc("any_peer", "call_local", "reliable")
func finish_minigame(from_timer: bool) -> void:
	print("Gameplay Finished.")
	gameplay_finished.emit()
	PauseManager.disable_pause_inputs()
	rpc("play_animation", "minigame-time" if from_timer else "minigame-finish")

## Emits the signal to actually enable gameplay objects.
func on_gameplay_started() -> void:
	gameplay_start_tick = NetworkTimeSynchronizer.get_time()
	gameplay_started.emit()
	PauseManager.enable_pause_inputs()

## Emits the signal to teleport players to the results screen.
func on_minigame_finished() -> void:
	if is_instance_valid(intro_animator) && intro_animator.has_animation("finish"):
		intro_animator.play("finish")
	
	is_minigame_finished = true
	minigame_finished.emit()
	
	for i in results_location.size():
		results_location[i].visible = true
	
	splitscreen_parent.visible = false # Hide splitscreen stuff
	if results_camera != null:
		results_camera.make_current()

## Calculate the minigame winners and plays the proper results screen.
func attempt_start_results() -> void:
	is_results_active = true
	if !NetworkManager.is_hosting_game || !autoplay_results_animation:
		return
	
	start_results_animation()

## Calculate the minigame winners and plays the proper results screen.
func start_results_animation() -> void:
	if !NetworkManager.is_hosting_game:
		return
	
	var rankings: Array[int]
	rankings.resize(PartyManager.MAX_PLAYER_COUNT)
	
	is_tie = check_tie()
	if is_tie: # Force everyone to lose if it's a tie.
		for i in rankings.size():
			rankings[i] = PartyManager.MAX_PLAYER_COUNT # This forces everybody to "lose."
	else: # Figure out the proper placement for each player
		if rank_mode == RANK_MODE.TIME:
			for i in player_times.size():
				var rank: int = 0
				if !PartyManager.minigame_players.has(i):
					rank = PartyManager.MAX_PLAYER_COUNT
				else:
					for j in i:
						if player_times[j] < player_times[i]:
							rank += 1
						else:
							rankings[j] += 1
				rankings[i] = rank
		else:
			for i in player_scores.size():
				var rank: int = 0
				if !PartyManager.minigame_players.has(i):
					rank = PartyManager.MAX_PLAYER_COUNT
				else:
					for j in i:
						if player_scores[j] > player_scores[i]:
							rank += 1
						elif player_scores[j] < player_scores[i]:
							rankings[j] += 1
				rankings[i] = rank
	
	# Store rankings to PartyManager
	for i in PartyManager.MAX_PLAYER_COUNT:
		PartyManager.rpc("set_minigame_placement", i, rankings[i])
	
	if !is_tie:
		rpc("update_win_text")
	rpc("play_animation", "results-draw" if is_tie else "results-win") # Play the correct results animation

## Returns true if everybody has the same score.
func check_tie() -> bool:
	if rank_mode == RANK_MODE.TIME: # Can't tie in timed mode
		return false
	
	for i in range(1, player_scores.size()): # Check all scores against P1's score
		if player_scores[i] != player_scores[0]:
			return false
	return true

## Updates the names of the winners for the results screen.
@rpc("any_peer", "call_local", "reliable")
func update_win_text() -> void:
	var label_index: int = 0
	for i in PartyManager.MAX_PLAYER_COUNT:
		var data : PlayerData = PartyManager.get_player_data(i)
		print("%s: Player %s placed %s with a score of %s and a time of %s" % [multiplayer.get_unique_id(), data.character_data.character_name, data.minigame_placement, player_scores[i], player_times[i]])
		
		if data.minigame_placement != 0:
			continue
		winner_labels[label_index].set_synced_text(data.character_data.character_name)
		label_index += 1
	
	for i in range(label_index, winner_labels.size()):
		winner_labels[i].modulate = Color.TRANSPARENT # Hide unused labels
	animator.play("results-solo-winner" if label_index <= 1 else "results-multi-winner")
	animator.advance(0.0)

func on_results_started() -> void:
	results_started.emit()

## Cleans up all nodes related to this particular mini-game.
func on_results_finished() -> void:
	if NetworkManager.is_online && !NetworkManager.is_hosting_game:
		return
	# Return to the active attraction
	NetworkManager.rpc("unload_scene", minigame_resource.scene_path, NetworkManager.TRANSITION_TYPE_ENUM.ATTRACTION)
