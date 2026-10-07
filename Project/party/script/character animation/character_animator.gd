### Interface for handling animations of a party character.
class_name CharacterAnimator extends Node3D

signal animation_event(info : int)

@export var skeleton : Skeleton3D
@export var animator : AnimationPlayer
@export var voice_player : AudioStreamPlayer
@export var data : PartyCharacterResource

@export var invincibility_animator : AnimationPlayer
@export var invincibility_animation_curve : Curve

## The index of this character
var player_index : int
## Should this animation player automatically play the results animation?
var autoplay_result_anim : bool = true

## Updates the invincibility animator.
func process_invincibility_timer(time_remaining : float) -> void:
	if is_zero_approx(time_remaining):
		invincibility_animator.play("RESET")
		invincibility_animator.advance(0.0)
		return
	
	if invincibility_animator.current_animation != "loop":
		invincibility_animator.play("loop")
	invincibility_animator.speed_scale = invincibility_animation_curve.sample(time_remaining)

## Plays a voice clip.
@rpc("any_peer", "call_local", "reliable")
func play_voice(key : String, index : int = -1) -> void:
	if data == null || data.voice_library == null:
		return
	
	var stream : AudioStream = data.get_voice_stream(key, index)
	voice_player.stream = stream
	voice_player.play()

func is_voice_playing() -> bool:
	return voice_player.playing

# TODO Add support for more complex character animations (i.e. blending)
func _ready() -> void:
	player_index = PartyManager.get_character_index(data)
	animator.deterministic = true
	
	if player_index == -1:
		return
	
	if MinigameManager.instance != null && data != null:
		MinigameManager.instance.minigame_finished.connect(Callable.create(self, "on_minigame_finished"))
		MinigameManager.instance.results_started.connect(Callable.create(self, "on_results_started"))

## Emits a signal from an animation. Use this to send data to the generic minigame controller.
func emit_animation_event(info : int) -> void:
	emit_signal("animation_event", info)

## Returns whether an animation exists or not.
func has_animation(anim : StringName) -> bool:
	return animator.has_animation(anim)

## Plays an animation locally.
func play_animation(anim : StringName, reset : bool = false, blend : float = 0.0) -> void:
	if animator == null || !has_animation(anim):
		print("Anim %s not found!" % anim)
		return
	
	if !reset && animator.assigned_animation == anim:
		print("Already playing %s!" % anim)
		return
	
	animator.play(anim, blend)
	print("Playing %s!" % anim)
	
	if reset:
		animator.seek(0.0, true)

## Gets the current animator's speed.
func get_speed() -> float:
	return animator.speed_scale

## Gets the current animation position.
func get_animation_position() -> float:
	return animator.current_animation_position

## Gets the current animation length.
func get_animation_length() -> float:
	return animator.current_animation_length

## Sets the current animator's speed.
func set_speed(value : float) -> void:
	animator.speed_scale = value

func get_current_animation() -> String:
	return animator.assigned_animation

## Plays a specific mini-game animation on the animator.
@rpc("any_peer", "call_local", "reliable")
func play_minigame_animation(anim : StringName, blend : float = 0.0, speed : float = 1.0, seek : float = 0.0, tick : float = 0.0) -> void:
	if animator == null || !has_animation(anim):
		return
	
	animator.play("%s" % anim, blend, speed)
	print("Playing%s" % anim)
	if !is_zero_approx(tick):
		seek += NetworkTimeSynchronizer.get_time() - tick
	seek = fmod(seek, get_animation_length())
	animator.seek(seek, true)

## Seeks an animation to the given network tick.
func network_seek(original_tick : float) -> void:
	if !NetworkManager.is_online:
		return
	animator.advance(NetworkTimeSynchronizer.get_time() - original_tick)

func queue_minigame_animation(anim : StringName, blend : float = 0.0) -> void:
	animator.set_blend_time(animator.assigned_animation, anim, blend)
	animator.queue(anim)

## Warps this animator to the correct results location.
func on_minigame_finished() -> void:
	if !PartyManager.minigame_players.has(player_index):
		return
	
	if MinigameManager.instance.is_canoe_minigame:
		get_parent().get_parent().reparent(MinigameManager.instance.results_location[player_index])
		get_parent().get_parent().set_deferred("transform", Transform3D.IDENTITY)
	else:
		reparent(MinigameManager.instance.results_location[player_index])
		set_deferred("transform", Transform3D.IDENTITY)
		call_deferred("play_animation", "%s/wait" % MinigameManager.COMMON_ANIMATION_LIBRARY_PREFIX)

## Play victory or loss animation
func on_results_started() -> void:
	if !PartyManager.minigame_players.has(player_index):
		return
	
	if autoplay_result_anim:
		play_result_animation()

func play_result_animation() -> void:
	set_speed(1)
	if PartyManager.get_player_data(player_index).minigame_placement == 0:
		play_animation("canoe-win" if MinigameManager.instance.is_canoe_minigame else "win")
		play_voice("win%s" % randi_range(1, 2))
	elif MinigameManager.instance.is_tie:
		play_animation("canoe-draw" if MinigameManager.instance.is_canoe_minigame else "draw")
		play_voice("draw")
	else:
		play_animation("canoe-lose" if MinigameManager.instance.is_canoe_minigame else "lose")

## Loads a given animation library to the animator.
func load_animation_library(library_name : String, library : AnimationLibrary) -> void:
	animator.add_animation_library(library_name, library);
