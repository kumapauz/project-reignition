### Represents a player in the Weight Puzzle minigame.
extends PartyGameCharacterSpawner

@export var coin_box: Node3D
@export var coin_meshes: Array[Node3D]
@export var coin_particles: Array[GPUParticles3D]
@export var weight_platform2: Node3D
@export var coinbox_offset: Node3D
@export var weight_animator: AnimationPlayer
@export var hand_attachment: BoneAttachment3D
@export var debug_label: Label3D

var zeroout_hands: bool = false

var is_demo_complete: bool
##Can the player tilt the bucket
var can_pour : bool
##The current number of coins in the bucket
var num_coins: int
##A visual representation of how many coins are in the bucket
var num_coins_visual: int
var current_input: float

##The amount of coins needed to win the minigame
const COINS_TO_REACH: int = 50

func on_spawn_finished() -> void:
	super()
	
	num_coins = 0
	num_coins_visual = 0
	hand_attachment.reparent(character_animator.skeleton)
	MinigameManager.instance.gameplay_started.connect(Callable(self, "send_chest"))
	character_animator.play_animation("%s/wait" % MinigameManager.COMMON_ANIMATION_LIBRARY_PREFIX, true, 0.1)

func _physics_process(delta: float) -> void:
	if player_index == 0:
		print("Processing Player!%s" % is_demo_complete)
	if can_pour:
		current_input = get_vertical_input()
		process_animation()

func _process(delta: float) -> void:
	debug_label.text = str(num_coins)

	if zeroout_hands:
		zero_hands()
	
func complete_demo() -> void:
	is_demo_complete = true
	set_physics_process(true)

	if is_minigame_host():
		var start_callable : Callable = Callable.create(MinigameManager.instance, "request_minigame_start")
		get_tree().create_timer(1).timeout.connect(start_callable)

#Have the player catch the chest
func catch_chest() -> void:
	set_zero(true)
	coin_box.reparent(hand_attachment)
	character_animator.play_animation("%s/19-catch" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)
	await get_tree().create_timer(1.2).timeout
	set_can_pour(true)

#Have the player return the chest to the weight
func throw_chest() -> void:
	character_animator.play_animation("%s/19-return" % MinigameManager.ANIMATION_LIBRARY_PREFIX, true)
	character_animator.queue_minigame_animation("%s/wait" % MinigameManager.COMMON_ANIMATION_LIBRARY_PREFIX, 0.0)

#Send the chest from the weight to the player
func send_chest() -> void:
	
	if num_coins < COINS_TO_REACH:
		coin_particles[0].emitting = true
		coin_particles[1].emitting = true
		coin_particles[2].emitting = true

		await get_tree().create_timer(0.5).timeout

		if num_coins == 0:
			await get_tree().create_timer(3).timeout
		elif num_coins > 0 && num_coins <= 25:
			await get_tree().create_timer(2).timeout
		elif num_coins > 25 && num_coins <= 40:
			await get_tree().create_timer(1).timeout
		elif num_coins > 40 && num_coins <= 50:
			await get_tree().create_timer(0.5).timeout
	
		coin_particles[0].emitting = false
		coin_particles[1].emitting = false
		coin_particles[2].emitting = false
		num_coins = 100

	weight_animator.play("weight_toss")
	
	

func unparent() -> void:
	print("Unparenting")
	coin_box.reparent(coinbox_offset)
	
func zero_hands() -> void:
	coin_box.position = Vector3.ZERO
	coin_box.rotation = Vector3.ZERO

func set_zero(zero: bool) -> void:
	zeroout_hands = zero

func tilt_box(type: int):
	match type:
		0:
			character_animator.queue_minigame_animation("%s/19-lift-wait" % MinigameManager.ANIMATION_LIBRARY_PREFIX, 0.0)
		1:
			character_animator.queue_minigame_animation("%s/19-slant1" % MinigameManager.ANIMATION_LIBRARY_PREFIX, 0.0)
		2:
			character_animator.queue_minigame_animation("%s/19-slant2" % MinigameManager.ANIMATION_LIBRARY_PREFIX, 0.0)

func set_can_pour(pour: bool):
	print("setting pour")
	can_pour = pour

#####################
### ROLLBACK CODE ###
#####################
@export var rollback_timer : RollbackTimer
const RB_INPUT : int = 0
func on_rollback_applied(rb_params : Array) -> void:
	current_input = rb_params[RB_INPUT]

func process_rollback() -> void:
	rollback_timer.set_param(RB_INPUT, current_input)
	rollback_timer.process_rollback()

func process_animation() -> void:
	var target_animation : String
	if current_input <= 0 : # If we're not tilting the stick
		target_animation = "%s/19-lift-wait" % MinigameManager.ANIMATION_LIBRARY_PREFIX
	elif current_input > 0 && current_input <= 0.8 : #If we're only tilting the stick part-way
		target_animation = "%s/19-slant1" % MinigameManager.ANIMATION_LIBRARY_PREFIX
	elif current_input > 0.8: #If we're fully tilting the stick
		target_animation = "%s/19-slant2" % MinigameManager.ANIMATION_LIBRARY_PREFIX
	
	if player_index == 0:
		print("Current Animation%s Target Animation:%s" % [character_animator.get_current_animation(), target_animation])
	
	if target_animation != null && character_animator.get_current_animation() != target_animation:
		character_animator.play_minigame_animation(target_animation)
